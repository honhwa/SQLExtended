using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SQLExtended.EnvTabs;

/// <summary>One managed group: the rule it came from, and the document paths currently assigned to it.</summary>
internal sealed class EnvTabGroup
{
    public string RuleKey { get; set; }
    public string Label { get; set; }
    public int ColorIndex { get; set; }
    public List<string> Paths { get; } = new();
}

/// <summary>
/// Builds the text of the shell's <c>ColorByRegexConfig.txt</c>. Pure string work, linked by the test
/// project, because every rule below was read out of the shell's own parser
/// (<c>RegexFileProvider.LoadRegexInfoFromConfigurationFileAsync</c>) and getting one wrong produces a
/// file that loads without complaint and colours nothing.
///
/// What that parser actually does, line by line:
/// <list type="bullet">
/// <item>A line is skipped if it starts with <c>//</c> or is empty — so comments are safe, and that is
/// how the managed block is delimited.</item>
/// <item><b>It calls <c>Trim()</c> and throws the result away.</b> That is not a paraphrase; the shell
/// really does <c>text.Trim();</c> as a statement and then uses the untrimmed line. So leading
/// whitespace is part of the pattern, and — worse — an indented <c>//</c> comment is <i>not</i>
/// recognised as a comment and is compiled as a regex. Every line written here is therefore emitted
/// flush-left with no trailing whitespace, and the block is never indented for readability.</item>
/// <item>Patterns are compiled with <c>IgnoreCase</c>, against the document's full file path.</item>
/// <item>First matching line wins, so line order is group precedence.</item>
/// <item>The pattern text is also the group's display name in the tab tooltip, and its hash is the group
/// id. Both are why the text is generated rather than hand-written.</item>
/// </list>
/// </summary>
internal static class ColorByRegexConfigText
{
    public const string BeginMarker = "// >>> SQLExtended EnvTabs — generated, do not edit between these markers";
    public const string EndMarker = "// <<< SQLExtended EnvTabs";

    /// <summary>
    /// A regex matching exactly the given document paths and nothing else.
    ///
    /// Full paths, not file names. Two query windows always differ by path — that is how the running
    /// document table keys them — whereas an unsaved window is <c>SQLQuery1.sql</c> in every folder SSMS
    /// has ever used, so a name-only pattern would hand one server's colour to another server's tab. The
    /// cost is that this pattern is what the tab's tooltip displays; correctness wins.
    /// </summary>
    public static string BuildGroupPattern(IEnumerable<string> paths)
    {
        var escaped = (paths ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Regex.Escape)
            .ToList();

        if (escaped.Count == 0) return null;
        return "^(?:" + string.Join("|", escaped) + ")$";
    }

    /// <summary>
    /// Rewrites the managed block inside <paramref name="existing"/>, leaving every other line exactly as
    /// it was.
    ///
    /// Foreign lines are preserved because this file is not ours: the shell seeds it with <c>^.*\.cs$</c>
    /// and friends on first run, and a user may have added their own patterns before installing this. The
    /// managed block is written <b>first</b> so our rules take precedence — first match wins, and a stray
    /// <c>^.*\.sql$</c> further down would otherwise swallow every query window.
    /// </summary>
    public static string Merge(string existing, IEnumerable<EnvTabGroup> groups)
    {
        var foreign = StripManagedBlock(existing);

        var sb = new StringBuilder();
        sb.Append(BeginMarker).Append('\n');

        foreach (var group in groups ?? Enumerable.Empty<EnvTabGroup>())
        {
            string pattern = BuildGroupPattern(group?.Paths);
            if (pattern == null) continue;

            // The label goes in a comment rather than a regex comment group: it is only here so the file
            // is readable by a human, and anything inside the pattern would change the group id.
            sb.Append("// ").Append(Sanitize(group.Label)).Append(" — colour ").Append(group.ColorIndex).Append('\n');
            sb.Append(pattern).Append('\n');
        }

        sb.Append(EndMarker).Append('\n');

        if (!string.IsNullOrWhiteSpace(foreign))
            sb.Append(foreign.TrimEnd('\n')).Append('\n');

        // The shell reads with StreamReader.ReadLineAsync, which handles either ending; CRLF matches what
        // the shell itself writes when it seeds the file.
        return sb.ToString().Replace("\n", "\r\n");
    }

    /// <summary>
    /// Returns <paramref name="existing"/> with any previously-written managed block removed.
    /// Tolerates a missing end marker (a half-written file from a crash) by dropping to end of file,
    /// which is the safe reading: leaving an orphaned block would duplicate every group on the next write.
    /// </summary>
    public static string StripManagedBlock(string existing)
    {
        if (string.IsNullOrEmpty(existing)) return "";

        var lines = existing.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        bool inBlock = false;

        foreach (var line in lines)
        {
            if (!inBlock && line.StartsWith(BeginMarker, StringComparison.Ordinal)) { inBlock = true; continue; }
            if (inBlock)
            {
                if (line.StartsWith(EndMarker, StringComparison.Ordinal)) inBlock = false;
                continue;
            }
            kept.Add(line);
        }

        return string.Join("\n", kept).Trim('\n');
    }

    /// <summary>
    /// Keeps a label on one line and out of comment-marker territory, so a label can never turn a comment
    /// into something the shell tries to compile.
    /// </summary>
    private static string Sanitize(string label)
    {
        if (string.IsNullOrEmpty(label)) return "(unnamed)";
        var cleaned = new string(label.Where(c => c != '\r' && c != '\n').ToArray()).Trim();
        return cleaned.Length == 0 ? "(unnamed)" : cleaned;
    }
}
