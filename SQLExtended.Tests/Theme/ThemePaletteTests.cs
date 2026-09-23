using SQLExtended.Theme;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SQLExtended.Tests.Theme;

/// <summary>
/// A <c>{DynamicResource}</c> naming a key that does not exist is not an error in WPF: the property just stays
/// unset, and a background goes transparent or a label goes black-on-black, in one theme or both. Nothing on screen
/// says why. These tests are the only place that failure is loud.
/// </summary>
public class ThemePaletteTests
{
    private static readonly Regex XamlKey = new(@"\{DynamicResource\s+(Sqlx\w+)\}", RegexOptions.Compiled);
    private static readonly Regex CodeKey = new(@"""(Sqlx\w+)""", RegexOptions.Compiled);

    [Fact]
    public void Keys_AreUnique_AndEveryValueParses()
    {
        var keys = ThemePalette.Entries.Select(e => e.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());

        foreach (var e in ThemePalette.Entries)
        {
            Assert.Matches("^#([0-9A-F]{6}|[0-9A-F]{8})$", e.Dark);
            Assert.Matches("^#([0-9A-F]{6}|[0-9A-F]{8})$", e.Light);
        }
    }

    [Fact]
    public void EveryKeyReferenced_InXamlOrCode_IsInThePalette()
    {
        string root = FindRepoRoot();
        var missing = new List<string>();
        int seen = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "SQLExtended"), "*.*", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;

            Regex pattern = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? XamlKey
                : file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? CodeKey : null;
            if (pattern == null) continue;

            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                seen++;
                if (!ThemePalette.Contains(m.Groups[1].Value))
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        }

        Assert.True(seen > 500, $"Only {seen} palette references found — the scan has stopped matching.");
        Assert.Empty(missing);
    }

    [Fact]
    public void NoXaml_StillHardcodesAColour()
    {
        // A literal colour cannot follow the theme. Comments may still quote hex values when explaining a choice.
        string root = FindRepoRoot();
        var literal = new Regex(@"=""#[0-9A-Fa-f]{6,8}""");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "SQLExtended"), "*.xaml", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;

            string text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", "", RegexOptions.Singleline);
            foreach (Match m in literal.Matches(text))
                offenders.Add($"{Path.GetFileName(file)}: {m.Value}");
        }

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("SqlxText", "SqlxSurface")]
    [InlineData("SqlxTextChrome", "SqlxSurface")]
    [InlineData("SqlxTextChrome", "SqlxControl")]
    [InlineData("SqlxTextSubtle", "SqlxSurface")]
    [InlineData("SqlxTextMuted", "SqlxSurface")]
    [InlineData("SqlxTextMuted", "SqlxSurfaceAlt")]
    [InlineData("SqlxHeading", "SqlxSurface")]
    [InlineData("SqlxHeading", "SqlxSurfaceAlt")]
    [InlineData("SqlxSelectionText", "SqlxSelection")]
    [InlineData("SqlxOnAccent", "SqlxAccent")]
    [InlineData("SqlxTextStrong", "SqlxControlPressed")]
    [InlineData("SqlxGood", "SqlxSurface")]
    [InlineData("SqlxWarn", "SqlxSurface")]
    [InlineData("SqlxBad", "SqlxSurface")]
    [InlineData("SqlxError", "SqlxSurface")]
    [InlineData("SqlxHighlight", "SqlxBanner")]
    [InlineData("SqlxSynIdentifier", "SqlxSurface")]
    [InlineData("SqlxSynFunction", "SqlxSurface")]
    [InlineData("SqlxSynComment", "SqlxSurface")]
    [InlineData("SqlxSynNumber", "SqlxSurfaceAlt")]
    [InlineData("SqlxText", "SqlxWarnRow")]
    [InlineData("SqlxText", "SqlxBadRow")]
    public void LightText_IsReadable_OnItsSurface(string text, string surface)
    {
        // WCAG AA for body text. The dark column is what shipped before light support and is not re-litigated here.
        double ratio = ThemePalette.Contrast(ThemePalette.Hex(text, dark: false), ThemePalette.Hex(surface, dark: false));
        Assert.True(ratio >= 4.5, $"{text} on {surface} in the light theme is {ratio:F2}:1");
    }

    [Theory]
    [InlineData(0x1E, 0x1E, 0x1E, true)]
    [InlineData(0xF5, 0xF5, 0xF5, false)]
    [InlineData(0xFF, 0xFF, 0xFF, false)]
    [InlineData(0x25, 0x25, 0x26, true)]
    public void DarkDetection_SplitsTheStockBackgrounds(byte r, byte g, byte b, bool dark) => Assert.Equal(dark, ThemePalette.IsDarkBackground(r, g, b));

    [Fact]
    public void Resolve_WithNoShellColours_IsExactlyTheBuiltInVariant()
    {
        var empty = new Dictionary<string, string>();
        foreach (bool dark in new[] { true, false })
        {
            var resolved = ThemePalette.Resolve(dark, empty);
            foreach (var e in ThemePalette.Entries)
                Assert.Equal(dark ? e.Dark : e.Light, resolved[e.Key]);
        }
    }

    [Fact]
    public void Resolve_TakesTheShellsChrome_ButKeepsStatusColoursBuiltIn()
    {
        // Roughly the VS Blue theme: a light theme whose chrome is nothing like our light column.
        var shell = new Dictionary<string, string> { ["SqlxSurface"] = "#F5F5F5", ["SqlxText"] = "#1E1E1E", ["SqlxSelection"] = "#3399FF", ["SqlxSelectionText"] = "#FFFFFF" };
        var resolved = ThemePalette.Resolve(dark: false, shell);

        Assert.Equal("#F5F5F5", resolved["SqlxSurface"]);
        Assert.Equal("#3399FF", resolved["SqlxSelection"]);
        Assert.Equal(ThemePalette.Hex("SqlxGood", dark: false), resolved["SqlxGood"]);
        Assert.Equal(ThemePalette.Hex("SqlxHeading", dark: false), resolved["SqlxHeading"]);
    }

    [Fact]
    public void Resolve_DerivesShades_FromTheShellsSurfaceAndText()
    {
        // The fractions are tuned so the stock dark theme reproduces the dark column to within a step.
        var resolved = ThemePalette.Resolve(dark: true, new Dictionary<string, string> { ["SqlxSurface"] = "#1E1E1E", ["SqlxText"] = "#D4D4D4" });

        Assert.Equal("#252525", resolved["SqlxSurfaceAlt"]);
        Assert.Equal("#333333", resolved["SqlxBorderSubtle"]);
    }

    [Fact]
    public void Resolve_FallsBack_WhenTheShellsPairIsUnreadable()
    {
        // A theme whose selection text barely differs from its selection fill.
        var shell = new Dictionary<string, string> { ["SqlxSelection"] = "#3A3A3A", ["SqlxSelectionText"] = "#444444" };
        var resolved = ThemePalette.Resolve(dark: true, shell);

        Assert.True(ThemePalette.Contrast(resolved["SqlxSelectionText"], resolved["SqlxSelection"]) >= 2.5);
        Assert.Equal(ThemePalette.Hex("SqlxSelection", dark: true), resolved["SqlxSelection"]);
    }

    private static bool IsBuildOutput(string path) =>
        path.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0 ||
        path.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SQLExtended.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException($"SQLExtended.slnx not found above {AppContext.BaseDirectory}");
    }
}
