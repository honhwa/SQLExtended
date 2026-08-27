using System;
using System.Collections.Generic;
using System.Linq;
using SQLExtended.EnvTabs;
using Xunit;

namespace SQLExtended.Tests.EnvTabs;

/// <summary>
/// The text written into the shell's ColorByRegexConfig.txt.
///
/// Every assertion here mirrors something the shell's own parser does, and the shell reports none of it:
/// a line it rejects, or a comment it fails to recognise, produces a file that loads cleanly and colours
/// nothing.
/// </summary>
public class EnvTabConfigTextTests
{
    private static EnvTabGroup Group(string label, int color, params string[] paths)
    {
        var group = new EnvTabGroup { Label = label, ColorIndex = color, RuleKey = label };
        group.Paths.AddRange(paths);
        return group;
    }

    [Fact]
    public void PatternMatchesTheWholePathAndNothingElse()
    {
        string pattern = ColorByRegexConfigText.BuildGroupPattern(new[] { @"C:\Temp\SQLQuery1.sql" });
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.True(regex.IsMatch(@"C:\Temp\SQLQuery1.sql"));
        Assert.False(regex.IsMatch(@"D:\Other\SQLQuery1.sql"));
        Assert.False(regex.IsMatch(@"C:\Temp\SQLQuery11.sql"));
    }

    [Fact]
    public void PathMetacharactersAreEscaped()
    {
        // A Windows path is full of backslashes; unescaped, "C:\t" is a tab and the pattern matches
        // nothing at all.
        string pattern = ColorByRegexConfigText.BuildGroupPattern(new[] { @"C:\temp\a+b(1).sql" });
        var regex = new System.Text.RegularExpressions.Regex(pattern);

        Assert.True(regex.IsMatch(@"C:\temp\a+b(1).sql"));
    }

    [Fact]
    public void EmptyGroupProducesNoPattern()
    {
        // "^(?:)$" would match the empty string and, worse, is a valid regex the shell would accept.
        Assert.Null(ColorByRegexConfigText.BuildGroupPattern(Array.Empty<string>()));
        Assert.Null(ColorByRegexConfigText.BuildGroupPattern(new[] { "  " }));
    }

    [Fact]
    public void EveryLineIsFlushLeft_BecauseTheShellDiscardsItsOwnTrim()
    {
        // The shell calls Trim() and throws the result away, so an indented "//" is not treated as a
        // comment — it is compiled as a regex — and leading whitespace becomes part of a pattern.
        string text = ColorByRegexConfigText.Merge("", new[] { Group("Prod", 3, @"C:\a.sql") });

        foreach (var line in text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0))
        {
            Assert.Equal(line.TrimStart(), line);
            Assert.Equal(line.TrimEnd(), line);
        }
    }

    [Fact]
    public void ForeignLinesArePreserved_AndTheManagedBlockComesFirst()
    {
        // The shell seeds this file itself and users add their own patterns. Ours must win (first match
        // wins) without deleting theirs.
        string existing = "// mine\r\n^.*\\.cs$\r\n";
        string text = ColorByRegexConfigText.Merge(existing, new[] { Group("Prod", 3, @"C:\a.sql") });

        Assert.Contains(@"^.*\.cs$", text);
        Assert.Contains("// mine", text);
        Assert.True(text.IndexOf(ColorByRegexConfigText.BeginMarker, StringComparison.Ordinal) <
                    text.IndexOf(@"^.*\.cs$", StringComparison.Ordinal));
    }

    [Fact]
    public void RewritingReplacesTheBlockRatherThanAppendingASecondOne()
    {
        string once = ColorByRegexConfigText.Merge("", new[] { Group("Prod", 3, @"C:\a.sql") });
        string twice = ColorByRegexConfigText.Merge(once, new[] { Group("Prod", 3, @"C:\b.sql") });

        Assert.Equal(1, CountOccurrences(twice, ColorByRegexConfigText.BeginMarker));
        Assert.Equal(1, CountOccurrences(twice, ColorByRegexConfigText.EndMarker));
        Assert.Contains(@"b\.sql", twice);
        Assert.DoesNotContain(@"a\.sql", twice);
    }

    [Fact]
    public void RewritingIsStable_SoAnUnchangedPollDoesNotRewriteTheFile()
    {
        // The store skips the write when the text is identical; that only helps if Merge is deterministic.
        var groups = new[] { Group("Prod", 3, @"C:\a.sql"), Group("Dev", 4, @"C:\b.sql") };
        string first = ColorByRegexConfigText.Merge("", groups);
        Assert.Equal(first, ColorByRegexConfigText.Merge(first, groups));
    }

    [Fact]
    public void AnOrphanedBlockWithNoEndMarkerIsDropped()
    {
        // A half-written file from a crash. Leaving the orphan would duplicate every group forever.
        string broken = ColorByRegexConfigText.BeginMarker + "\r\n^stale$\r\n";
        string text = ColorByRegexConfigText.Merge(broken, new[] { Group("Prod", 3, @"C:\a.sql") });

        Assert.DoesNotContain("^stale$", text);
        Assert.Equal(1, CountOccurrences(text, ColorByRegexConfigText.BeginMarker));
    }

    [Fact]
    public void GroupOrderIsPreserved_BecauseItIsPrecedence()
    {
        string text = ColorByRegexConfigText.Merge("", new[] { Group("First", 1, @"C:\a.sql"), Group("Second", 2, @"C:\b.sql") });

        Assert.True(text.IndexOf(@"a\.sql", StringComparison.Ordinal) < text.IndexOf(@"b\.sql", StringComparison.Ordinal));
    }

    [Fact]
    public void ALabelCannotBreakOutOfItsComment()
    {
        // A newline in a label would turn the rest of it into a line the shell compiles as a regex.
        string text = ColorByRegexConfigText.Merge("", new[] { Group("Bad\r\n^.*$", 3, @"C:\a.sql") });

        Assert.DoesNotContain("\r\n^.*$\r\n", text);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0 && !l.StartsWith("//")))
            Assert.StartsWith("^(?:", line);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }
        return count;
    }
}

/// <summary>
/// Caption formatting. <see cref="TabCaptionFormatter.Strip"/> has to be the exact inverse of
/// <see cref="TabCaptionFormatter.Format"/>, or a tab re-formatted on a later poll accumulates prefixes.
/// </summary>
public class TabCaptionFormatterTests
{
    [Fact]
    public void FormatPrefixesTheDocumentName()
    {
        string caption = TabCaptionFormatter.Format("{n}. {label}", "Prod", "SRV", "Sales", 1, "SQLQuery1.sql");
        Assert.Equal("1. Prod — SQLQuery1.sql", caption);
    }

    [Fact]
    public void ReformattingDoesNotAccumulatePrefixes()
    {
        string once = TabCaptionFormatter.Format("{n}. {label}", "Prod", "SRV", "Sales", 1, "SQLQuery1.sql");
        string twice = TabCaptionFormatter.Format("{n}. {label}", "QA", "SRV", "Sales", 2, once);

        Assert.Equal("2. QA — SQLQuery1.sql", twice);
    }

    [Fact]
    public void StripReturnsTheDocumentName_AndLeavesForeignCaptionsAlone()
    {
        Assert.Equal("SQLQuery1.sql", TabCaptionFormatter.Strip("1. Prod — SQLQuery1.sql"));
        Assert.Equal("SQLQuery1.sql", TabCaptionFormatter.Strip("SQLQuery1.sql"));
        Assert.Equal("my - report.sql", TabCaptionFormatter.Strip("my - report.sql"));
    }

    [Theory]
    [InlineData("{label}", "Prod — SQLQuery1.sql")]
    [InlineData("{server}/{database}", "SRV/Sales — SQLQuery1.sql")]
    [InlineData("{n}. {label}", "1. Prod — SQLQuery1.sql")]
    public void TokensAreSubstituted(string template, string expected) =>
        Assert.Equal(expected, TabCaptionFormatter.Format(template, "Prod", "SRV", "Sales", 1, "SQLQuery1.sql"));

    [Fact]
    public void OmittingTheSequenceDoesNotLeaveStrayPunctuation()
    {
        Assert.Equal("Prod — SQLQuery1.sql", TabCaptionFormatter.Format("{n}. {label}", "Prod", "SRV", "Sales", 0, "SQLQuery1.sql"));
    }

    [Fact]
    public void HasPrefixDistinguishesOurCaptionsFromEveryoneElses()
    {
        Assert.True(TabCaptionFormatter.HasPrefix("1. Prod — SQLQuery1.sql"));
        Assert.False(TabCaptionFormatter.HasPrefix("SQLQuery1.sql"));
    }
}
