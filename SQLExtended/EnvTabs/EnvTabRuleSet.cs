using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.EnvTabs;

/// <summary>
/// The ordered list of environment rules, and the matching over it.
///
/// <b>First match wins, and order is the user's to control.</b> There is no specificity scoring: a scheme
/// that silently decides one pattern is "more specific" than another is unpredictable exactly when it
/// matters, and the whole point of the feature is that you can trust the colour. The rule editor shows the
/// list in evaluation order and lets it be reordered.
///
/// Pure — no VS, WPF or SqlClient — so the test project can link it.
/// </summary>
internal sealed class EnvTabRuleSet
{
    public List<EnvTabRule> Rules { get; set; } = new();

    /// <summary>The first enabled rule matching this connection, or null.</summary>
    public EnvTabRule Match(string server, string database)
    {
        if (Rules == null) return null;
        for (int i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            if (rule != null && rule.Matches(server, database)) return rule;
        }
        return null;
    }

    /// <summary>True when nothing covers this connection, i.e. the auto-prompt has something to offer.</summary>
    public bool IsUnmapped(string server, string database) => Match(server, database) == null;

    /// <summary>
    /// Builds the rule the auto-prompt proposes for an unmapped connection. Patterns are the literal
    /// server (and database, when grouping by both) rather than a guessed wildcard — a prompt that
    /// pre-fills <c>PROD*</c> off a server called <c>PRODUCTION-01</c> would quietly also claim
    /// <c>PROD-SANDBOX</c>. Widening is a deliberate edit the user can make in the dialog.
    /// </summary>
    public static EnvTabRule ProposeRule(string server, string database, EnvTabGrouping grouping, int colorIndex)
    {
        bool byDatabase = grouping == EnvTabGrouping.ServerAndDatabase && !string.IsNullOrWhiteSpace(database);
        return new EnvTabRule
        {
            Enabled = true,
            MatchMode = EnvTabMatchMode.Wildcard,
            ServerPattern = EscapeWildcards(server ?? ""),
            DatabasePattern = byDatabase ? EscapeWildcards(database) : "",
            Label = byDatabase ? database : (server ?? ""),
            ColorIndex = EnvTabPalette.Sanitize(colorIndex),
            AutoCreated = true,
        };
    }

    /// <summary>
    /// A server or database name can legally contain <c>*</c> or <c>?</c> — rare, but a name carrying one
    /// would turn a literal proposal into a wildcard that matches far more than the user was asked about.
    /// Wildcard mode has no escape character, so such a name is proposed as a regex instead by the caller;
    /// here we simply leave the characters alone and let <see cref="NeedsRegexMode"/> decide.
    /// </summary>
    private static string EscapeWildcards(string value) => value;

    /// <summary>
    /// True when a literal name cannot be expressed safely as a wildcard pattern and the proposed rule
    /// should use <see cref="EnvTabMatchMode.Regex"/> with an escaped pattern instead.
    /// </summary>
    public static bool NeedsRegexMode(string value) => !string.IsNullOrEmpty(value) && (value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0);

    /// <summary>
    /// Inserts a freshly-answered rule at the top. The user has just been asked about this exact server
    /// and database and answered, so it should beat anything already there — including a catch-all they
    /// added earlier, which is the case where appending would make the prompt appear to do nothing.
    /// </summary>
    public void AddFromPrompt(EnvTabRule rule)
    {
        if (rule == null) return;
        Rules ??= new List<EnvTabRule>();
        Rules.Insert(0, rule);
        EnvTabRule.ClearCache();
    }

    /// <summary>
    /// Picks the next colour for an auto-created rule: the lowest palette index not already spoken for,
    /// falling back to round-robin once all 16 are in use. Reusing a colour is not wrong — two
    /// environments can share one — but handing out a fresh colour first is what makes the defaults
    /// useful without any editing.
    /// </summary>
    public int NextFreeColor()
    {
        var used = new HashSet<int>((Rules ?? new List<EnvTabRule>()).Where(r => r != null).Select(r => r.ColorIndex));
        for (int i = 0; i < EnvTabPalette.Count; i++)
            if (!used.Contains(i)) return i;
        return Math.Abs((Rules?.Count ?? 0) % EnvTabPalette.Count);
    }

    public EnvTabRuleSet Clone() => new() { Rules = (Rules ?? new List<EnvTabRule>()).Where(r => r != null).Select(r => r.Clone()).ToList() };
}
