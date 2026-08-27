#if DEBUG
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace SQLExtended;

/// <summary>
/// DEBUG ONLY: Logs the content type of every text buffer SSMS opens.
/// This helps discover the correct [ContentType] for the SQL editor.
/// Check %TEMP%\sqlextended-debug.log after hovering in a query window.
/// Remove this file before release.
/// </summary>
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("SQLExtended ContentType Sniffer")]
[ContentType("text")]
[Order(After = "default")]
internal sealed class ContentTypeSnifferProvider : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        var ct = textBuffer.ContentType;
        var baseTypes = string.Join(", ", System.Linq.Enumerable.Select(ct.BaseTypes, b => b.TypeName));
        SchemaQuickInfoSourceProvider.DebugLog(
            $"[SNIFFER] Buffer ContentType: \"{ct.TypeName}\" (bases: {baseTypes})");
        return null; // don't actually provide any QuickInfo
    }
}
#endif
