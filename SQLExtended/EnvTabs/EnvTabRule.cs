using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SQLExtended.EnvTabs;

/// <summary>How a rule's server/database patterns are interpreted.</summary>
public enum EnvTabMatchMode
{
    /// <summary>Shell-style wildcards: <c>*</c> for any run, <c>?</c> for one character. The default.</summary>
    Wildcard,

    /// <summary>The pattern is a .NET regular expression, applied case-insensitively.</summary>
    Regex,
}

/// <summary>What an auto-created rule keys on when the user connects somewhere unmapped.</summary>
public enum EnvTabGrouping
{
    /// <summary>One rule (one colour, one caption) per server, whatever database is selected.</summary>
    Server,

    /// <summary>One rule per server + database pair.</summary>
    ServerAndDatabase,
}

/// <summary>
/// One environment rule: "connections matching this server/database look like <i>this</i>".
///
/// Deliberately free of VS, WPF and SqlClient types so the test project can link it — the matching below
/// is the piece where a mistake is silent and expensive. A rule that fails to match costs a missing
/// colour, which is noticed; a rule that matches <i>too much</i> paints production in the development
/// colour, which is the failure this whole feature exists to prevent.
/// </summary>
public sealed class EnvTabRule
{
    /// <summary>Unchecked rules stay in the list but never match. Used to park a rule without losing it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Matched against the connection's server. Empty means "any server".</summary>
    public string ServerPattern { get; set; } = "";

    /// <summary>Matched against the connection's database. Empty means "any database".</summary>
    public string DatabasePattern { get; set; } = "";

    public EnvTabMatchMode MatchMode { get; set; } = EnvTabMatchMode.Wildcard;

    /// <summary>Short environment name shown on the tab, e.g. "Prod".</summary>
    public string Label { get; set; } = "";

    /// <summary>Index into <see cref="EnvTabPalette"/>, or <see cref="EnvTabPalette.NoColor"/>.</summary>
    public int ColorIndex { get; set; } = EnvTabPalette.NoColor;

    /// <summary>
    /// Set on rules the auto-prompt created, so the settings UI can show which ones the user chose
    /// deliberately and the prompt can avoid re-offering something already declined.
    /// </summary>
    public bool AutoCreated { get; set; }

    /// <summary>
    /// Stable identity for the rule, independent of its position in the list. This is what the
    /// colour and caption state is keyed by, so reordering rules in the editor does not renumber
    /// every open tab.
    /// </summary>
    public string Key => $"{MatchMode}|{ServerPattern}|{DatabasePattern}";

    public bool Matches(string server, string database)
    {
        if (!Enabled) return false;
        return PatternMatches(ServerPattern, server) && PatternMatches(DatabasePattern, database);
    }

    private bool PatternMatches(string pattern, string value)
    {
        // An empty pattern is "any", including "the caller could not determine this". A rule that names
        // a pattern, though, must not match an unknown value — that is how a Prod rule would claim a
        // connection we failed to read.
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        if (value == null) return false;

        try
        {
            var rx = Compile(pattern, MatchMode);
            return rx != null && rx.IsMatch(value);
        }
        catch
        {
            // A malformed user regex must never throw into a tab-update path.
            return false;
        }
    }

    private static readonly Dictionary<string, Regex> Cache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Compiles a pattern to a whole-string, case-insensitive regex. Cached because this runs on every
    /// tab update for every rule until one matches.
    /// </summary>
    internal static Regex Compile(string pattern, EnvTabMatchMode mode)
    {
        string cacheKey = (int)mode + "|" + pattern;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

            Regex compiled = null;
            try
            {
                string body = mode == EnvTabMatchMode.Regex ? pattern : WildcardToRegex(pattern);
                compiled = new Regex("^(?:" + body + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                // Bad user regex — cache the null so we don't retry the parse every poll.
            }

            Cache[cacheKey] = compiled;
            return compiled;
        }
    }

    /// <summary>Translates <c>*</c>/<c>?</c> wildcards to regex, escaping everything else.</summary>
    internal static string WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder(pattern.Length * 2);
        foreach (char c in pattern)
        {
            if (c == '*') sb.Append(".*");
            else if (c == '?') sb.Append('.');
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.ToString();
    }

    /// <summary>Clears the compiled-pattern cache. Called when the rule set is edited.</summary>
    internal static void ClearCache()
    {
        lock (CacheLock) Cache.Clear();
    }

    public EnvTabRule Clone() => new()
    {
        Enabled = Enabled,
        ServerPattern = ServerPattern,
        DatabasePattern = DatabasePattern,
        MatchMode = MatchMode,
        Label = Label,
        ColorIndex = ColorIndex,
        AutoCreated = AutoCreated,
    };
}
