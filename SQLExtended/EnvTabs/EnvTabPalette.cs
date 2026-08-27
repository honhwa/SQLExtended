using System;

namespace SQLExtended.EnvTabs;

/// <summary>
/// The 16 accent colours the VS 2026 shell uses to tint document tabs, plus the two special indices.
///
/// These values are <b>not ours to choose</b> — they are copied from
/// <c>Microsoft.VisualStudio.PlatformUI.AccentColorPalette</c> in the shell's
/// <c>Microsoft.VisualStudio.Shell.UI.Internal.dll</c>, because the shell tints a tab by
/// <c>ColorPalette[index]</c> and never asks us for a colour. We only ever hand it an <i>index</i>, so the
/// hexes here exist purely so our own rule editor can show the user the swatch they are actually going to
/// get. If a future SSMS reorders that array, the swatches in our UI go wrong while the tabs stay right —
/// which is the harmless direction to be wrong, and the reason nothing downstream computes anything from
/// these values.
///
/// Note these differ by a digit or two from the palette published on the EnvTabs wiki (#9183EE vs #9083EF,
/// and others). The values here were read out of the SSMS 22 binary; the wiki's were not.
/// </summary>
internal static class EnvTabPalette
{
    /// <summary>No colour — the tab is left with the shell's default chrome.</summary>
    public const int NoColor = -1;

    /// <summary>Number of real colours in the shell's palette. The shell range-checks against this.</summary>
    public const int Count = 16;

    private static readonly string[] Hexes =
    {
        "#9183EE", "#D0B132", "#31B0CD", "#CE6469",
        "#6BA02B", "#BC8F6F", "#5BB2FA", "#D67540",
        "#BDBDBD", "#CACD38", "#2AA0A4", "#D957A7",
        "#6BC7A4", "#946A5B", "#6B8EC7", "#E0A2A4",
    };

    // Descriptive names only — the shell's own names come from a localised resource we can't read
    // reliably, and these are what appear in our rule editor's dropdown.
    private static readonly string[] Names =
    {
        "Lavender", "Gold", "Cyan", "Rose",
        "Green", "Tan", "Sky", "Pumpkin",
        "Grey", "Volt", "Teal", "Magenta",
        "Mint", "Brown", "Blue", "Pink",
    };

    /// <summary>True when <paramref name="index"/> is something the shell will accept.</summary>
    public static bool IsValid(int index) => index == NoColor || (index >= 0 && index < Count);

    /// <summary>
    /// Clamps an arbitrary integer into the palette. Out-of-range values become <see cref="NoColor"/>
    /// rather than wrapping: a rule pointing at a colour this SSMS doesn't have should show as uncoloured,
    /// not as a different environment's colour.
    /// </summary>
    public static int Sanitize(int index) => IsValid(index) ? index : NoColor;

    public static string HexOf(int index) => index >= 0 && index < Count ? Hexes[index] : null;

    public static string NameOf(int index) => index >= 0 && index < Count ? Names[index] : "None";

    /// <summary>Palette index, name and hex for each real colour — for binding a picker.</summary>
    public static (int Index, string Name, string Hex)[] All()
    {
        var all = new (int, string, string)[Count];
        for (int i = 0; i < Count; i++) all[i] = (i, Names[i], Hexes[i]);
        return all;
    }

    /// <summary>
    /// The colour the shell would pick for a group on its own, given the group id it derived. Used only to
    /// preview "what happens if I don't pin a colour" — we always pin one in practice.
    /// </summary>
    public static int DefaultForGroupId(int groupId) => Math.Abs(groupId % Count);
}
