using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.IO;

namespace SQLExtended;

/// <summary>
/// MEF-exported provider that creates SchemaQuickInfoSource instances for SQL editor buffers.
/// The [ContentType] must match the SSMS SQL editor content type.
/// Known candidates: "SQL Server Tools", "T-SQL", "sql", "text".
/// </summary>
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("SQLExtended Schema QuickInfo")]
[ContentType("SQL")]
[Order(After = "default")]
internal sealed class SchemaQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    [Import]
    internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        DebugLog($"TryCreateQuickInfoSource called — ContentType: {textBuffer.ContentType.TypeName}");
        return textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new SchemaQuickInfoSource(textBuffer, NavigatorService));
    }

    // [Conditional("DEBUG")] removes every call site (and its string-interpolation argument)
    // from Release builds — better than an #if DEBUG body, which still builds the interpolated
    // string on every hover before discarding it.
    [System.Diagnostics.Conditional("DEBUG")]
    internal static void DebugLog(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "sqlextended-ssms-debug.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
