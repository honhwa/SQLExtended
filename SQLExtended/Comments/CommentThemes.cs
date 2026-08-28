using System;
using System.Collections.Generic;

namespace SQLExtended.Comments;

/// <summary>The colour schemes this feature ships. Stored in settings by name, so the order here is free to change.</summary>
public enum CommentScheme
{
    /// <summary>Borders drop to near-background so the outline reads as texture. The most readable, and the busiest.</summary>
    StructuralFade,

    /// <summary>The block stays classic comment green; only the change rows get column tints. Lowest clash risk.</summary>
    LedgerColumns,

    /// <summary>One hue at four luminance steps. The most native-looking, and the weakest differentiation.</summary>
    MonochromeRamp,

    /// <summary>Borrows the language's own token colours. Familiar, but makes comments look executable at a glance.</summary>
    SemanticMirror,

    /// <summary>Warmer accents against a receded green box. Designed around a background fill this does not paint — see the notes.</summary>
    TintedBanner
}

/// <summary>
/// The palettes. Sixteen colours per variant, <b>in <see cref="CommentMarkKind"/> order</b>, as 0xRRGGBB.
///
/// <para>Plain <see cref="uint"/> rather than <c>System.Windows.Media.Color</c> on purpose: it keeps this
/// file free of the VS and WPF assemblies so the test project can link it and assert that every scheme
/// defines every role. <c>CommentThemeApplier</c> does the conversion.</para>
///
/// <para><b>Weight is not part of a scheme.</b> The label, section and todo roles are bold in all of them —
/// a scheme decides hue, and making weight vary too would mean a user who liked one scheme's emphasis
/// could not keep it while changing its colours. Fonts and Colors still overrides weight per entry.</para>
///
/// <para><b>No scheme paints a background</b>, including <see cref="CommentScheme.TintedBanner"/>, which was
/// designed around one. Two reasons, both about a fill several lines tall: it fights the selection and the
/// current-line highlight down the whole block, and it exposes a ragged right edge on lines of differing
/// length, which can only be hidden by padding every line to a fixed width — i.e. by editing the user's
/// script. Its foregrounds are shipped as specified; only the fill is left out.</para>
/// </summary>
public static class CommentThemes
{
    /// <summary>The four comment tags, shared by every scheme.</summary>
    /// <remarks>
    /// A scheme is about the banner. The tags mean the same thing whichever one is chosen — an alert is an
    /// alert — and re-hueing them per scheme would make <c>-- ! careful</c> change colour for a reason that
    /// has nothing to do with it.
    /// </remarks>
    private static readonly uint[] TagsDark = [0xE05252, 0x4FA6E8, 0xD8A400, 0x3FA46A];

    private static readonly uint[] TagsLight = [0xC02626, 0x1F6FB5, 0x8A6800, 0x1E7A46];

    /// <summary>Number of roles a palette must define. Equals the number of <see cref="CommentMarkKind"/> values.</summary>
    public static readonly int RoleCount = Enum.GetValues(typeof(CommentMarkKind)).Length;

    private static readonly Dictionary<CommentScheme, (uint[] Dark, uint[] Light)> Palettes = new()
    {
        // rule, prefix, label, punct, prose, section, colhead, dashes, date, author, ticket, desc
        [CommentScheme.StructuralFade] = (
            Banner(0x3A3A3A, 0x3A3A3A, 0xC586C0, 0x6A6A6A, 0xA8A8A8, 0xC586C0, 0x7A9E6E, 0x3A3A3A, 0xB5CEA8, 0x4EC9B0, 0x6A6A6A, 0xC4A484, dark: true),
            Banner(0xDDDDDD, 0xDDDDDD, 0x9B2FAE, 0x9A9A9A, 0x4A4A4A, 0x9B2FAE, 0x4E7A3C, 0xDDDDDD, 0x0B7A4B, 0x1E6E7E, 0x9A9A9A, 0x8A5A2B, dark: false)),

        [CommentScheme.LedgerColumns] = (
            Banner(0x4B6E44, 0x4B6E44, 0x6A9955, 0x4B6E44, 0x6A9955, 0x6A9955, 0x808080, 0x4B4B4B, 0x9CDCFE, 0xDCDCAA, 0x808080, 0xCE9178, dark: true),
            Banner(0x7FA97A, 0x7FA97A, 0x008000, 0x7FA97A, 0x008000, 0x008000, 0x808080, 0xC8C8C8, 0x0070C1, 0x795E26, 0x909090, 0xA31515, dark: false)),

        [CommentScheme.MonochromeRamp] = (
            Banner(0x2F4A2C, 0x2F4A2C, 0xA8D49A, 0x4E7048, 0x6A9955, 0xA8D49A, 0x4E7048, 0x2F4A2C, 0x8FBF7F, 0x8FBF7F, 0x4E7048, 0x6A9955, dark: true),
            Banner(0xC3DBBE, 0xC3DBBE, 0x14571A, 0x85AC7D, 0x2E7D32, 0x14571A, 0x85AC7D, 0xC3DBBE, 0x14571A, 0x14571A, 0x85AC7D, 0x2E7D32, dark: false)),

        [CommentScheme.SemanticMirror] = (
            Banner(0x6A6A6A, 0x6A6A6A, 0x569CD6, 0xD4D4D4, 0x6A9955, 0x569CD6, 0x9CDCFE, 0x6A6A6A, 0xB5CEA8, 0x9CDCFE, 0xCE9178, 0x6A9955, dark: true),
            Banner(0xA0A0A0, 0xA0A0A0, 0x0000FF, 0x333333, 0x008000, 0x0000FF, 0x001080, 0xA0A0A0, 0x098658, 0x001080, 0xA31515, 0x008000, dark: false)),

        [CommentScheme.TintedBanner] = (
            Banner(0x3E5C3A, 0x3E5C3A, 0xE0C878, 0x8A8A8A, 0xCFCFCF, 0xE0C878, 0x7DA8C4, 0x3E5C3A, 0xA8D49A, 0x7DA8C4, 0x8A8A8A, 0xCFCFCF, dark: true),
            Banner(0xB8CDB2, 0xB8CDB2, 0x7A5B12, 0x7A7A7A, 0x3C3C3C, 0x7A5B12, 0x1E5F82, 0xB8CDB2, 0x2E7D32, 0x1E5F82, 0x7A7A7A, 0x3C3C3C, dark: false))
    };

    /// <summary>Prepends the shared tag colours to a scheme's twelve banner colours.</summary>
    private static uint[] Banner(uint rule, uint prefix, uint label, uint punct, uint prose, uint section,
        uint columnHeader, uint dashes, uint date, uint author, uint ticket, uint description, bool dark)
    {
        var tags = dark ? TagsDark : TagsLight;

        return
        [
            tags[0], tags[1], tags[2], tags[3],
            rule, prefix, label, punct, prose, section, columnHeader, dashes, date, author, ticket, description
        ];
    }

    /// <summary>The sixteen colours for one scheme in one variant, indexed by <see cref="CommentMarkKind"/>.</summary>
    /// <param name="scheme">Which scheme. An unknown value falls back to <see cref="CommentScheme.StructuralFade"/>
    /// rather than throwing, so a hand-edited settings file cannot leave the editor uncoloured.</param>
    /// <param name="dark">True for the dark variant.</param>
    public static uint[] Palette(CommentScheme scheme, bool dark)
    {
        if (!Palettes.TryGetValue(scheme, out var pair))
            pair = Palettes[CommentScheme.StructuralFade];

        return dark ? pair.Dark : pair.Light;
    }

    /// <summary>Roles rendered bold, in every scheme. See the note on the class about why weight is not per-scheme.</summary>
    public static bool IsBold(CommentMarkKind kind) =>
        kind is CommentMarkKind.Task or CommentMarkKind.BannerLabel or CommentMarkKind.BannerSection;

    /// <summary>Every scheme, for the settings dropdown and for the completeness test.</summary>
    public static IEnumerable<CommentScheme> All => Palettes.Keys;

    /// <summary>The name shown in the settings dropdown.</summary>
    public static string DisplayName(CommentScheme scheme) => scheme switch
    {
        CommentScheme.StructuralFade => "Structural fade",
        CommentScheme.LedgerColumns => "Ledger columns",
        CommentScheme.MonochromeRamp => "Monochrome ramp",
        CommentScheme.SemanticMirror => "Semantic mirror",
        CommentScheme.TintedBanner => "Tinted banner",
        _ => scheme.ToString()
    };
}
