using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using SQLExtended.Formatting;
using SQLExtended.IntelliSense;
using SQLExtended.Settings;
using SQLExtended.Snippets;
using System;
using System.ComponentModel.Composition;

namespace SQLExtended.Qualify;

[Export(typeof(IVsTextViewCreationListener))]
[ContentType("SQL")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
[Name("SQLExtended Star Expand Command Filter")]
internal sealed class StarExpandCommandFilterProvider : IVsTextViewCreationListener
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

        wpfView.Properties.GetOrCreateSingletonProperty(() => new StarExpandCommandFilter(textViewAdapter, wpfView, CompletionBroker));
    }
}

/// <summary>
/// Tab straight after a SELECT-list <c>*</c> or <c>alias.*</c> expands it to the column list, each column prefixed with its
/// table's alias (GitHub issue #4) — <see cref="ColumnQualifier.ExpandStar"/> does the work, under one undo step.
///
/// <para>Tab is passed on untouched unless the character before the caret is a <c>*</c> the context analyzer reads as a
/// SELECT-list star, with no completion list or snippet open. Past that gate the Tab is always swallowed, expanded or not:
/// a tab character typed after the <c>*</c> would stop the next Tab from finding it, and the status bar says why.</para>
/// </summary>
internal sealed class StarExpandCommandFilter : IOleCommandTarget
{
    private const string Title = "Expand *";

    private readonly IWpfTextView _textView;
    private readonly IAsyncCompletionBroker _broker;
    private readonly IOleCommandTarget _next;

    public StarExpandCommandFilter(IVsTextView textViewAdapter, IWpfTextView textView, IAsyncCompletionBroker broker)
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

        if (pguidCmdGroup == VSConstants.VSStd2K && (VSConstants.VSStd2KCmdID)nCmdID == VSConstants.VSStd2KCmdID.TAB)
        {
            try
            {
                if (HandleTab())
                    return VSConstants.S_OK;
            }
            catch (Exception ex)
            {
                // Expanding must never cost the user the keystroke - fall through to an ordinary Tab.
                Diagnostics.SQLExtendedLog.Error("StarExpand", "Tab after * failed", ex);
            }
        }

        return _next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    private bool HandleTab()
    {
        if (!SQLExtendedSettings.Current.TabExpandsSelectStar || !_textView.Selection.IsEmpty || _textView.Caret.InVirtualSpace)
            return false;

        // An open list owns Tab (it commits the selected item), and so does a snippet moving between its fields.
        if (_broker?.IsCompletionActive(_textView) == true)
            return false;
        if (_textView.Properties.TryGetProperty(typeof(SnippetSession), out SnippetSession snippet) && snippet?.IsActive == true)
            return false;

        var snapshot = _textView.TextBuffer.CurrentSnapshot;
        int caret = _textView.Caret.Position.BufferPosition.Position;
        if (caret == 0 || snapshot[caret - 1] != '*')
            return false;

        string text = snapshot.GetText();
        var analysis = SqlContextAnalyzer.Analyze(text, caret);
        if (analysis.Type != SqlContextAnalyzer.CompletionType.StarExpansion)
            return false;

        var context = ColumnLookupContext.TryCreate(out string notReady);
        if (context == null)
        {
            QualifyColumnsCommand.SetStatusBarText($"{Title}: {notReady}");
            return true;
        }

        var layout = Layout(snapshot, caret - analysis.StarReplaceLength);
        var localTables = LocalTableScanner.Scan(text);

        // The batch first, so a table aliased by name elsewhere in it is followed; the statement alone when the batch
        // does not parse, which is the usual state of a script part-way through being written.
        StarExpansionResult result = null;
        int regionStart = 0;
        foreach (var (start, length) in new[] { SqlContextAnalyzer.GetBatchSpan(text, caret), SqlContextAnalyzer.GetStatementSpan(text, caret) })
        {
            regionStart = start;
            result = ColumnQualifier.ExpandStar(text.Substring(start, length), caret - start, t => context.Lookup(t, localTables), layout);
            if (!result.DidNotParse)
                break;
        }

        if (!result.Success)
        {
            QualifyColumnsCommand.SetStatusBarText($"{Title}: {result.ErrorMessage}.");
            return true;
        }

        using (var edit = _textView.TextBuffer.CreateEdit())
        {
            foreach (var (start, length, replacement) in result.Edits)
                edit.Replace(regionStart + start, length, replacement);
            snapshot = edit.Apply();
        }
        _textView.Caret.MoveTo(new SnapshotPoint(snapshot, regionStart + result.ExpansionEnd));

        string aliased = result.TablesAliased == 0 ? "" : result.TablesAliased == 1 ? ", aliased 1 table" : $", aliased {result.TablesAliased} tables";
        QualifyColumnsCommand.SetStatusBarText($"{Title}: {result.ColumnsExpanded} columns{aliased}.");
        return true;
    }

    /// <summary>The formatter's own choices: one line, or one column per line lined up under the first, with its commas.</summary>
    private static StarListLayout Layout(ITextSnapshot snapshot, int starStart)
    {
        FormatterOptions options;
        try { options = FormatterOptions.Load(); }
        catch { options = new FormatterOptions(); }

        var line = snapshot.GetLineFromPosition(starStart);
        string newLine = line.GetLineBreakText();
        if (string.IsNullOrEmpty(newLine))
            newLine = snapshot.LineCount > 1 ? snapshot.GetLineFromLineNumber(0).GetLineBreakText() : Environment.NewLine;

        return new StarListLayout
        {
            Stacked = options.SelectColumnLayout != SelectColumnLayoutOption.SameLine,
            LeadingCommas = options.CommaPosition == CommaPositionOption.LeadingComma,
            LeadingCommaKeepIndent = options.LeadingCommaKeepIndent,
            Indent = StarListLayout.IndentFor(snapshot.GetText(line.Start, starStart - line.Start)),
            NewLine = newLine
        };
    }
}
