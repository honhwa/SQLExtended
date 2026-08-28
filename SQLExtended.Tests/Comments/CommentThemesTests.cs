using SQLExtended.Comments;
using System;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests.Comments;

/// <summary>
/// The palettes are data, and the one way data like this goes wrong is silently: a palette one entry short,
/// or written in a different order from the enum, paints every role after the gap with its neighbour's
/// colour and looks like a scheme someone simply designed badly.
/// </summary>
public class CommentThemesTests
{
    public static TheoryData<CommentScheme> AllSchemes
    {
        get
        {
            var data = new TheoryData<CommentScheme>();
            foreach (var scheme in CommentThemes.All)
                data.Add(scheme);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllSchemes))]
    public void EveryScheme_DefinesEveryRole_InBothVariants(CommentScheme scheme)
    {
        Assert.Equal(CommentThemes.RoleCount, CommentThemes.Palette(scheme, dark: true).Length);
        Assert.Equal(CommentThemes.RoleCount, CommentThemes.Palette(scheme, dark: false).Length);
    }

    [Fact]
    public void RoleCount_MatchesTheEnum()
    {
        // The palettes are indexed by CommentMarkKind, so a role added to the enum without a colour added to
        // each palette would throw at the end of the array rather than show up as a missing colour.
        Assert.Equal(Enum.GetValues(typeof(CommentMarkKind)).Length, CommentThemes.RoleCount);
    }

    [Fact]
    public void AllSchemesAreListed()
    {
        // The settings dropdown is built from CommentThemes.All, so a scheme with no palette would be
        // offerable and would silently fall back to another one when chosen.
        Assert.Equal(Enum.GetValues(typeof(CommentScheme)).Length, CommentThemes.All.Count());
    }

    [Theory]
    [MemberData(nameof(AllSchemes))]
    public void EveryScheme_HasADisplayName(CommentScheme scheme)
    {
        string name = CommentThemes.DisplayName(scheme);

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.NotEqual(scheme.ToString(), name);
    }

    [Theory]
    [MemberData(nameof(AllSchemes))]
    public void DarkAndLightVariants_Differ(CommentScheme scheme)
    {
        // A variant copied from the other one is the mistake this catches: it compiles, ships, and is only
        // visible to someone using the theme that got the wrong half.
        Assert.NotEqual(CommentThemes.Palette(scheme, dark: true), CommentThemes.Palette(scheme, dark: false));
    }

    [Theory]
    [MemberData(nameof(AllSchemes))]
    public void CommentTags_AreTheSameInEveryScheme(CommentScheme scheme)
    {
        // A scheme is about the banner. An alert is an alert whichever one is chosen, and re-hueing the
        // tags per scheme would make `-- ! careful` change colour for a reason that has nothing to do
        // with it.
        var reference = CommentThemes.Palette(CommentScheme.StructuralFade, dark: true);
        var palette = CommentThemes.Palette(scheme, dark: true);

        for (int i = 0; i <= (int)CommentMarkKind.Highlight; i++)
            Assert.Equal(reference[i], palette[i]);
    }

    [Fact]
    public void UnknownScheme_FallsBackRatherThanThrowing()
    {
        // A hand-edited settings file must not be able to leave the editor uncoloured.
        var palette = CommentThemes.Palette((CommentScheme)999, dark: true);

        Assert.Equal(CommentThemes.Palette(CommentScheme.StructuralFade, dark: true), palette);
    }

    [Fact]
    public void BoldRoles_AreTheEmphasisOnes()
    {
        Assert.True(CommentThemes.IsBold(CommentMarkKind.Task));
        Assert.True(CommentThemes.IsBold(CommentMarkKind.BannerLabel));
        Assert.True(CommentThemes.IsBold(CommentMarkKind.BannerSection));

        Assert.False(CommentThemes.IsBold(CommentMarkKind.BannerRule));
        Assert.False(CommentThemes.IsBold(CommentMarkKind.BannerProse));
    }
}
