using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SQLExtended.Tests.Settings;

/// <summary>
/// Fails when a property on <c>SQLExtendedSettings</c> is persisted and bound to a control but never read
/// by anything else — the state fifteen settings shipped in.
///
/// <para>This is a source scan, not a behaviour test, because that is the shape of the defect. A dead
/// setting compiles, round-trips to JSON, loads its checkbox and saves it again; the only thing it does not
/// do is anything at all, and no unit test of the feature it is supposed to control will notice, because
/// there is no call to mock. What a reader sees is a switch that does nothing, which is indistinguishable
/// from the feature behind it being broken — and worse, it is reached for first when something misbehaves,
/// so it does not merely fail to help, it actively misleads the person diagnosing.</para>
///
/// <para>Being a source scan it can only prove a <i>mention</i>, not a use. That is the right strength here:
/// it catches the failure that actually happened (no mention anywhere) and cannot produce a false alarm on
/// a setting that is read in some way this test did not anticipate.</para>
/// </summary>
public class SettingsAreReadTests
{
    /// <summary>The two files that make a setting exist. A mention in either proves nothing.</summary>
    private static readonly string[] DeclarationFiles =
    {
        "SQLExtendedSettings.cs",
        "SQLExtendedSettingsDialog.xaml.cs",
    };

    [Fact]
    public void Every_setting_is_read_outside_the_settings_layer()
    {
        string root = FindRepoRoot();
        string settingsFile = Path.Combine(root, "SQLExtended", "Settings", "SQLExtendedSettings.cs");
        Assert.True(File.Exists(settingsFile), $"Settings file not found at {settingsFile}");

        var properties = ReadPropertyNames(File.ReadAllText(settingsFile));
        Assert.True(properties.Count > 50, $"Only found {properties.Count} settings — the property regex has stopped matching.");

        string corpus = string.Join("\n", SourceFiles(root)
            .Where(f => !DeclarationFiles.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            .Select(File.ReadAllText));

        var dead = properties
            .Where(name => !Regex.IsMatch(corpus, $@"\b{Regex.Escape(name)}\b"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(dead.Count == 0,
            "These settings are persisted and bound to a control but never read anywhere else — " +
            "the dialog shows a switch that does nothing: " + string.Join(", ", dead));
    }

    /// <summary>
    /// Public instance properties with a getter — the ones that round-trip to the settings file.
    /// Expression-bodied members (<c>Current</c>, <c>Defaults</c>) are not settings and do not match.
    /// </summary>
    private static List<string> ReadPropertyNames(string source) => Regex
        .Matches(source, @"^\s+public\s+[\w<>?\[\]\.]+\s+(?<name>\w+)\s*\{\s*get", RegexOptions.Multiline)
        .Cast<Match>()
        .Select(m => m.Groups["name"].Value)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private static IEnumerable<string> SourceFiles(string root) => Directory
        .EnumerateFiles(Path.Combine(root, "SQLExtended"), "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                    !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Walks up from the test assembly to the directory holding <c>SQLExtended.slnx</c>. Throws rather than
    /// skipping when it cannot: a guard that quietly passes when it cannot find what it guards is the same
    /// silence it exists to catch.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SQLExtended.slnx")))
            dir = dir.Parent;

        if (dir == null)
            throw new InvalidOperationException($"SQLExtended.slnx not found above {AppContext.BaseDirectory}");

        return dir.FullName;
    }
}
