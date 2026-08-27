using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace SQLExtended.IntelliSense;

/// <summary>
/// MEF-exported provider that creates SignatureHelpSource instances for SQL editor buffers.
/// Triggers when the user types inside a stored procedure or function call to show parameter hints.
/// </summary>
[Export(typeof(ISignatureHelpSourceProvider))]
[Name("SQLExtended SQL Signature Help")]
[ContentType("SQL")]
[Order(Before = "default")]
internal sealed class SignatureHelpSourceProvider : ISignatureHelpSourceProvider
{
    public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer)
    {
        return textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new SignatureHelpSource(textBuffer));
    }
}
