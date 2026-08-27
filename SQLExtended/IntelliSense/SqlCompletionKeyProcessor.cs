using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.Windows.Input;

namespace SQLExtended.IntelliSense;

/// <summary>
/// MEF-exported provider that attaches <see cref="SqlCompletionKeyProcessor"/> to SQL editor views.
/// The key processor handles Ctrl+Space to manually trigger the async completion session,
/// since SSMS's legacy IntelliSense can intercept the standard binding before it reaches the broker.
/// </summary>
[Export(typeof(IKeyProcessorProvider))]
[Name("SQLExtended SQL Completion KeyProcessor")]
[ContentType("SQL")]
[Order(Before = "default")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class SqlCompletionKeyProcessorProvider : IKeyProcessorProvider
{
    [Import]
    internal IAsyncCompletionBroker CompletionBroker { get; set; }

    public KeyProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
    {
        return wpfTextView.Properties.GetOrCreateSingletonProperty(
            () => new SqlCompletionKeyProcessor(wpfTextView, CompletionBroker));
    }
}

/// <summary>
/// Intercepts Ctrl+Space on the SQL editor and triggers the async completion broker explicitly.
/// </summary>
internal sealed class SqlCompletionKeyProcessor : KeyProcessor
{
    private readonly IWpfTextView _textView;
    private readonly IAsyncCompletionBroker _broker;

    public SqlCompletionKeyProcessor(IWpfTextView textView, IAsyncCompletionBroker broker)
    {
        _textView = textView;
        _broker = broker;
    }

    public override bool IsInterestedInHandledEvents => true;

    public override void PreviewKeyDown(KeyEventArgs args)
    {
        if (TryTriggerCompletion(args))
            return;

        base.PreviewKeyDown(args);
    }

    private bool TryTriggerCompletion(KeyEventArgs args)
    {
        // Accept Ctrl+Space or Ctrl+, (comma) — SSMS intercepts Ctrl+Space at a lower level
        if (args.Key != Key.Space && args.Key != Key.OemComma)
            return false;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        if (!ctrl || shift || alt)
            return false;

        try
        {
            var caretPoint = _textView.Caret.Position.BufferPosition;

            // If a session is already open, leave it alone
            var existing = _broker?.GetSession(_textView);
            if (existing != null)
                return false;

            var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, caretPoint.Snapshot, '\0');
            var session = _broker?.TriggerCompletion(_textView, trigger, caretPoint, default);

            if (session != null)
            {
                // TriggerCompletion creates the session but doesn't request items —
                // OpenOrUpdate forces the source to compute items and show the UI.
                session.OpenOrUpdate(trigger, caretPoint, default);
                SqlCompletionSource.DebugLog($"[KeyProcessor] Ctrl+{args.Key} triggered + OpenOrUpdate");
                args.Handled = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            SqlCompletionSource.DebugLog($"[KeyProcessor] Ctrl+Space failed: {ex.Message}");
        }

        return false;
    }
}
