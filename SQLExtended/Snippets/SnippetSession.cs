using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SQLExtended.IntelliSense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SQLExtended.Snippets;

/// <summary>
/// Represents a single tab-stop field in an active snippet session.
/// Multiple spans with the same name are linked (editing one updates all).
/// </summary>
internal sealed class SnippetField
{
    public string Name { get; }
    public string DefaultValue { get; }
    public List<ITrackingSpan> Spans { get; } = new List<ITrackingSpan>();

    public SnippetField(string name, string defaultValue)
    {
        Name = name;
        DefaultValue = defaultValue;
    }
}

/// <summary>
/// Manages an active snippet editing session. Tracks placeholder fields as
/// <see cref="ITrackingSpan"/> instances and provides Tab/Shift-Tab navigation.
/// </summary>
internal sealed class SnippetSession
{
    private static readonly Regex PlaceholderPattern = new Regex(
        @"\$([a-zA-Z_][a-zA-Z0-9_]*)\$", RegexOptions.Compiled);

    private const string CursorPlaceholder = "cursor";

    private readonly ITextView _textView;
    private readonly List<SnippetField> _fields = new List<SnippetField>();
    private int _currentFieldIndex = -1;
    private bool _isActive;
    private bool _isSyncing;
    private ITrackingPoint _cursorPosition;

    public bool IsActive => _isActive;

    public SnippetSession(ITextView textView)
    {
        _textView = textView;
    }

    /// <summary>
    /// Inserts the snippet body into the editor at <paramref name="replacementSpan"/>,
    /// resolving system placeholders and substituting custom placeholder defaults.
    /// Creates tracking spans for each custom placeholder field.
    /// </summary>
    public bool Start(SqlSnippet snippet, SnapshotSpan replacementSpan)
    {
        if (snippet == null || _isActive)
            return false;

        var defaults = snippet.Defaults ?? new Dictionary<string, string>();
        string body = snippet.Body;

        // Interactive insertion runs on the UI thread, so refresh the connection-derived
        // placeholders ($dbname$, $server$) to live values before resolving.
        ThreadHelper.ThrowIfNotOnUIThread();
        SnippetPlaceholderResolver.RefreshConnectionInfoFromSsms();

        // Resolve system placeholders first
        body = SnippetPlaceholderResolver.ResolveSystemOnly(body);

        // Find custom placeholder positions BEFORE substituting defaults
        var customNames = SnippetPlaceholderResolver.GetCustomPlaceholderNames(body);
        bool hasCursor = body.IndexOf("$cursor$", StringComparison.OrdinalIgnoreCase) >= 0;

        if (customNames.Count == 0 && !hasCursor)
            return false;

        SqlCompletionSource.DebugLog($"[Session] Starting for '{snippet.Code}' with {customNames.Count} fields");

        // Build the final text by replacing $placeholder$ with default values,
        // tracking where each field lands
        var fieldMap = new Dictionary<string, SnippetField>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in customNames)
        {
            string def = defaults.TryGetValue(name, out string val) ? val : name;
            fieldMap[name] = new SnippetField(name, def);
        }

        // Process the body: replace each $placeholder$ with its default and record positions.
        // $cursor$ is special — it's removed from the text and its position is saved for
        // final caret placement when the session ends.
        var buffer = _textView.TextBuffer;
        int insertionStart = replacementSpan.Start.Position;

        string substituted;
        var fieldPositions = new List<(string Name, int Start, int Length)>();
        int cursorOffset = -1;
        {
            var result = new System.Text.StringBuilder();
            int lastEnd = 0;

            foreach (Match m in PlaceholderPattern.Matches(body))
            {
                string name = m.Groups[1].Value;

                // $cursor$ — record position and strip from output
                if (string.Equals(name, CursorPlaceholder, StringComparison.OrdinalIgnoreCase))
                {
                    result.Append(body, lastEnd, m.Index - lastEnd);
                    cursorOffset = result.Length;
                    lastEnd = m.Index + m.Length;
                    continue;
                }

                // Skip system placeholders (already resolved, shouldn't be here)
                if (!fieldMap.ContainsKey(name))
                {
                    result.Append(body, lastEnd, m.Index + m.Length - lastEnd);
                    lastEnd = m.Index + m.Length;
                    continue;
                }

                // Append text before this placeholder
                result.Append(body, lastEnd, m.Index - lastEnd);
                int offset = result.Length;

                // Append the default value
                string defaultVal = fieldMap[name].DefaultValue;
                result.Append(defaultVal);

                fieldPositions.Add((name, offset, defaultVal.Length));

                lastEnd = m.Index + m.Length;
            }

            // Append remaining text
            result.Append(body, lastEnd, body.Length - lastEnd);
            substituted = result.ToString();
        }

        // Replace the typed prefix with the full snippet text
        using (var edit = buffer.CreateEdit())
        {
            edit.Replace(replacementSpan.Span, substituted);
            edit.Apply();
        }

        // Create tracking spans for each field position.
        // EdgeInclusive so the span grows when the user types within it.
        var snapshot = buffer.CurrentSnapshot;
        foreach (var (name, start, length) in fieldPositions)
        {
            var span = snapshot.CreateTrackingSpan(
                new Span(insertionStart + start, length),
                SpanTrackingMode.EdgeInclusive);
            fieldMap[name].Spans.Add(span);
        }

        // Track $cursor$ position if present
        if (cursorOffset >= 0)
        {
            _cursorPosition = snapshot.CreateTrackingPoint(
                insertionStart + cursorOffset, PointTrackingMode.Positive);
        }
        else
        {
            _cursorPosition = null;
        }

        // Build ordered field list (in order of first appearance)
        _fields.Clear();
        foreach (var name in customNames)
        {
            if (fieldMap.TryGetValue(name, out var field) && field.Spans.Count > 0)
                _fields.Add(field);
        }

        if (_fields.Count == 0 && _cursorPosition == null)
            return false;

        _isActive = true;
        _currentFieldIndex = 0;

        // Cursor-only snippet (no tab-stop fields) — just place caret and finish
        if (_fields.Count == 0)
        {
            End();
            return true;
        }

        // Listen for text changes to sync linked fields
        _textView.TextBuffer.Changed += OnBufferChanged;
        _textView.Closed += OnViewClosed;

        // Select the first field
        SelectCurrentField();

        SqlCompletionSource.DebugLog($"[Session] Active with {_fields.Count} fields, first='{_fields[0].Name}'");
        return true;
    }

    /// <summary>Moves to the next field. Returns false if at the last field (session should end).</summary>
    public bool MoveNext()
    {
        if (!_isActive)
            return false;

        _currentFieldIndex++;
        if (_currentFieldIndex >= _fields.Count)
        {
            End();
            return false;
        }

        SelectCurrentField();
        return true;
    }

    /// <summary>Moves to the previous field. Returns false if at the first field.</summary>
    public bool MovePrevious()
    {
        if (!_isActive || _currentFieldIndex <= 0)
            return false;

        _currentFieldIndex--;
        SelectCurrentField();
        return true;
    }

    /// <summary>Ends the snippet session, placing the caret at $cursor$ position or end of snippet.</summary>
    public void End()
    {
        if (!_isActive)
            return;

        _isActive = false;
        _textView.TextBuffer.Changed -= OnBufferChanged;
        _textView.Closed -= OnViewClosed;

        // Clear selection first
        _textView.Selection.Clear();

        var snapshot = _textView.TextBuffer.CurrentSnapshot;

        if (_cursorPosition != null)
        {
            // Place caret at $cursor$ position
            var point = _cursorPosition.GetPoint(snapshot);
            _textView.Caret.MoveTo(point);
            SqlCompletionSource.DebugLog($"[Session] Ended — caret at $cursor$ position {point.Position}");
        }
        else if (_fields.Count > 0)
        {
            // Fall back to end of last field
            var lastField = _fields[_fields.Count - 1];
            if (lastField.Spans.Count > 0)
            {
                var span = lastField.Spans[lastField.Spans.Count - 1].GetSpan(snapshot);
                _textView.Caret.MoveTo(new SnapshotPoint(snapshot, span.End));
            }
            SqlCompletionSource.DebugLog("[Session] Ended — caret at end of last field");
        }

        _fields.Clear();
        _cursorPosition = null;
    }

    /// <summary>Cancels the session without moving the caret.</summary>
    public void Cancel()
    {
        if (!_isActive)
            return;

        _isActive = false;
        _textView.TextBuffer.Changed -= OnBufferChanged;
        _textView.Closed -= OnViewClosed;
        _fields.Clear();
        SqlCompletionSource.DebugLog("[Session] Cancelled");
    }

    private void SelectCurrentField()
    {
        if (_currentFieldIndex < 0 || _currentFieldIndex >= _fields.Count)
            return;

        var field = _fields[_currentFieldIndex];
        if (field.Spans.Count == 0)
            return;

        // Select the first span of the current field
        var span = field.Spans[0].GetSpan(_textView.TextBuffer.CurrentSnapshot);
        _textView.Selection.Select(new SnapshotSpan(span.Start, span.End), isReversed: false);
        _textView.Caret.MoveTo(span.End);

        SqlCompletionSource.DebugLog($"[Session] Selected field '{field.Name}' at [{span.Start}..{span.End}]");
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        if (!_isActive || _isSyncing)
            return;

        if (_currentFieldIndex < 0 || _currentFieldIndex >= _fields.Count)
            return;

        var field = _fields[_currentFieldIndex];
        if (field.Spans.Count <= 1)
            return; // No linked fields to sync

        // Get the new text from the first (primary) span
        var snapshot = _textView.TextBuffer.CurrentSnapshot;
        string newText;
        try
        {
            newText = field.Spans[0].GetText(snapshot);
        }
        catch
        {
            return;
        }

        // Sync to all other spans of the same field
        _isSyncing = true;
        try
        {
            using (var edit = _textView.TextBuffer.CreateEdit())
            {
                for (int i = 1; i < field.Spans.Count; i++)
                {
                    var span = field.Spans[i].GetSpan(snapshot);
                    string current = snapshot.GetText(span);
                    if (current != newText)
                        edit.Replace(span, newText);
                }
                edit.Apply();
            }
        }
        catch (Exception ex)
        {
            SqlCompletionSource.DebugLog($"[Session] Sync error: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void OnViewClosed(object sender, EventArgs e)
    {
        Cancel();
    }
}
