using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Attaches an <see cref="IOleCommandTarget"/> filter to each SQL text view so we can
/// intercept Ctrl+Space (Edit.CompleteWord / Edit.ListMembers) before SSMS's legacy
/// T-SQL IntelliSense consumes it, and invoke our async completion broker instead.
/// </summary>
[Export(typeof(IVsTextViewCreationListener))]
[ContentType("SQL")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
[Name("SQLExtended SQL Completion Command Filter")]
internal sealed class SqlCompletionCommandFilterProvider : IVsTextViewCreationListener
{
    [Import]
    internal IVsEditorAdaptersFactoryService AdapterFactory { get; set; }

    [Import]
    internal IAsyncCompletionBroker CompletionBroker { get; set; }

    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        var wpfView = AdapterFactory?.GetWpfTextView(textViewAdapter);
        if (wpfView == null)
        {
            SqlCompletionSource.DebugLog("[CmdFilter] VsTextViewCreated — wpfView null, skipping");
            return;
        }

        SqlCompletionSource.DebugLog($"[CmdFilter] VsTextViewCreated — attaching, contentType={wpfView.TextBuffer.ContentType.TypeName}");

        wpfView.Properties.GetOrCreateSingletonProperty(
            () => new SqlCompletionCommandFilter(textViewAdapter, wpfView, CompletionBroker));
    }
}

internal sealed class SqlCompletionCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _textView;
    private readonly IAsyncCompletionBroker _broker;
    private readonly IOleCommandTarget _next;

    public SqlCompletionCommandFilter(
        IVsTextView textViewAdapter, IWpfTextView textView, IAsyncCompletionBroker broker)
    {
        _textView = textView;
        _broker = broker;

        // Add ourselves to the command chain, keeping a reference to the next handler
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
            SqlCompletionSource.DebugLog($"[CmdFilter] Exec VSStd2K.{(VSConstants.VSStd2KCmdID)nCmdID}");

        // VSStd2K commands — Ctrl+Space maps to COMPLETEWORD / AUTOCOMPLETE / SHOWMEMBERLIST
        if (pguidCmdGroup == VSConstants.VSStd2K)
        {
            var cmd = (VSConstants.VSStd2KCmdID)nCmdID;
            if (cmd == VSConstants.VSStd2KCmdID.COMPLETEWORD ||
                cmd == VSConstants.VSStd2KCmdID.AUTOCOMPLETE ||
                cmd == VSConstants.VSStd2KCmdID.SHOWMEMBERLIST)
            {
                SqlCompletionSource.DebugLog($"[CmdFilter] Intercepted VSStd2K.{cmd}");
                if (TryTrigger())
                    return VSConstants.S_OK; // we handled it — don't pass through
            }
        }

        return _next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
               ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private bool TryTrigger()
    {
        try
        {
            if (_broker == null) return false;

            var existing = _broker.GetSession(_textView);
            if (existing != null)
            {
                // Re-filter/refresh the existing session rather than opening a new one
                return false;
            }

            var caret = _textView.Caret.Position.BufferPosition;
            var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, caret.Snapshot, '\0');
            var session = _broker.TriggerCompletion(_textView, trigger, caret, default);

            if (session != null)
            {
                session.OpenOrUpdate(trigger, caret, default);
                SqlCompletionSource.DebugLog("[CmdFilter] Ctrl+Space triggered + OpenOrUpdate");
                return true;
            }
        }
        catch (Exception ex)
        {
            SqlCompletionSource.DebugLog($"[CmdFilter] Ctrl+Space failed: {ex.Message}");
        }

        return false;
    }
}
