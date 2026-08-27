using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;

namespace SQLExtended;

/// <summary>
/// Provides hover tooltips for SQL object names in the SSMS query editor.
/// Shows object type, row count, and a clickable link to open the full schema dialog.
/// Only triggers when the identifier appears in a SQL context (after FROM, JOIN, etc.).
/// </summary>
internal sealed class SchemaQuickInfoSource : IAsyncQuickInfoSource
{
    private readonly ITextBuffer _textBuffer;
    private readonly ITextStructureNavigatorSelectorService _navigatorService;
    private bool _disposed;

    // SQL identifier pattern: supports multi-part names with brackets
    private static readonly Regex SqlIdentifierRegex = new Regex(
        @"[\w@#\.\[\]]+",
        RegexOptions.Compiled);

    // Context patterns: keywords after which an identifier is likely a table/view/proc reference
    private static readonly Regex ContextPattern = new Regex(
        @"(?i)\b(?:FROM|(?:INNER|LEFT|RIGHT|CROSS|FULL(?:\s+OUTER)?)\s+JOIN|JOIN|INTO|UPDATE|DELETE(?:\s+FROM)?|TRUNCATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE|INSERT\s+INTO|EXEC(?:UTE)?|TABLE)\s+$",
        RegexOptions.Compiled);

    public SchemaQuickInfoSource(
        ITextBuffer textBuffer,
        ITextStructureNavigatorSelectorService navigatorService)
    {
        _textBuffer = textBuffer;
        _navigatorService = navigatorService;
    }

    public async Task<QuickInfoItem> GetQuickInfoItemAsync(
        IAsyncQuickInfoSession session,
        CancellationToken cancellationToken)
    {
        if (_disposed) return null;

        try
        {
            Log("GetQuickInfoItemAsync entered");

            var snapshot = _textBuffer.CurrentSnapshot;
            var triggerPoint = session.GetTriggerPoint(snapshot);
            if (!triggerPoint.HasValue) return null;

            var position = triggerPoint.Value.Position;

            // Get the full line text and find the identifier at the trigger position
            var line = snapshot.GetLineFromPosition(position);
            string lineText = line.GetText();
            int columnInLine = position - line.Start.Position;

            // Find the SQL identifier span at the cursor
            string identifier = null;
            int identStart = 0;
            int identLength = 0;

            foreach (Match match in SqlIdentifierRegex.Matches(lineText))
            {
                if (columnInLine >= match.Index && columnInLine < match.Index + match.Length)
                {
                    identifier = match.Value;
                    identStart = match.Index;
                    identLength = match.Length;
                    break;
                }
            }

            if (string.IsNullOrEmpty(identifier))
            {
                Log("No identifier found at position");
                return null;
            }

            // Clean the identifier (strip brackets, trailing punctuation)
            string cleaned = identifier.Replace("[", "").Replace("]", "")
                .TrimEnd('.', ';', ',', '(', ')', ' ').TrimStart(' ');
            if (string.IsNullOrEmpty(cleaned)) return null;

            // Skip obvious SQL keywords
            if (IsSqlKeyword(cleaned))
            {
                Log($"Skipping keyword: {cleaned}");
                return null;
            }

            // Context detection: check if this identifier follows a table-reference keyword
            bool hasDot = identifier.Contains(".");
            if (!hasDot)
            {
                // For single-part names, require SQL context
                string textBefore = lineText.Substring(0, identStart);

                // Also look at previous lines (up to ~500 chars back) for multi-line FROM/JOIN
                if (identStart == 0 || string.IsNullOrWhiteSpace(textBefore))
                {
                    int charsToLookBack = Math.Min(500, position - line.Start.Position + line.Start.Position);
                    int lookBackStart = Math.Max(0, position - 500);
                    textBefore = snapshot.GetText(lookBackStart, position - lookBackStart);
                }

                if (!IsInSqlObjectContext(textBefore))
                {
                    Log($"No SQL context for: {cleaned}");
                    return null;
                }
            }
            // Multi-part names (schema.table) are very likely object references — skip context check

            Log($"Identifier: {cleaned}, hasDot: {hasDot}");

            // Parse the name
            var (database, schema, name) = EditorHelper.ParseObjectName(cleaned);
            if (string.IsNullOrEmpty(name)) return null;

            // Get connection string (must happen on background thread safe — GetActiveConnectionString needs UI thread)
            string connectionString = null;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            connectionString = ConnectionHelper.GetActiveConnectionString();

            if (string.IsNullOrEmpty(connectionString))
            {
                Log("No active connection string");
                return null;
            }

            // Ensure the schema cache is loading for this database
            try
            {
                string currentDb = database ?? ConnectionHelper.GetCurrentDatabaseName();
                if (!string.IsNullOrEmpty(currentDb))
                {
                    var cache = SchemaCache.Instance;
                    string connKey = cache.GetConnectionKey(connectionString);
                    var state = cache.GetState(connKey, currentDb);
                    if (state == CacheState.NotLoaded)
                    {
                        _ = cache.LoadDatabaseAsync(connectionString, currentDb);
                    }
                }
            }
            catch { }

            Log($"Querying: db={database}, schema={schema}, name={name}");
            cancellationToken.ThrowIfCancellationRequested();

            // Lightweight existence check (runs on background thread)
            var quickInfo = await Task.Run(
                () => SchemaQueryService.GetQuickInfo(connectionString, database, schema, name),
                cancellationToken);

            if (quickInfo == null)
            {
                Log($"Object not found: {cleaned}");
                return null;
            }

            Log($"Found: {quickInfo.QualifiedName} ({quickInfo.ObjectTypeDisplay}, {quickInfo.RowCount} rows)");

            // Build the applicability span
            var applicabilitySpan = snapshot.CreateTrackingSpan(
                line.Start.Position + identStart, identLength, SpanTrackingMode.EdgeInclusive);

            // Build the WPF tooltip element (must be created, but can be built here)
            var tooltip = BuildTooltipElement(quickInfo, cleaned, session);

            return new QuickInfoItem(applicabilitySpan, tooltip);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            // Never crash SSMS
            return null;
        }
    }

    /// <summary>
    /// Checks whether the text preceding the identifier indicates a SQL object reference context.
    /// </summary>
    private static bool IsInSqlObjectContext(string textBefore)
    {
        if (string.IsNullOrWhiteSpace(textBefore)) return false;

        // Trim trailing whitespace to match the regex
        string trimmed = textBefore.TrimEnd();

        // Check if the text ends with one of our context keywords
        return ContextPattern.IsMatch(textBefore);
    }

    /// <summary>
    /// Returns true if the word is a common SQL keyword (not a table/view name).
    /// </summary>
    private static bool IsSqlKeyword(string word)
    {
        switch (word.ToUpperInvariant())
        {
            case "SELECT": case "FROM": case "WHERE": case "AND": case "OR":
            case "INSERT": case "UPDATE": case "DELETE": case "INTO": case "VALUES":
            case "SET": case "JOIN": case "INNER": case "LEFT": case "RIGHT":
            case "OUTER": case "CROSS": case "FULL": case "ON": case "AS":
            case "ORDER": case "BY": case "GROUP": case "HAVING": case "DISTINCT":
            case "TOP": case "WITH": case "UNION": case "ALL": case "EXISTS":
            case "IN": case "NOT": case "NULL": case "IS": case "LIKE": case "BETWEEN":
            case "CASE": case "WHEN": case "THEN": case "ELSE": case "END":
            case "BEGIN": case "COMMIT": case "ROLLBACK": case "TRANSACTION":
            case "CREATE": case "ALTER": case "DROP": case "TABLE": case "VIEW":
            case "INDEX": case "PROCEDURE": case "FUNCTION": case "TRIGGER":
            case "EXEC": case "EXECUTE": case "DECLARE":
            case "IF": case "WHILE": case "RETURN": case "PRINT": case "GO":
            case "USE": case "DATABASE": case "SCHEMA": case "GRANT": case "REVOKE":
            case "TRUNCATE": case "PRIMARY": case "KEY": case "FOREIGN": case "REFERENCES":
            case "CONSTRAINT": case "DEFAULT": case "CHECK": case "UNIQUE":
            case "CLUSTERED": case "NONCLUSTERED": case "ASC": case "DESC":
            case "COUNT": case "SUM": case "AVG": case "MIN": case "MAX":
            case "CAST": case "CONVERT": case "COALESCE": case "ISNULL":
            case "GETDATE": case "NEWID": case "SCOPE_IDENTITY":
            case "VARCHAR": case "NVARCHAR": case "INT": case "BIGINT":
            case "BIT": case "DATETIME": case "FLOAT": case "DECIMAL":
            case "CHAR": case "NCHAR": case "TEXT": case "NTEXT": case "IMAGE":
            case "MONEY": case "SMALLMONEY": case "SMALLINT": case "TINYINT":
            case "REAL": case "NUMERIC": case "BINARY": case "VARBINARY":
            case "IDENTITY": case "OUTPUT": case "OVER": case "PARTITION":
            case "ROW_NUMBER": case "RANK": case "DENSE_RANK": case "NTILE":
            case "LAG": case "LEAD": case "FIRST_VALUE": case "LAST_VALUE":
            case "APPLY": case "PIVOT": case "UNPIVOT": case "OFFSET": case "FETCH":
            case "NEXT": case "ROWS": case "ONLY": case "PERCENT": case "TIES":
            case "EXCEPT": case "INTERSECT": case "MERGE": case "USING":
            case "MATCHED": case "TARGET": case "SOURCE":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Builds the WPF tooltip element styled to match SSMS dark theme.
    /// </summary>
    private UIElement BuildTooltipElement(
        SchemaQueryService.QuickInfoResult info,
        string rawObjectName,
        IAsyncQuickInfoSession session)
    {
        var panel = new StackPanel
        {
            MaxWidth = 350,
            Margin = new Thickness(0)
        };

        var border = new Border
        {
            Background = new SolidColorBrush(ColorFromHex("#252526")),
            BorderBrush = new SolidColorBrush(ColorFromHex("#007ACC")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 8, 10, 8),
            Child = panel
        };

        // Object name line
        var nameText = new TextBlock
        {
            Text = $"\U0001F4CB {info.QualifiedName}",
            Foreground = new SolidColorBrush(ColorFromHex("#569CD6")),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(nameText);

        // Type and row count line
        string details = info.ObjectTypeDisplay;
        if (info.ObjectType == "U" || info.ObjectType == "V")
            details += $" \u00B7 {info.RowCount:N0} rows";
        if (!string.IsNullOrEmpty(info.Database))
            details += $" \u00B7 {info.Database}";

        var detailsText = new TextBlock
        {
            Text = details,
            Foreground = new SolidColorBrush(ColorFromHex("#CCCCCC")),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };
        panel.Children.Add(detailsText);

        // Clickable link to open full schema
        var linkText = new TextBlock
        {
            FontSize = 12,
            Cursor = Cursors.Hand
        };
        var hyperlink = new Run("Click for full schema...")
        {
            Foreground = new SolidColorBrush(ColorFromHex("#007ACC")),
        };
        hyperlink.TextDecorations = TextDecorations.Underline;
        linkText.Inlines.Add(hyperlink);

        linkText.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            OnTooltipClicked(info, rawObjectName, session);
        };
        // Also make the name clickable
        nameText.Cursor = Cursors.Hand;
        nameText.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            OnTooltipClicked(info, rawObjectName, session);
        };

        panel.Children.Add(linkText);

        return border;
    }

    /// <summary>
    /// Handles click on the tooltip: dismisses QuickInfo and opens the SchemaDialog.
    /// </summary>
    private void OnTooltipClicked(
        SchemaQueryService.QuickInfoResult info,
        string rawObjectName,
        IAsyncQuickInfoSession session)
    {
        try
        {
            // Dismiss the tooltip
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await session.DismissAsync();
            });
        }
        catch { }

        // Build the script on a background thread, then open the dialog on the UI thread. RunAsync, not
        // Run: building the script can open a second connection — for a module defined WITH ENCRYPTION it
        // opens a dedicated administrator connection and briefly ALTERs the object — and blocking the UI
        // thread on that freezes SSMS with nothing on screen to explain it.
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            string schemaScript = null;

            try
            {
                schemaScript = await Task.Run(() => SchemaQueryService.GetSchemaScript(info.ConnectionString, rawObjectName));
            }
            catch { }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (!string.IsNullOrEmpty(schemaScript))
                    new SchemaDialog(rawObjectName, schemaScript, info.ConnectionString).ShowDialog();
            }
            catch { }
        });
    }

    private static Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Color.FromRgb(r, g, b);
    }

    private static void Log(string message) => SchemaQuickInfoSourceProvider.DebugLog(message);

    public void Dispose()
    {
        _disposed = true;
    }
}
