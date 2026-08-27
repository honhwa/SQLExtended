using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using SQLExtended.Formatting;
using System;
using System.Windows;
using System.Windows.Input;
using DTE = EnvDTE.DTE;
using DTE2 = EnvDTE80.DTE2;
using TextDocument = EnvDTE.TextDocument;

namespace SQLExtended;

public partial class FormatterOptionsDialog : Window
{
    private const string DefaultSampleSql =
        "select c.CustomerID,c.FirstName,c.LastName,o.OrderID,o.OrderDate,\r\n" +
        "case when o.Status='Active' then 'Open' when o.Status='Hold' then 'On hold'\r\n" +
        "else 'Closed' end as 'Order state',\r\n" +
        "p.ProductName,od.Quantity,od.UnitPrice from dbo.Customers c inner join\r\n" +
        "dbo.Orders o on c.CustomerID=o.CustomerID inner join dbo.OrderDetails od\r\n" +
        "on o.OrderID=od.OrderID inner join dbo.Products p on od.ProductID=p.ProductID\r\n" +
        "where o.OrderDate>='2024-01-01' and c.Region='APAC' and o.Status='Active'\r\n" +
        "order by o.OrderDate desc,c.LastName";

    private FormatterOptions _options;
    private bool _isLoading;
    private readonly string _currentDocumentSql;
    private readonly FormatterProfileManager _profileManager;
    private string _selectedProfileName;

    public FormatterOptions ResultOptions { get; private set; }

    /// <summary>
    /// Creates the formatter options dialog.
    /// </summary>
    /// <param name="options">Current formatter options.</param>
    /// <param name="currentDocumentSql">
    /// SQL text from the active editor window. Pass null if no document is open.
    /// When provided, the user can toggle the preview to show this text formatted live.
    /// </param>
    public FormatterOptionsDialog(FormatterOptions options, string currentDocumentSql = null)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        InitializeComponent();
        _profileManager = FormatterProfileManager.Instance;
        _options = options.Clone();
        // If the caller didn't supply the active document, try to fetch it ourselves
        // so the "Current Document" preview toggle works from every entry point
        // (FormatCommand passes it; SQLExtendedSettingsDialog does not).
        _currentDocumentSql = currentDocumentSql ?? TryGetActiveDocumentSql();

        LoadProfileList();
        LoadOptionsToUI();

        // Disable "Current Document" toggle if there's no editor text
        if (string.IsNullOrWhiteSpace(_currentDocumentSql))
        {
            RbCurrentDocument.IsEnabled = false;
            RbCurrentDocument.ToolTip = "No active document open";
        }
        else
        {
            RbCurrentDocument.ToolTip = $"Preview using the active editor ({LineCount(_currentDocumentSql)} lines)";
        }

        // Start with sample SQL
        PreviewInput.Text = DefaultSampleSql;
        UpdatePreview();
    }

    /// <summary>
    /// Best-effort fetch of the active editor's text via DTE. Returns null if no
    /// document is open, the active document isn't text, or anything fails.
    /// Must be called on the UI thread.
    /// </summary>
    private static string TryGetActiveDocumentSql()
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            var doc = dte?.ActiveDocument;
            if (doc == null) return null;

            var textDoc = doc.Object("TextDocument") as TextDocument;
            if (textDoc == null) return null;

            var start = textDoc.StartPoint.CreateEditPoint();
            return start.GetText(textDoc.EndPoint);
        }
        catch
        {
            return null;
        }
    }

    private static int LineCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
        }
        return count;
    }

    private void LoadOptionsToUI()
    {
        _isLoading = true;

        try
        {
            // General tab
            CboKeywordCase.Items.Clear();
            CboKeywordCase.Items.Add("UPPER");
            CboKeywordCase.Items.Add("lower");
            CboKeywordCase.Items.Add("Unchanged");
            CboKeywordCase.SelectedIndex = (int)_options.KeywordCase;

            CboBuiltInFunctionCase.Items.Clear();
            CboBuiltInFunctionCase.Items.Add("UPPER (SUM, GETDATE)");
            CboBuiltInFunctionCase.Items.Add("lower (sum, getdate)");
            CboBuiltInFunctionCase.Items.Add("Unchanged");
            CboBuiltInFunctionCase.SelectedIndex = (int)_options.BuiltInFunctionCase;

            CboIdentifierCase.Items.Clear();
            CboIdentifierCase.Items.Add("UPPER");
            CboIdentifierCase.Items.Add("lower");
            CboIdentifierCase.Items.Add("Unchanged");
            CboIdentifierCase.SelectedIndex = _options.IdentifierCase == CasingOption.Upper ? 0
                : _options.IdentifierCase == CasingOption.Lower ? 1 : 2;

            CboIndentStyle.Items.Clear();
            CboIndentStyle.Items.Add("Tabs");
            CboIndentStyle.Items.Add("Spaces");
            CboIndentStyle.SelectedIndex = (int)_options.IndentStyle;

            TxtIndentSize.Text = _options.IndentSize.ToString();
            ChkIndentConditions.IsChecked = _options.IndentBetweenConditions;
            TxtMaxLineWidth.Text = _options.MaxLineWidth.ToString();

            CboSemicolon.Items.Clear();
            CboSemicolon.Items.Add("Always");
            CboSemicolon.Items.Add("Never");
            CboSemicolon.Items.Add("Unchanged");
            CboSemicolon.SelectedIndex = (int)_options.TrailingSemicolon;

            // Layout tab
            CboSelectLayout.Items.Clear();
            CboSelectLayout.Items.Add("Same Line");
            CboSelectLayout.Items.Add("Stacked Indented");
            CboSelectLayout.Items.Add("Stacked Aligned");
            CboSelectLayout.Items.Add("Stacked (SELECT on own line)");
            CboSelectLayout.SelectedIndex = (int)_options.SelectColumnLayout;

            CboCommaPosition.Items.Clear();
            CboCommaPosition.Items.Add("Trailing (col1, col2,)");
            CboCommaPosition.Items.Add("Leading (, col1 , col2)");
            CboCommaPosition.SelectedIndex = (int)_options.CommaPosition;
            ChkLeadingCommaKeepIndent.IsChecked = _options.LeadingCommaKeepIndent;

            CboJoinLayout.Items.Clear();
            CboJoinLayout.Items.Add("New Line");
            CboJoinLayout.Items.Add("Same Line");
            CboJoinLayout.SelectedIndex = (int)_options.JoinLayout;

            ChkJoinOnSameLine.IsChecked = _options.JoinOnSameLine;
            ChkAlignFromAndJoins.IsChecked = _options.AlignFromAndJoins;
            ChkNormalizeJoins.IsChecked = _options.NormalizeJoinKeywords;
            ChkCteStacked.IsChecked = _options.CteStackedLayout;
            ChkDerivedTableStacked.IsChecked = _options.DerivedTableStackedLayout;

            CboWhereLayout.Items.Clear();
            CboWhereLayout.Items.Add("New Line Per Condition");
            CboWhereLayout.Items.Add("Inline");
            CboWhereLayout.SelectedIndex = (int)_options.WhereConditionLayout;

            ChkMultilineSet.IsChecked = _options.MultilineSetClauseItems;
            ChkAlignSet.IsChecked = _options.AlignSetClauseItem;
            ChkAlignSetWithUpdate.IsChecked = _options.AlignSetWithUpdate;
            ChkNewLineOpenParen.IsChecked = _options.NewLineBeforeOpenParenthesis;
            ChkNewLineCloseParen.IsChecked = _options.NewLineBeforeCloseParenthesis;
            ChkNewLineOffset.IsChecked = _options.NewLineBeforeOffsetClause;
            ChkNewLineWindow.IsChecked = _options.NewLineBeforeWindowClause;
            ChkAsKeywordOwnLine.IsChecked = _options.AsKeywordOnOwnLine;

            ChkBlankLineBeforeStatement.IsChecked = _options.BlankLineBeforeStatement;
            TxtBlankLinesBetween.Text = _options.BlankLinesBetweenStatements.ToString();
            TxtBlankLinesAfterGo.Text = _options.BlankLineAfterGO.ToString();

            // Style tab
            CboAliasStyle.Items.Clear();
            CboAliasStyle.Items.Add("AS (FROM Customers AS c)");
            CboAliasStyle.Items.Add("No AS (FROM Customers c)");
            CboAliasStyle.Items.Add("Unchanged");
            CboAliasStyle.Items.Add("Column = value (SELECT Title = '')");
            CboAliasStyle.SelectedIndex = (int)_options.AliasStyle;

            CboCaseWhenLayout.Items.Clear();
            CboCaseWhenLayout.Items.Add("Unchanged");
            CboCaseWhenLayout.Items.Add("Stacked (CASE / WHEN … / END)");
            CboCaseWhenLayout.Items.Add("First WHEN on the CASE line");
            CboCaseWhenLayout.SelectedIndex = (int)_options.CaseWhenLayout;

            CboBracketQuoting.Items.Clear();
            CboBracketQuoting.Items.Add("Unchanged");
            CboBracketQuoting.Items.Add("Add Brackets");
            CboBracketQuoting.Items.Add("Remove Brackets");
            CboBracketQuoting.SelectedIndex = (int)_options.BracketQuoting;

            TxtInsertColumnsPerLine.Text = _options.InsertColumnsPerLine.ToString();
            TxtInsertValuesPerLine.Text = _options.InsertValuesPerLine.ToString();
            ChkInsertOpenParenSameLine.IsChecked = _options.InsertOpenParenthesisOnSameLine;
            ChkInsertParensSameLine.IsChecked = _options.InsertParenthesesOnSameLine;

            CboInsertTemplateStyle.Items.Clear();
            CboInsertTemplateStyle.Items.Add("VALUES form");
            CboInsertTemplateStyle.Items.Add("SELECT col = val form");
            CboInsertTemplateStyle.SelectedIndex = (int)_options.InsertTemplateDefaultStyle;

            ChkProcParamsSameLine.IsChecked = _options.ProcedureParametersOnSameLine;
            ChkSpaceBeforeTypeParams.IsChecked = _options.SpaceBetweenDataTypeAndParameters;
            ChkSpaceBetweenTypeParams.IsChecked = _options.SpaceBetweenParametersInDataType;
            ChkAlignColumnDefs.IsChecked = _options.AlignColumnDefinitionFields;
            ChkNewLineCheckConstraint.IsChecked = _options.NewlineFormattedCheckConstraint;
            ChkNewLineIndexDef.IsChecked = _options.NewLineFormattedIndexDefinition;
            ChkMultilineViewCols.IsChecked = _options.MultilineViewColumnsList;
            ChkIndentViewBody.IsChecked = _options.IndentViewBody;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ReadOptionsFromUI()
    {
        // General
        _options.KeywordCase = (CasingOption)CboKeywordCase.SelectedIndex;
        _options.BuiltInFunctionCase = (CasingOption)CboBuiltInFunctionCase.SelectedIndex;

        _options.IdentifierCase = CboIdentifierCase.SelectedIndex switch
        {
            0 => CasingOption.Upper,
            1 => CasingOption.Lower,
            _ => CasingOption.Unchanged
        };

        _options.IndentStyle = (IndentStyleOption)CboIndentStyle.SelectedIndex;
        _options.IndentSize = ParseInt(TxtIndentSize.Text, 4);
        _options.IndentBetweenConditions = ChkIndentConditions.IsChecked == true;
        _options.MaxLineWidth = ParseInt(TxtMaxLineWidth.Text, 120);
        _options.TrailingSemicolon = (SemicolonOption)CboSemicolon.SelectedIndex;

        // Layout
        _options.SelectColumnLayout = (SelectColumnLayoutOption)CboSelectLayout.SelectedIndex;
        _options.CommaPosition = (CommaPositionOption)CboCommaPosition.SelectedIndex;
        _options.LeadingCommaKeepIndent = ChkLeadingCommaKeepIndent.IsChecked == true;
        _options.JoinLayout = (JoinLayoutOption)CboJoinLayout.SelectedIndex;
        _options.JoinOnSameLine = ChkJoinOnSameLine.IsChecked == true;
        _options.AlignFromAndJoins = ChkAlignFromAndJoins.IsChecked == true;
        _options.NormalizeJoinKeywords = ChkNormalizeJoins.IsChecked == true;
        _options.CteStackedLayout = ChkCteStacked.IsChecked == true;
        _options.DerivedTableStackedLayout = ChkDerivedTableStacked.IsChecked == true;
        _options.WhereConditionLayout = (WhereConditionLayoutOption)CboWhereLayout.SelectedIndex;
        _options.MultilineSetClauseItems = ChkMultilineSet.IsChecked == true;
        _options.AlignSetClauseItem = ChkAlignSet.IsChecked == true;
        _options.AlignSetWithUpdate = ChkAlignSetWithUpdate.IsChecked == true;
        _options.NewLineBeforeOpenParenthesis = ChkNewLineOpenParen.IsChecked == true;
        _options.NewLineBeforeCloseParenthesis = ChkNewLineCloseParen.IsChecked == true;
        _options.NewLineBeforeOffsetClause = ChkNewLineOffset.IsChecked == true;
        _options.NewLineBeforeWindowClause = ChkNewLineWindow.IsChecked == true;
        _options.AsKeywordOnOwnLine = ChkAsKeywordOwnLine.IsChecked == true;
        _options.BlankLineBeforeStatement = ChkBlankLineBeforeStatement.IsChecked == true;
        _options.BlankLinesBetweenStatements = ParseInt(TxtBlankLinesBetween.Text, 1);
        _options.BlankLineAfterGO = ParseInt(TxtBlankLinesAfterGo.Text, 1);

        // Style
        _options.AliasStyle = (AliasStyleOption)CboAliasStyle.SelectedIndex;
        _options.CaseWhenLayout = (CaseWhenLayoutOption)CboCaseWhenLayout.SelectedIndex;
        _options.BracketQuoting = (BracketQuotingOption)CboBracketQuoting.SelectedIndex;
        _options.InsertColumnsPerLine = ParseInt(TxtInsertColumnsPerLine.Text, 4);
        _options.InsertValuesPerLine = ParseInt(TxtInsertValuesPerLine.Text, 4);
        _options.InsertOpenParenthesisOnSameLine = ChkInsertOpenParenSameLine.IsChecked == true;
        _options.InsertParenthesesOnSameLine = ChkInsertParensSameLine.IsChecked == true;
        _options.InsertTemplateDefaultStyle = CboInsertTemplateStyle.SelectedIndex == 1
            ? InsertTemplateStyleOption.SelectAssign
            : InsertTemplateStyleOption.Values;
        _options.ProcedureParametersOnSameLine = ChkProcParamsSameLine.IsChecked == true;
        _options.SpaceBetweenDataTypeAndParameters = ChkSpaceBeforeTypeParams.IsChecked == true;
        _options.SpaceBetweenParametersInDataType = ChkSpaceBetweenTypeParams.IsChecked == true;
        _options.AlignColumnDefinitionFields = ChkAlignColumnDefs.IsChecked == true;
        _options.NewlineFormattedCheckConstraint = ChkNewLineCheckConstraint.IsChecked == true;
        _options.NewLineFormattedIndexDefinition = ChkNewLineIndexDef.IsChecked == true;
        _options.MultilineViewColumnsList = ChkMultilineViewCols.IsChecked == true;
        _options.IndentViewBody = ChkIndentViewBody.IsChecked == true;
    }

    private void UpdatePreview()
    {
        if (_isLoading || PreviewInput == null || PreviewOutput == null)
            return;

        try
        {
            ReadOptionsFromUI();
            var formatter = new SqlFormatterService(_options);
            var result = formatter.Format(PreviewInput.Text);

            if (result.Success)
            {
                PreviewOutput.Text = result.FormattedSql;
                TxtPreviewStatus.Text = "";
                TxtPreviewStatus.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                PreviewOutput.Text = PreviewInput.Text;
                TxtPreviewStatus.Text = "Parse error";
                TxtPreviewStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF1, 0x4C, 0x4C));
                TxtPreviewStatus.ToolTip = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            PreviewOutput.Text = $"-- Error: {ex.Message}";
            TxtPreviewStatus.Text = "Error";
            TxtPreviewStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF1, 0x4C, 0x4C));
        }
    }

    // Event handlers

    private void Option_Changed(object sender, EventArgs e)
    {
        UpdatePreview();
    }

    private void PreviewInput_TextChanged(object sender, EventArgs e)
    {
        UpdatePreview();
    }

    private void PreviewSource_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || PreviewInput == null)
            return;

        _isLoading = true;
        try
        {
            if (RbCurrentDocument.IsChecked == true && !string.IsNullOrWhiteSpace(_currentDocumentSql))
            {
                PreviewInput.Text = _currentDocumentSql;
                PreviewInput.IsReadOnly = true;
                PreviewInput.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E));
            }
            else
            {
                PreviewInput.Text = DefaultSampleSql;
                PreviewInput.IsReadOnly = false;
                PreviewInput.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x26));
            }
        }
        finally
        {
            _isLoading = false;
        }

        UpdatePreview();
    }

    // Profile management

    private void LoadProfileList()
    {
        _isLoading = true;
        try
        {
            _selectedProfileName = _profileManager.ActiveProfileName;
            CboProfile.Items.Clear();
            foreach (var name in _profileManager.GetProfileNames())
                CboProfile.Items.Add(name);

            CboProfile.SelectedItem = _selectedProfileName;
            UpdateProfileButtons();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateProfileButtons()
    {
        bool isDefault = string.Equals(_selectedProfileName, FormatterProfileManager.DefaultProfileName,
            StringComparison.OrdinalIgnoreCase);
        BtnDeleteProfile.IsEnabled = !isDefault;
        BtnRenameProfile.IsEnabled = !isDefault;
    }

    private void Profile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoading || CboProfile.SelectedItem == null)
            return;

        string newProfile = CboProfile.SelectedItem.ToString();
        if (string.Equals(newProfile, _selectedProfileName, StringComparison.OrdinalIgnoreCase))
            return;

        _selectedProfileName = newProfile;
        if (_profileManager.Profiles.TryGetValue(newProfile, out var profileOptions))
        {
            _options = profileOptions.Clone();
            LoadOptionsToUI();
            UpdatePreview();
        }

        UpdateProfileButtons();
    }

    private void SaveProfileAs_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForName("Save Profile As", "Enter a name for the new profile:", "");
        if (name == null)
            return;

        if (_profileManager.Profiles.ContainsKey(name))
        {
            var result = MessageBox.Show(
                $"A profile named \"{name}\" already exists. Overwrite it?",
                "Profile Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;
        }

        ReadOptionsFromUI();
        _profileManager.SaveProfile(name, _options);
        _selectedProfileName = name;
        LoadProfileList();
        TxtProfileStatus.Text = $"Saved \"{name}\"";
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_selectedProfileName, FormatterProfileManager.DefaultProfileName,
            StringComparison.OrdinalIgnoreCase))
            return;

        var newName = PromptForName("Rename Profile", "Enter a new name:", _selectedProfileName);
        if (newName == null || string.Equals(newName, _selectedProfileName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!_profileManager.RenameProfile(_selectedProfileName, newName))
        {
            MessageBox.Show("A profile with that name already exists.", "Rename Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedProfileName = newName;
        LoadProfileList();
        TxtProfileStatus.Text = $"Renamed to \"{newName}\"";
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_selectedProfileName, FormatterProfileManager.DefaultProfileName,
            StringComparison.OrdinalIgnoreCase))
            return;

        var result = MessageBox.Show(
            $"Delete profile \"{_selectedProfileName}\"?",
            "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        _profileManager.DeleteProfile(_selectedProfileName);
        _selectedProfileName = FormatterProfileManager.DefaultProfileName;
        _options = _profileManager.GetActiveOptions();
        LoadProfileList();
        LoadOptionsToUI();
        UpdatePreview();
        TxtProfileStatus.Text = "Profile deleted";
    }

    private static string PromptForName(string title, string prompt, string defaultValue)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

        var label = new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Foreground = System.Windows.Media.Brushes.LightGray,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x37)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12
        };
        textBox.SelectAll();

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        string resultName = null;

        var okBtn = new System.Windows.Controls.Button
        {
            Content = "OK",
            Width = 80,
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okBtn.Click += (s, ev) =>
        {
            resultName = textBox.Text?.Trim();
            dlg.DialogResult = true;
        };

        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 80,
            Padding = new Thickness(0, 4, 0, 4),
            IsCancel = true
        };

        buttonPanel.Children.Add(okBtn);
        buttonPanel.Children.Add(cancelBtn);

        stack.Children.Add(label);
        stack.Children.Add(textBox);
        stack.Children.Add(buttonPanel);
        dlg.Content = stack;

        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(resultName))
            return resultName;

        return null;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ReadOptionsFromUI();
        ResultOptions = _options;

        // Save to the selected profile and make it active
        _profileManager.SaveProfile(_selectedProfileName, _options);
        _profileManager.SetActiveProfile(_selectedProfileName);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        _options = FormatterOptions.Defaults;
        LoadOptionsToUI();
        UpdatePreview();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Import Formatter Settings"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                _options = FormatterOptions.ImportFrom(dlg.FileName);
                LoadOptionsToUI();
                UpdatePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import settings: {ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        ReadOptionsFromUI();

        var dlg = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            Title = "Export Formatter Settings",
            FileName = "sqlextended-formatter-options.json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                _options.ExportTo(dlg.FileName);
                MessageBox.Show("Settings exported successfully.",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export settings: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private static int ParseInt(string text, int fallback)
    {
        return int.TryParse(text, out int value) && value >= 0 ? value : fallback;
    }
}
