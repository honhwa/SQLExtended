using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SQLExtended.Formatting;
using SQLExtended.Settings;
using System;
using System.ComponentModel.Composition;

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
    private readonly ITextView _textView;
    private readonly ITextBuffer _buffer;

    /// <summary>Guards against re-entrancy when our own replace raises <see cref="ITextBuffer.Changed"/>.</summary>
    private bool _applying;

    public KeywordCaseController(ITextView textView)
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

            _applying = true;
            try
            {
                using var edit = _buffer.CreateEdit();
                edit.Replace(new Span(tokenStart, len), cased);
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
