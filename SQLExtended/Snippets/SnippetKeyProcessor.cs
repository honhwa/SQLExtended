using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SQLExtended.IntelliSense;
using System.ComponentModel.Composition;
using System.Windows.Input;

namespace SQLExtended.Snippets;

/// <summary>
/// MEF-exported provider that attaches a <see cref="SnippetKeyProcessor"/> to SQL editor views.
/// The key processor intercepts Tab, Shift+Tab, Enter, and Escape during active snippet sessions.
/// </summary>
[Export(typeof(IKeyProcessorProvider))]
[Name("SQLExtended Snippet KeyProcessor")]
[ContentType("SQL")]
[Order(Before = "default")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class SnippetKeyProcessorProvider : IKeyProcessorProvider
{
    public KeyProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
    {
        return wpfTextView.Properties.GetOrCreateSingletonProperty(
            () => new SnippetKeyProcessor(wpfTextView));
    }
}

/// <summary>
/// Intercepts keyboard input during an active <see cref="SnippetSession"/>.
/// Tab advances to the next field, Shift+Tab goes back, Enter/Escape ends the session.
/// Handles both Preview (tunneling) and regular (bubbling) phases to ensure
/// the key event is fully consumed and doesn't reach the editor.
/// </summary>
internal sealed class SnippetKeyProcessor : KeyProcessor
{
    private readonly ITextView _textView;

    public SnippetKeyProcessor(ITextView textView)
    {
        _textView = textView;
    }

    public override bool IsInterestedInHandledEvents => true;

    public override void PreviewKeyDown(KeyEventArgs args)
    {
        if (HandleSnippetKey(args, "PreviewKeyDown"))
            return;

        base.PreviewKeyDown(args);
    }

    public override void KeyDown(KeyEventArgs args)
    {
        // Safety net: if PreviewKeyDown didn't fully suppress the event
        if (HandleSnippetKey(args, "KeyDown"))
            return;

        base.KeyDown(args);
    }

    private bool HandleSnippetKey(KeyEventArgs args, string phase)
    {
        var session = GetActiveSession();
        if (session == null || !session.IsActive)
            return false;

        if (args.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (!args.Handled)
            {
                SqlCompletionSource.DebugLog($"[KeyProcessor] {phase}: Tab — next field");
                session.MoveNext();
            }
            args.Handled = true;
            return true;
        }

        if (args.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (!args.Handled)
            {
                SqlCompletionSource.DebugLog($"[KeyProcessor] {phase}: Shift+Tab — prev field");
                session.MovePrevious();
            }
            args.Handled = true;
            return true;
        }

        if (args.Key == Key.Return || args.Key == Key.Enter)
        {
            if (!args.Handled)
            {
                SqlCompletionSource.DebugLog($"[KeyProcessor] {phase}: Enter — end session");
                session.End();
            }
            args.Handled = true;
            return true;
        }

        if (args.Key == Key.Escape)
        {
            if (!args.Handled)
            {
                SqlCompletionSource.DebugLog($"[KeyProcessor] {phase}: Escape — cancel");
                session.Cancel();
            }
            args.Handled = true;
            return true;
        }

        return false;
    }

    private SnippetSession GetActiveSession()
    {
        _textView.Properties.TryGetProperty(typeof(SnippetSession), out SnippetSession session);
        return session;
    }
}
