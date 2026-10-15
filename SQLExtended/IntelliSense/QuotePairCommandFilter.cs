using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using SQLExtended.Settings;
using System;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;

namespace SQLExtended.IntelliSense;

[Export(typeof(IVsTextViewCreationListener))]
[ContentType("SQL")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
[Name("SQLExtended Quote Pair Command Filter")]
internal sealed class QuotePairCommandFilterProvider : IVsTextViewCreationListener
{
    [Import]
    internal IVsEditorAdaptersFactoryService AdapterFactory { get; set; }

    [Import]
    internal IAsyncCompletionBroker CompletionBroker { get; set; }

    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        var wpfView = AdapterFactory?.GetWpfTextView(textViewAdapter);
        if (wpfView == null)
            return;

        wpfView.Properties.GetOrCreateSingletonProperty(() => new QuotePairCommandFilter(textViewAdapter, wpfView, CompletionBroker));
    }
}

/// <summary>
/// Typing <c>'</c> in code inserts <c>''</c> with the caret between them; typing <c>'</c> on the closing quote it
/// inserted steps over it; Backspace on an empty pair it inserted removes both. Only the closing quote this
/// filter put there is ever stepped over or deleted - an existing literal is edited exactly as before.
///
/// <para>The quote is handled here rather than passed down the chain and patched afterwards. Passed down, the
/// keystroke can open a completion session whose applicable span ends at the caret, and a closing quote
/// inserted there afterwards lands inside that span and is read as part of the filter text.</para>
/// </summary>
internal sealed class QuotePairCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _textView;
    private readonly IAsyncCompletionBroker _broker;
    private readonly IOleCommandTarget _next;

    /// <summary>The opening and closing quote of the last pair inserted, while it is still live.</summary>
    private ITrackingPoint _opener, _closer;

    public QuotePairCommandFilter(IVsTextView textViewAdapter, IWpfTextView textView, IAsyncCompletionBroker broker)
    {
        _textView = textView;
        _broker = broker;

        ThreadHelper.ThrowIfNotOnUIThread();
        textViewAdapter.AddCommandFilter(this, out _next);
    }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return _next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (pguidCmdGroup == VSConstants.VSStd2K)
        {
            try
            {
                var cmd = (VSConstants.VSStd2KCmdID)nCmdID;
                if (cmd == VSConstants.VSStd2KCmdID.TYPECHAR && pvaIn != IntPtr.Zero
                    && (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn) == '\'' && HandleQuote())
                    return VSConstants.S_OK;
                if (cmd == VSConstants.VSStd2KCmdID.BACKSPACE && HandleBackspace())
                    return VSConstants.S_OK;
            }
            catch
            {
                // Pairing must never cost the user the keystroke - fall through to normal typing.
            }
        }

        return _next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private bool HandleQuote()
    {
        if (!SQLExtendedSettings.Current.AutoCloseQuotes || !_textView.Selection.IsEmpty || _textView.Caret.InVirtualSpace)
            return false;

        var snapshot = _textView.TextBuffer.CurrentSnapshot;
        int caret = _textView.Caret.Position.BufferPosition.Position;

        // On the closing quote we inserted: step over it rather than typing a second one.
        if (TryGetLivePair(snapshot, out _, out int closer) && caret == closer)
        {
            Clear();
            _textView.Caret.MoveTo(new SnapshotPoint(snapshot, caret + 1));
            return true;
        }

        char next = caret < snapshot.Length ? snapshot[caret] : '\0';
        if (!SqlQuotePairing.ShouldPair(snapshot.GetText(0, caret), next))
            return false;

        // Typed over an open list the quote would have filtered it to nothing and dismissed it anyway.
        _broker?.GetSession(_textView)?.Dismiss();

        using (var edit = _textView.TextBuffer.CreateEdit())
        {
            edit.Insert(caret, "''");
            snapshot = edit.Apply();
        }

        _textView.Caret.MoveTo(new SnapshotPoint(snapshot, caret + 1));
        _opener = snapshot.CreateTrackingPoint(caret, PointTrackingMode.Negative);
        _closer = snapshot.CreateTrackingPoint(caret + 1, PointTrackingMode.Positive);
        return true;
    }

    private bool HandleBackspace()
    {
        if (!_textView.Selection.IsEmpty)
            return false;

        var snapshot = _textView.TextBuffer.CurrentSnapshot;
        int caret = _textView.Caret.Position.BufferPosition.Position;
        if (!TryGetLivePair(snapshot, out int opener, out int closer) || closer != opener + 1 || caret != closer)
            return false;

        Clear();
        using (var edit = _textView.TextBuffer.CreateEdit())
        {
            edit.Delete(opener, 2);
            edit.Apply();
        }
        return true;
    }

    /// <summary>
    /// The pair is live while both quotes are still where it put them, on one line, with the caret between.
    /// Anything else - the caret left, the line was edited around them, an undo ran - retires it for good.
    /// </summary>
    private bool TryGetLivePair(ITextSnapshot snapshot, out int opener, out int closer)
    {
        opener = closer = -1;
        if (_opener == null || _closer == null)
            return false;

        opener = _opener.GetPosition(snapshot);
        closer = _closer.GetPosition(snapshot);
        int caret = _textView.Caret.Position.BufferPosition.Position;
        bool live = closer > opener && closer < snapshot.Length && snapshot[opener] == '\'' && snapshot[closer] == '\''
                    && caret > opener && caret <= closer
                    && snapshot.GetLineNumberFromPosition(opener) == snapshot.GetLineNumberFromPosition(closer);
        if (!live)
            Clear();
        return live;
    }

    private void Clear() => _opener = _closer = null;
}
