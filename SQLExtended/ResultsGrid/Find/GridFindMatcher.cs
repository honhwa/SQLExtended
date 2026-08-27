using System;
using System.Text.RegularExpressions;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// Decides whether one cell's displayed text matches what the user typed. Compiled once per search and
/// then called once per cell, so it holds the finished comparison rather than re-reading the options.
///
/// <para><b>Comparison is ordinal, not culture-sensitive.</b> This runs over hundreds of thousands of
/// cells, and a culture-sensitive <c>IndexOf</c> is both markedly slower and willing to call strings equal
/// that do not look equal on screen (combining characters, ignorable code points). A results grid is data,
/// not prose: what the user wants is the cells whose text contains those characters.</para>
///
/// <para><b>Regular expressions carry a match timeout.</b> A pathological pattern against a large grid
/// would otherwise wedge the UI thread with no way out — and it is the UI thread by necessity, since
/// <c>IGridStorage</c> cannot be read from anywhere else. A timeout is reported rather than swallowed:
/// silently treating it as "no match" would report a grid full of matches as empty.</para>
/// </summary>
internal sealed class GridFindMatcher
{
    /// <summary>Per-cell regex budget. Generous for any sane pattern, short enough that a catastrophic one
    /// is noticed rather than felt as a hang.</summary>
    private const int RegexTimeoutMs = 250;

    private readonly string _term;
    private readonly Regex _regex;
    private readonly bool _wholeCell;
    private readonly StringComparison _comparison;

    private GridFindMatcher(string term, Regex regex, bool wholeCell, StringComparison comparison)
    {
        _term = term;
        _regex = regex;
        _wholeCell = wholeCell;
        _comparison = comparison;
    }

    /// <summary>True once a regex has timed out on some cell. The search is still usable; the caller says so.</summary>
    public bool TimedOut { get; private set; }

    /// <summary>
    /// Builds a matcher, or returns null with <paramref name="error"/> set. An unparseable regex is the
    /// common case and its message is worth showing verbatim — .NET's is specific about what it disliked.
    /// </summary>
    public static GridFindMatcher Create(string term, GridFindOptions options, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(term))
        {
            error = "Type something to find.";
            return null;
        }

        options ??= new GridFindOptions();

        if (!options.UseRegex)
            return new GridFindMatcher(term, null, options.WholeCell, options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
            regexOptions |= RegexOptions.IgnoreCase;

        // Anchored with \A…\z rather than ^…$, which in .NET also match at line boundaries — a multi-line
        // cell would then satisfy "whole cell" on the strength of one of its lines.
        string pattern = options.WholeCell ? $@"\A(?:{term})\z" : term;

        try
        {
            return new GridFindMatcher(term, new Regex(pattern, regexOptions, TimeSpan.FromMilliseconds(RegexTimeoutMs)), wholeCell: false, StringComparison.Ordinal);
        }
        catch (ArgumentException ex)
        {
            error = $"Invalid regular expression: {ex.Message}";
            return null;
        }
    }

    public bool IsMatch(string cellText)
    {
        cellText ??= string.Empty;

        if (_regex != null)
        {
            try { return _regex.IsMatch(cellText); }
            catch (RegexMatchTimeoutException) { TimedOut = true; return false; }
        }

        return _wholeCell
            ? cellText.Length == _term.Length && cellText.Equals(_term, _comparison)
            : cellText.IndexOf(_term, _comparison) >= 0;
    }
}
