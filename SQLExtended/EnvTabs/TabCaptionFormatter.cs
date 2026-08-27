using System;
using System.Text;

namespace SQLExtended.EnvTabs;

/// <summary>
/// Turns a matched rule plus a connection into the text shown on a document tab.
///
/// Pure and linked by the test project. The awkward part is not the substitution, it is that
/// <see cref="Strip"/> has to be the exact inverse of <see cref="Format"/> for the captions we produce:
/// the service re-derives a caption every time a tab's connection changes, and if it cannot recognise
/// its own previous output it appends to it instead of replacing it, giving "1. Prod — 1. Prod — 1. QA".
/// That was the first thing to break when this was written by hand.
/// </summary>
internal static class TabCaptionFormatter
{
    /// <summary>Default template — matches the shape EnvTabs uses, e.g. "1. Prod".</summary>
    public const string DefaultTemplate = "{n}. {label}";

    /// <summary>
    /// Separator placed between our prefix and the document's own name. Chosen as an en dash with spaces
    /// because it does not occur in SQL Server object names or in SSMS's generated "SQLQuery3.sql"
    /// captions, so <see cref="Strip"/> can find it unambiguously.
    /// </summary>
    public const string Separator = " — ";

    /// <summary>
    /// Builds the caption. <paramref name="sequence"/> is the per-group tab number; pass 0 to omit it.
    /// </summary>
    public static string Format(string template, string label, string server, string database, int sequence, string originalCaption)
    {
        string prefix = Substitute(string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template, label, server, database, sequence).Trim();
        string baseName = Strip(originalCaption);

        if (prefix.Length == 0) return baseName;
        if (string.IsNullOrEmpty(baseName)) return prefix;
        return prefix + Separator + baseName;
    }

    private static string Substitute(string template, string label, string server, string database, int sequence)
    {
        var sb = new StringBuilder(template);
        sb.Replace("{label}", label ?? "");
        sb.Replace("{server}", server ?? "");
        sb.Replace("{database}", database ?? "");
        sb.Replace("{n}", sequence > 0 ? sequence.ToString() : "");

        string text = sb.ToString();

        // With {n} omitted the default template leaves a leading ". " behind. Tidy the common shapes
        // rather than forbidding the combination.
        text = text.Trim();
        while (text.StartsWith(".") || text.StartsWith("-") || text.StartsWith(":"))
            text = text.Substring(1).TrimStart();

        return text;
    }

    /// <summary>
    /// Removes a prefix this class previously added, returning the document's own caption. Anything we
    /// did not add is returned untouched — a caption the user set by hand, or one another extension owns,
    /// is not ours to rewrite.
    /// </summary>
    public static string Strip(string caption)
    {
        if (string.IsNullOrEmpty(caption)) return caption ?? "";

        int at = caption.IndexOf(Separator, StringComparison.Ordinal);
        if (at < 0) return caption;

        // Only the last separator matters: a caption we built from an already-prefixed one would nest,
        // and the document's real name is whatever follows the final separator.
        int last = caption.LastIndexOf(Separator, StringComparison.Ordinal);
        return caption.Substring(last + Separator.Length);
    }

    /// <summary>True when this caption already carries a prefix we added.</summary>
    public static bool HasPrefix(string caption) =>
        !string.IsNullOrEmpty(caption) && caption.IndexOf(Separator, StringComparison.Ordinal) >= 0;
}
