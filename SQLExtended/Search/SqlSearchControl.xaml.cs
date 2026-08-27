using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using SQLExtended.Monitoring.Jobs;
using SQLExtended.Settings;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLExtended.Search;

/// <summary>
/// WPF control for the SQL Search tool window.
/// Server/database/type multi-select filters with schema preview pane.
/// </summary>
public partial class SqlSearchControl : UserControl
{
    private CancellationTokenSource _searchCts;
    private System.Windows.Threading.DispatcherTimer _debounceTimer;
    private List<ObjectExplorerHelper.ServerInfo> _servers = new();
    private bool _isLoadingServers;

    /// <summary>
    /// Set by the Object Explorer context menu before the window is shown, to pre-select a specific
    /// server (by connection key) and database. Consumed once on the next <see cref="LoadServers"/>.
    /// </summary>
    internal static (string ConnKey, string Database)? PendingTarget;
    private string _pendingTargetDatabase;
    private readonly SearchTermHighlighter _searchHighlighter = new();
    private readonly SearchTermHighlighter _tempTableHighlighter = new();

    /// <summary>
    /// SERVERPROPERTY('ServerName') for the searched server, learned from the job step search's probe. The
    /// Job Properties dialog needs the SMO server name in the job's URN, which behind an AG listener or a
    /// CNAME is not the connection string's Data Source.
    /// </summary>
    private string _jobServerName;

    public SqlSearchControl()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        InitializeComponent();
        InitializeSyntaxHighlighting();
        PreviewEditor.TextArea.TextView.LineTransformers.Add(_searchHighlighter);
        TempTableEditor.TextArea.TextView.LineTransformers.Add(_tempTableHighlighter);

        _debounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _debounceTimer.Tick += (s, e) =>
        {
            _debounceTimer.Stop();
            ExecuteSearch();
        };

        Loaded += (s, e) =>
        {
            SearchTextBox.Focus();
            LoadServers();
        };

        SchemaCache.Instance.CacheRefreshed += (s, e) =>
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateConnectionInfo()));
        };
    }

    // --- Server / Database population ---

    private void LoadServers()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _isLoadingServers = true;
        try
        {
            _servers = ObjectExplorerHelper.GetConnectedServers();
            ServerCombo.Items.Clear();
            foreach (var server in _servers)
                ServerCombo.Items.Add(new ComboBoxItem { Content = server.DisplayName, Tag = server });

            // A pending target (from the Object Explorer context menu) wins over the active connection.
            var target = PendingTarget;
            PendingTarget = null;
            string preferredKey = target?.ConnKey;
            _pendingTargetDatabase = target?.Database;

            if (string.IsNullOrEmpty(preferredKey))
            {
                try
                {
                    string activeConn = ConnectionHelper.GetActiveConnectionString();
                    if (!string.IsNullOrEmpty(activeConn))
                        preferredKey = SchemaCache.Instance.GetConnectionKey(activeConn);
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(preferredKey))
            {
                for (int i = 0; i < _servers.Count; i++)
                {
                    if (string.Equals(_servers[i].ServerName, preferredKey, StringComparison.OrdinalIgnoreCase))
                    {
                        ServerCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (ServerCombo.SelectedIndex < 0 && ServerCombo.Items.Count > 0)
                ServerCombo.SelectedIndex = 0;

            // SelectionChanged is suppressed while loading, so trigger the DB load ourselves when we
            // either have a single server or a pending target whose database we want to pre-select.
            if (ServerCombo.Items.Count == 1 || !string.IsNullOrEmpty(_pendingTargetDatabase))
                LoadDatabasesForSelectedServer();
        }
        finally
        {
            _isLoadingServers = false;
        }

        UpdateConnectionInfo();
    }

    private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_isLoadingServers) return;
        LoadDatabasesForSelectedServer();
    }

    private void LoadDatabasesForSelectedServer()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        DatabaseList.Items.Clear();

        var serverInfo = GetSelectedServer();
        if (serverInfo == null) return;

        StatusText.Text = "Loading databases...";

        string connStr = serverInfo.ConnectionString;
        if (string.IsNullOrEmpty(connStr))
        {
            // Try to get connection from active editor if server matches
            try
            {
                string active = ConnectionHelper.GetActiveConnectionString();
                if (!string.IsNullOrEmpty(active))
                {
                    string activeKey = SchemaCache.Instance.GetConnectionKey(active);
                    if (string.Equals(activeKey, serverInfo.ServerName, StringComparison.OrdinalIgnoreCase))
                        connStr = active;
                }
            }
            catch { }
        }

        if (string.IsNullOrEmpty(connStr))
        {
            StatusText.Text = "No connection available for this server";
            return;
        }

        // Store for later use
        serverInfo.ConnectionString = connStr;

        _ = Task.Run(() =>
        {
            var databases = ObjectExplorerHelper.GetDatabases(connStr);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                DatabaseList.Items.Clear();
                foreach (string db in databases)
                    DatabaseList.Items.Add(db);

                // Pre-select the pending target database (from the OE menu) when present; else select all.
                string pendingDb = _pendingTargetDatabase;
                _pendingTargetDatabase = null;
                if (!string.IsNullOrEmpty(pendingDb) && databases.Contains(pendingDb))
                    DatabaseList.SelectedItem = pendingDb;
                else
                    DatabaseList.SelectAll();

                StatusText.Text = $"{databases.Count} database(s) found";
            }));
        });
    }

    private void RefreshServers_Click(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        LoadServers();
    }

    private void SelectAllDatabases_Click(object sender, RoutedEventArgs e)
    {
        DatabaseList.SelectAll();
    }

    private void SelectNoDatabases_Click(object sender, RoutedEventArgs e)
    {
        DatabaseList.UnselectAll();
    }

    private ObjectExplorerHelper.ServerInfo GetSelectedServer()
    {
        return (ServerCombo.SelectedItem as ComboBoxItem)?.Tag as ObjectExplorerHelper.ServerInfo;
    }

    private List<string> GetSelectedDatabases()
    {
        var result = new List<string>();
        foreach (var item in DatabaseList.SelectedItems)
        {
            if (item is string s)
                result.Add(s);
            else
                result.Add(item?.ToString() ?? "");
        }
        return result;
    }

    private string GetTypeFilter()
    {
        var types = new List<string>();
        if (ChkTypeTables.IsChecked == true) types.Add("U");
        if (ChkTypeViews.IsChecked == true) types.Add("V");
        if (ChkTypeProcs.IsChecked == true) types.Add("P");
        if (ChkTypeFunctions.IsChecked == true) { types.Add("FN"); types.Add("IF"); types.Add("TF"); }

        // If all or none selected, return null (no filter)
        if (types.Count == 0 || types.Count >= 7) return null;
        return string.Join(",", types);
    }

    // --- Search ---

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _debounceTimer.Stop();
            ExecuteSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchTextBox.Clear();
            ResultsList.ItemsSource = null;
            ClearPreview();
            StatusText.Text = "Ready \u2014 type to search";
            e.Handled = true;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text) && SearchTextBox.Text.Length >= 2)
        {
            _debounceTimer.Start();
        }
        else if (string.IsNullOrEmpty(SearchTextBox.Text))
        {
            ResultsList.ItemsSource = null;
            ClearPreview();
            StatusText.Text = "Ready \u2014 type to search";
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => ExecuteSearch();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Clear();
        ResultsList.ItemsSource = null;
        ClearPreview();
        StatusText.Text = "Ready \u2014 type to search";
        SearchTextBox.Focus();
    }

    private void ExecuteSearch()
    {
        string searchTerm = SearchTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm))
            return;

        var serverInfo = GetSelectedServer();
        if (serverInfo == null)
        {
            StatusText.Text = "Select a server first";
            return;
        }

        var selectedDatabases = GetSelectedDatabases();
        if (selectedDatabases.Count == 0)
        {
            StatusText.Text = "Select at least one database";
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        string typeFilter = GetTypeFilter();
        var options = new SearchOptions
        {
            TypeFilter = typeFilter,
            SearchObjectNames = SearchObjectNamesCheck.IsChecked == true,
            SearchColumnNames = SearchColumnNamesCheck.IsChecked == true,
            SearchDefinitions = SearchDefinitionsCheck.IsChecked == true,
            MaxResults = 500
        };

        // Agent job steps are not part of SearchOptions: they live in msdb, belong to the server rather than to
        // any of the selected databases, and none of the cache plumbing SearchOptions drives knows about them.
        bool searchJobSteps = SearchJobStepsCheck.IsChecked == true;

        StatusText.Text = "Searching...";

        string connStr = serverInfo.ConnectionString;
        string connectionKey = SchemaCache.Instance.GetConnectionKey(connStr);

        // Ensure selected databases are cached
        _ = Task.Run(() => EnsureDatabasesCached(connStr, connectionKey, selectedDatabases));

        _ = Task.Run(async () =>
        {
            try
            {
                var results = new List<SearchResultViewModel>();
                var cache = SchemaCache.Instance;

                foreach (string db in selectedDatabases)
                {
                    if (token.IsCancellationRequested) return;
                    var dbResults = cache.Search(connectionKey, db, searchTerm, options);
                    results.AddRange(dbResults.Select(r => new SearchResultViewModel(r, db, connectionKey, connStr)));
                }

                if (token.IsCancellationRequested) return;

                // Job steps are read live from msdb, once for the server rather than per database. Appended
                // after the object results so the cached (instant) half of the answer keeps its usual order.
                JobStepSearchService.Result jobResult = null;
                if (searchJobSteps)
                {
                    jobResult = await JobStepSearchService.SearchAsync(connStr, searchTerm, options.MaxResults, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;
                    results.AddRange(jobResult.Matches.Select(m => new SearchResultViewModel(m, connectionKey, connStr)));
                }

                int jobCount = jobResult?.Matches.Count ?? 0;
                string jobWarning = jobResult?.Warning;
                string jobServer = jobResult?.ServerName;

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _jobServerName = jobServer ?? _jobServerName;
                    ResultsList.ItemsSource = results;

                    string message = results.Count == 0
                        ? "No matches found"
                        : $"{results.Count} match{(results.Count == 1 ? "" : "es")} found across {selectedDatabases.Count} database(s)";
                    if (jobCount > 0)
                        message += $", including {jobCount} Agent job step{(jobCount == 1 ? "" : "s")}";
                    // Never silently: a restricted login sees only its own jobs, and a missing Agent returns
                    // nothing at all — both are indistinguishable from "no job step matches" without this.
                    if (!string.IsNullOrEmpty(jobWarning))
                        message += " — " + jobWarning;
                    StatusText.Text = message;
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText.Text = $"Search error: {ex.Message}";
                }));
            }
        }, token);
    }

    private void EnsureDatabasesCached(string connectionString, string connectionKey, List<string> databases)
    {
        var cache = SchemaCache.Instance;
        foreach (string db in databases)
        {
            var state = cache.GetState(connectionKey, db);
            if (state == CacheState.NotLoaded || state == CacheState.Error)
            {
                string dbConnStr = ConnectionHelper.GetConnectionStringForDatabase(connectionString, db);
                _ = cache.LoadDatabaseAsync(dbConnStr, db);
            }
        }
    }

    private SearchOptions BuildSearchOptions()
    {
        return new SearchOptions
        {
            TypeFilter = GetTypeFilter(),
            SearchObjectNames = SearchObjectNamesCheck.IsChecked == true,
            SearchColumnNames = SearchColumnNamesCheck.IsChecked == true,
            SearchDefinitions = SearchDefinitionsCheck.IsChecked == true,
            MaxResults = 500
        };
    }

    private void UpdateConnectionInfo()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var server = GetSelectedServer();
            string db = null;
            try { db = ConnectionHelper.GetCurrentDatabaseName(); } catch { }

            ConnectionText.Text = server != null
                ? (string.IsNullOrEmpty(db) ? server.DisplayName : $"{server.DisplayName} / {db}")
                : "";
        }
        catch
        {
            ConnectionText.Text = "";
        }
    }

    // --- Syntax highlighting ---

    private void InitializeSyntaxHighlighting()
    {
        try
        {
            var assembly = typeof(SqlSearchControl).Assembly;
            using (var stream = assembly.GetManifestResourceStream("SQLExtended.Search.TsqlDarkHighlighting.xshd"))
            {
                if (stream != null)
                {
                    using (var reader = new XmlTextReader(stream))
                    {
                        var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                        PreviewEditor.SyntaxHighlighting = highlighting;
                        TempTableEditor.SyntaxHighlighting = highlighting;
                    }
                }
            }
        }
        catch { }
    }

    // --- Schema preview ---

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultViewModel vm)
            LoadSchemaPreview(vm);
        else
            ClearPreview();
    }

    private void LoadSchemaPreview(SearchResultViewModel vm)
    {
        if (vm.IsJobStep)
        {
            ShowJobStepPreview(vm);
            return;
        }

        bool isTable = vm.ObjectType == "U";

        TempTableTab.Visibility = isTable ? Visibility.Visible : Visibility.Collapsed;
        if (!isTable && PreviewTabs.SelectedItem == TempTableTab)
            PreviewTabs.SelectedItem = SchemaTab;

        string dbPrefix = !string.IsNullOrEmpty(vm.DatabaseName) ? $"{vm.DatabaseName}." : "";
        PreviewHeader.Text = $"{dbPrefix}{vm.SchemaName}.{vm.DisplayName} \u2014 loading...";
        PreviewHeader.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x56, 0x9C, 0xD6));
        PreviewEditor.Text = "";
        TempTableEditor.Text = "";

        string objectName = vm.MatchLocation == "ColumnName"
            ? $"{dbPrefix}{vm.SchemaName}.{vm.ObjectName}"
            : $"{dbPrefix}{vm.SchemaName}.{vm.DisplayName}";

        string targetConnStr = ConnectionHelper.GetConnectionStringForDatabase(vm.ConnectionString, vm.DatabaseName);

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            string schemaScript = null;
            Exception error = null;

            await Task.Run(() =>
            {
                try
                {
                    schemaScript = SchemaQueryService.GetSchemaScript(targetConnStr, objectName, vm.ConnectionKey);
                }
                catch (Exception ex) { error = ex; }
            });

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (error != null)
            {
                SetPreviewText(vm, $"-- Error: {error.Message}", isError: true);
            }
            else if (!string.IsNullOrEmpty(schemaScript))
            {
                SetPreviewText(vm, schemaScript, isError: false);

                if (isTable)
                {
                    var settings = SQLExtendedSettings.Load();
                    TempTableEditor.Text = TempTableScriptBuilder.Build(schemaScript, vm.ObjectName, settings.TempTableDropIfExists);
                }
            }
            else
            {
                SetPreviewText(vm, $"-- No schema found for '{objectName}'", isError: true);
            }
        });
    }

    /// <summary>
    /// Shows a job step's command in the preview pane. No round trip: the command text came back with the
    /// search itself, which is the whole reason job steps are read live rather than from a cache.
    ///
    /// The header is commented with <c>--</c> even for a CmdExec or PowerShell step. Those are not T-SQL and
    /// the pane's highlighting will be wrong for them regardless; what matters is that the command is shown
    /// verbatim below a header saying which subsystem runs it.
    /// </summary>
    private void ShowJobStepPreview(SearchResultViewModel vm)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var step = vm.JobStep;

        TempTableTab.Visibility = Visibility.Collapsed;
        if (PreviewTabs.SelectedItem == TempTableTab)
            PreviewTabs.SelectedItem = SchemaTab;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- Job:       {step.JobName}{(step.JobEnabled ? "" : "   (disabled)")}");
        sb.AppendLine($"-- Step {step.StepId}:    {step.StepName}");
        if (!string.IsNullOrEmpty(step.Subsystem))
            sb.AppendLine($"-- Subsystem: {step.Subsystem}");
        if (!string.IsNullOrEmpty(step.StepDatabase))
            sb.AppendLine($"-- Database:  {step.StepDatabase}");
        sb.AppendLine("-- " + new string('-', 70));
        sb.AppendLine();
        sb.Append(step.Command ?? "-- (this step has no command text)");

        PreviewHeader.Text = $"{step.JobName} — {vm.StepDisplay}";
        PreviewHeader.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x56, 0x9C, 0xD6));

        _searchHighlighter.SearchTerm = SearchTextBox.Text?.Trim();
        PreviewEditor.Text = sb.ToString();
        TempTableEditor.Text = "";
    }

    private void SetPreviewText(SearchResultViewModel vm, string text, bool isError)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            string dbPrefix = !string.IsNullOrEmpty(vm.DatabaseName) ? $"{vm.DatabaseName}." : "";
            string headerName = vm.MatchLocation == "ColumnName"
                ? $"{dbPrefix}{vm.SchemaName}.{vm.ObjectName}"
                : $"{dbPrefix}{vm.SchemaName}.{vm.DisplayName}";

            PreviewHeader.Text = headerName;
            PreviewHeader.Foreground = new System.Windows.Media.SolidColorBrush(
                isError
                    ? System.Windows.Media.Color.FromRgb(0xF1, 0x4C, 0x4C)
                    : System.Windows.Media.Color.FromRgb(0x56, 0x9C, 0xD6));

            _searchHighlighter.SearchTerm = isError ? null : SearchTextBox.Text?.Trim();
            PreviewEditor.Text = text;
        }));
    }

    private void ClearPreview()
    {
        PreviewHeader.Text = "Select a result to view schema";
        PreviewHeader.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
        _searchHighlighter.SearchTerm = null;
        _tempTableHighlighter.SearchTerm = null;
        PreviewEditor.Text = "";
        TempTableEditor.Text = "";
        TempTableTab.Visibility = Visibility.Collapsed;
        PreviewTabs.SelectedItem = SchemaTab;
    }

    // --- Result actions ---

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewSchemaDialogForSelectedResult();
    }

    private void ViewSchemaDialog_Click(object sender, RoutedEventArgs e)
    {
        ViewSchemaDialogForSelectedResult();
    }

    private void CopyName_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultViewModel vm)
        {
            try { Clipboard.SetText(vm.DisplayName); } catch { }
        }
    }

    private void CopyQualifiedName_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultViewModel vm)
        {
            try { Clipboard.SetText(vm.QualifiedName); } catch { }
        }
    }

    private void CopySchemaScript_Click(object sender, RoutedEventArgs e)
    {
        string text = PreviewTabs.SelectedItem == TempTableTab
            ? TempTableEditor.Text
            : PreviewEditor.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { Clipboard.SetText(text); } catch { }
        }
    }

    private void FindReferences_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultViewModel vm)
        {
            SearchTextBox.Text = vm.DisplayName;
            SearchObjectNamesCheck.IsChecked = false;
            SearchColumnNamesCheck.IsChecked = false;
            SearchDefinitionsCheck.IsChecked = true;
            ExecuteSearch();
        }
    }

    private void OpenJobProperties_Click(object sender, RoutedEventArgs e)
    {
        OpenJobPropertiesForSelectedResult();
    }

    /// <summary>
    /// Opens SSMS's own Job Properties dialog for the selected job step — the job equivalent of "Open in
    /// Schema Viewer", and what a double-click on a job step result does.
    ///
    /// Everything about the launch is <see cref="JobDialogLauncher"/>'s; the only thing this adds is the
    /// server name, which has to be the SMO name rather than the connection string's Data Source and is
    /// therefore taken from the search probe rather than guessed here.
    /// </summary>
    private void OpenJobPropertiesForSelectedResult()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (ResultsList.SelectedItem is not SearchResultViewModel vm || !vm.IsJobStep)
        {
            StatusText.Text = "Select an Agent job step result first";
            return;
        }

        if (string.IsNullOrEmpty(_jobServerName))
        {
            StatusText.Text = "The server name is not known yet — run the search again.";
            return;
        }

        var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
        IntPtr owner = IntPtr.Zero;
        uiShell?.GetDialogOwnerHwnd(out owner);

        try
        {
            uiShell?.EnableModeless(0);
            StatusText.Text = $"Opening Job Properties for {vm.JobStep.JobName}…";
            JobDialogLauncher.ShowJobProperties(ServiceProvider.GlobalProvider, _jobServerName, vm.JobStep.JobId, vm.JobStep.JobName,
                                                JobStepSearchService.BuildConnectionString(vm.ConnectionString), owner);
            StatusText.Text = $"Closed Job Properties for {vm.JobStep.JobName}.";
        }
        catch (Exception ex)
        {
            // Undocumented SSMS internals: a servicing update that moves one of the types costs this one
            // action. Object Explorer and the Agent jobs dashboard are both still there as the way in.
            StatusText.Text = "Could not open the Job Properties dialog: " + JobDialogLauncher.Innermost(ex).Message;
        }
        finally
        {
            uiShell?.EnableModeless(1);
        }
    }

    private void ViewSchemaDialogForSelectedResult()
    {
        if (ResultsList.SelectedItem is not SearchResultViewModel vm)
            return;

        // A job step has nothing to script — the job equivalent of the schema dialog is SSMS's own properties
        // sheet, so a double-click on one goes there rather than reporting an object that cannot be found.
        if (vm.IsJobStep)
        {
            OpenJobPropertiesForSelectedResult();
            return;
        }

        try
        {
            string objectName = vm.MatchLocation == "ColumnName"
                ? $"{vm.SchemaName}.{vm.ObjectName}"
                : $"{vm.SchemaName}.{vm.DisplayName}";

            string targetConnStr = ConnectionHelper.GetConnectionStringForDatabase(vm.ConnectionString, vm.DatabaseName);
            string connectionKey = vm.ConnectionKey;
            string databaseName = vm.DatabaseName;

            // Off the UI thread, like the preview path above: for a module defined WITH ENCRYPTION the
            // script is built by opening an administrator connection and briefly ALTERing the object, which
            // inline would freeze SSMS until it finished.
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                string schemaScript = null;
                Exception error = null;

                await Task.Run(() =>
                {
                    try { schemaScript = SchemaQueryService.GetSchemaScript(targetConnStr, objectName, connectionKey); }
                    catch (Exception ex) { error = ex; }
                });

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (error != null)
                {
                    StatusText.Text = $"Error: {error.Message}";
                }
                else if (!string.IsNullOrEmpty(schemaScript))
                {
                    new SchemaDialog(objectName, schemaScript, targetConnStr).ShowDialog();
                }
                else
                {
                    string dbInfo = !string.IsNullOrEmpty(databaseName) ? $" in [{databaseName}]" : "";
                    StatusText.Text = $"Object '{objectName}' not found{dbInfo}";
                }
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }
}
