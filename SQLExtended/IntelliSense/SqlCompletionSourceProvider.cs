using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace SQLExtended.IntelliSense;

/// <summary>
/// MEF-exported provider that creates SqlCompletionSource instances for SQL editor buffers.
/// Runs before the default completion provider to take priority over built-in IntelliSense.
/// </summary>
[Export(typeof(IAsyncCompletionSourceProvider))]
[Name("SQLExtended SQL Completion")]
[ContentType("SQL")]
[Order(Before = "default")]
internal sealed class SqlCompletionSourceProvider : IAsyncCompletionSourceProvider
{
    public IAsyncCompletionSource GetOrCreate(ITextView textView)
    {
        return textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource());
    }
}
