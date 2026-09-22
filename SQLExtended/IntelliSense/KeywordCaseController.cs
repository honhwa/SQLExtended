using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SQLExtended.Formatting;
using SQLExtended.Settings;
using System;
using System.ComponentModel.Composition;
using System.Windows.Threading;

namespace SQLExtended.IntelliSense;

/// <summary>
/// MEF listener that attaches a <see cref="KeywordCaseController"/> to every editable SQL view.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[Name("SQLExtended Keyword Case Controller")]
[ContentType("SQL")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class KeywordCaseControllerProvider : IWpfTextViewCreationListener
{
    public void TextViewCreated(IWpfTextView textView)
    {
        textView.Properties.GetOrCreateSingletonProperty(() => new KeywordCaseController(textView));
    }
}

/// <summary>
/// Recases SQL keywords as the user types. When a word is completed by a non-word boundary
/// character (space, punctuation, newline), and that word is a standalone T-SQL keyword, it is
/// rewritten to match the active formatter profile's keyword casing (Upper/Lower). Casing is
/// length-preserving, so the replacement never shifts the caret or any tracking positions.
///
/// Complements the completion-time keyword casing: that path only cases keywords picked from the
/// IntelliSense list, while this covers keywords typed by hand. No-ops when the feature is off,
/// when keyword casing is Unchanged, or when the word sits inside a string literal, comment, or
/// [bracketed] identifier. Never touches identifiers (they carry digits/underscores/@/# and so
/// never match the keyword-word set).
/// </summary>
internal sealed class KeywordCaseController
{
    private readonly IWpfTextView _textView;
    private readonly ITextBuffer _buffer;

    /// <summary>Guards against re-entrancy when our own replace raises <see cref="ITextBuffer.Changed"/>.</summary>
    private bool _applying;

    public KeywordCaseController(IWpfTextView textView)
    {
        _textView = textView;
        _buffer = textView.TextBuffer;
        _buffer.Changed += OnChanged;
        _textView.Closed += OnClosed;
    }

    private void OnClosed(object sender, EventArgs e)
    {
        _buffer.Changed -= OnChanged;
        _textView.Closed -= OnClosed;
    }

    private void OnChanged(object sender, TextContentChangedEventArgs e)
    {
        if (_applying)
            return;

        try
        {
            if (!SQLExtendedSettings.Current.RecaseKeywordsWhileTyping)
                return;

            var casing = FormatterProfileManager.Instance.GetActiveCasing().Keyword;
            if (casing == CasingOption.Unchanged)
                return;

            // Only a single, pure insertion (no overtype/replace) counts as "finishing a word".
            if (e.Changes.Count != 1)
                return;
            var change = e.Changes[0];
            if (change.OldLength != 0 || change.NewLength == 0)
                return;

            // The insertion must START with a boundary char — that's what completes the word the
            // caret was sitting on. A word char means the user is still extending the word (or a
            // completion just inserted one), so leave it alone. This naturally covers Enter, whose
            // inserted text ("\r\n") starts with a boundary char.
            if (IsWordChar(change.NewText[0]))
                return;

            var snapshot = e.After;
            int tokenEnd = change.NewPosition; // the word sits immediately before the insertion point
            int tokenStart = tokenEnd;
            while (tokenStart > 0 && IsWordChar(snapshot[tokenStart - 1]))
                tokenStart--;

            int len = tokenEnd - tokenStart;
            if (len < 2)
                return;

            string token = snapshot.GetText(tokenStart, len);
            if (!SqlKeywords.IsKeywordWord(token))
                return;

            string cased = casing == CasingOption.Upper
                ? token.ToUpperInvariant()
                : token.ToLowerInvariant();
            if (string.Equals(cased, token, StringComparison.Ordinal))
                return;

            var line = snapshot.GetLineFromPosition(tokenStart);
            if (IsInStringCommentOrBracket(line.GetText(), tokenStart - line.Start.Position))
                return;

            // Queued, not applied here — see QueueRecase.
            QueueRecase(snapshot.CreateTrackingSpan(tokenStart, len, SpanTrackingMode.EdgeExclusive), token, cased);
        }
        catch
        {
            // A recasing failure must never disrupt typing.
        }
    }

    /// <summary>
    /// Applies the recase once the keystroke that triggered it has finished, rather than from inside the
    /// <see cref="ITextBuffer.Changed"/> handler.
    ///
    /// <para>Two things were wrong with replacing in place. The edit was <b>re-entrant</b> — applied to the
    /// buffer that was still raising the event for the character just typed, with the editor's own typing and
    /// caret handling part way through it. And the span was computed against <c>e.After</c> but applied to
    /// <c>CurrentSnapshot</c>, which need not be the same snapshot: any other <c>Changed</c> handler that
    /// edits ahead of this one (the snippet session's linked-field sync is one) moves every offset, and the
    /// replace then lands in the wrong place and duplicates text rather than recasing it. Both failures are
    /// silent — the catch above swallows them, and what reaches the screen is mangled typing with nothing
    /// naming the cause.</para>
    ///
    /// <para>Posted at <see cref="DispatcherPriority.Normal"/>, which runs ahead of pending input, so the
    /// recase still lands before the next keystroke is handled and fast typing cannot outrun it.</para>
    /// </summary>
    private void QueueRecase(ITrackingSpan tracking, string expected, string cased)
    {
        var dispatcher = _textView.VisualElement?.Dispatcher;
        if (dispatcher == null)
            return;

        dispatcher.BeginInvoke(new Action(() => ApplyRecase(tracking, expected, cased)), DispatcherPriority.Normal);
    }

    /// <summary>
    /// Re-validates against the snapshot as it is now and replaces only if the word is still there, unchanged.
    /// Anything else — the user kept typing, an undo ran, a completion replaced the span — means the recase no
    /// longer applies, and applying it anyway is what corrupts the line.
    /// </summary>
    private void ApplyRecase(ITrackingSpan tracking, string expected, string cased)
    {
        if (_applying)
            return;

        try
        {
            var snapshot = _buffer.CurrentSnapshot;
            var span = tracking.GetSpan(snapshot);
            if (span.Length != expected.Length || !string.Equals(span.GetText(), expected, StringComparison.Ordinal))
                return;

            _applying = true;
            try
            {
                using var edit = _buffer.CreateEdit();
                edit.Replace(span, cased);
                edit.Apply();
            }
            finally
            {
                _applying = false;
            }
        }
        catch
        {
            // A recasing failure must never disrupt typing.
        }
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';

    /// <summary>
    /// Line-scoped check: is column <paramref name="upto"/> inside a single-quoted string, a
    /// line/block comment, or a [bracketed] identifier? Strings/comments spanning multiple lines
    /// aren't tracked — a deliberate, cheap approximation for a per-keystroke hook.
    /// </summary>
    private static bool IsInStringCommentOrBracket(string lineText, int upto)
    {
        bool inString = false, inBracket = false;
        int n = Math.Min(upto, lineText.Length);
        for (int i = 0; i < n; i++)
        {
            char c = lineText[i];
            if (inString)
            {
                if (c == '\'') inString = false;
                continue;
            }
            if (inBracket)
            {
                if (c == ']') inBracket = false;
                continue;
            }
            if (c == '\'') { inString = true; continue; }
            if (c == '[') { inBracket = true; continue; }
            if (c == '-' && i + 1 < lineText.Length && lineText[i + 1] == '-') return true;
            if (c == '/' && i + 1 < lineText.Length && lineText[i + 1] == '*') return true;
        }
        return inString || inBracket;
    }
}
