using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SQLExtended.IntelliSense;
using System;
using System.ComponentModel.Composition;

namespace SQLExtended.Snippets;

/// <summary>
/// MEF-exported listener that installs a <see cref="SnippetCommandFilter"/> on each SQL text view.
/// The command filter intercepts Tab/Backtab/Return/Escape via the VS IOleCommandTarget chain,
/// which is how SSMS routes keyboard commands (WPF KeyProcessor doesn't work in SSMS SQL editors).
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[Name("SQLExtended Snippet Command Filter")]
[ContentType("SQL")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class SnippetCommandFilterProvider : IWpfTextViewCreationListener
{
    [Import]
    internal IVsEditorAdaptersFactoryService AdapterFactory { get; set; }

    public void TextViewCreated(IWpfTextView textView)
    {
        var vsTextView = AdapterFactory.GetViewAdapter(textView);
        if (vsTextView == null)
        {
            SqlCompletionSource.DebugLog("[CommandFilter] GetViewAdapter returned null, skipping");
            return;
        }

        var filter = new SnippetCommandFilter(textView);
        textView.Properties.AddProperty(typeof(SnippetCommandFilter), filter);

        vsTextView.AddCommandFilter(filter, out var nextTarget);
        filter.NextTarget = nextTarget;

        SqlCompletionSource.DebugLog("[CommandFilter] Installed on SQL text view");
    }
}

/// <summary>
/// IOleCommandTarget that intercepts Tab, Shift+Tab, Enter, and Escape during
/// an active <see cref="SnippetSession"/>. Passes all other commands through.
/// </summary>
internal sealed class SnippetCommandFilter : IOleCommandTarget
{
    private readonly ITextView _textView;

    public IOleCommandTarget NextTarget { get; set; }

    public SnippetCommandFilter(ITextView textView)
    {
        _textView = textView;
    }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var session = GetActiveSession();
        if (session != null && session.IsActive)
        {
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                for (int i = 0; i < cCmds; i++)
                {
                    switch ((VSConstants.VSStd2KCmdID)prgCmds[i].cmdID)
                    {
                        case VSConstants.VSStd2KCmdID.TAB:
                        case VSConstants.VSStd2KCmdID.BACKTAB:
                        case VSConstants.VSStd2KCmdID.RETURN:
                        case VSConstants.VSStd2KCmdID.CANCEL:
                            prgCmds[i].cmdf = (uint)(OLECMDF.OLECMDF_ENABLED | OLECMDF.OLECMDF_SUPPORTED);
                            return VSConstants.S_OK;
                    }
                }
            }
        }

        return NextTarget?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText)
            ?? (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var session = GetActiveSession();
        if (session != null && session.IsActive && pguidCmdGroup == VSConstants.VSStd2K)
        {
            switch ((VSConstants.VSStd2KCmdID)nCmdID)
            {
                case VSConstants.VSStd2KCmdID.TAB:
                    SqlCompletionSource.DebugLog("[CommandFilter] TAB — next field");
                    session.MoveNext();
                    return VSConstants.S_OK;

                case VSConstants.VSStd2KCmdID.BACKTAB:
                    SqlCompletionSource.DebugLog("[CommandFilter] BACKTAB — prev field");
                    session.MovePrevious();
                    return VSConstants.S_OK;

                case VSConstants.VSStd2KCmdID.RETURN:
                    SqlCompletionSource.DebugLog("[CommandFilter] RETURN — end session");
                    session.End();
                    return VSConstants.S_OK;

                case VSConstants.VSStd2KCmdID.CANCEL:
                    SqlCompletionSource.DebugLog("[CommandFilter] CANCEL — cancel session");
                    session.Cancel();
                    return VSConstants.S_OK;
            }
        }

        return NextTarget?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
            ?? (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private SnippetSession GetActiveSession()
    {
        _textView.Properties.TryGetProperty(typeof(SnippetSession), out SnippetSession session);
        return session;
    }
}
