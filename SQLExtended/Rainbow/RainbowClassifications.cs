using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace SQLExtended.Rainbow;

/// <summary>
/// The classification type names the rainbow tagger emits, and the colours SSMS starts them at.
///
/// <para>Every format definition is <see cref="UserVisibleAttribute"/>, so all eight appear under
/// <b>Tools → Options → Environment → Fonts and Colors</b> and the user can recolour them there —
/// which is why this feature ships no colour picker of its own.</para>
///
/// <para><b>The <see cref="OrderAttribute"/> is load-bearing.</b> SSMS classifies editor punctuation
/// itself, and without ordering after <see cref="Priority.High"/> the built-in colour can win: the
/// extension then loads, composes and scans correctly while nothing on screen changes colour, with
/// nothing anywhere saying why.</para>
/// </summary>
internal static class RainbowClassifications
{
    public const string Prefix = "SQLExtended.Rainbow.";

    public const string Level1 = Prefix + "Level1";
    public const string Level2 = Prefix + "Level2";
    public const string Level3 = Prefix + "Level3";
    public const string Level4 = Prefix + "Level4";
    public const string Level5 = Prefix + "Level5";
    public const string Level6 = Prefix + "Level6";
    public const string Level7 = Prefix + "Level7";
    public const string Unmatched = Prefix + "Unmatched";

    /// <summary>Level names by palette index. Length is <see cref="RainbowPairScanner.MaxSupportedLevels"/>.</summary>
    public static readonly string[] LevelNames = [Level1, Level2, Level3, Level4, Level5, Level6, Level7];

#pragma warning disable CS0649 // fields are never assigned — MEF exports them by attribute, which is the documented pattern
    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Level1)]
    internal static ClassificationTypeDefinition Level1Type;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Level2)]
    internal static ClassificationTypeDefinition Level2Type;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Level3)]
    internal static ClassificationTypeDefinition Level3Type;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Level4)]
    internal static ClassificationTypeDefinition Level4Type;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Level5)]
    internal static ClassificationTypeDefinition Level5Type;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Level6)]
    internal static ClassificationTypeDefinition Level6Type;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Level7)]
    internal static ClassificationTypeDefinition Level7Type;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Unmatched)]
    internal static ClassificationTypeDefinition UnmatchedType;
#pragma warning restore CS0649
}

/// <summary>
/// Base for the eight formats. Mid-tone foregrounds only: one definition has to read on both the dark
/// editor theme (the SSMS default here) and the light one, so nothing is picked near either end of the
/// range, and <b>no background is set</b> — a background would fight the selection and the current-line
/// highlight at every nesting level.
/// </summary>
internal abstract class RainbowFormatDefinition : ClassificationFormatDefinition
{
    protected RainbowFormatDefinition(string displayName, Color foreground)
    {
        DisplayName = displayName;
        ForegroundColor = foreground;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Level1)]
[Name(RainbowClassifications.Level1)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowLevel1Format() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis 1", Color.FromRgb(0xD8, 0xA4, 0x00));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Level2)]
[Name(RainbowClassifications.Level2)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowLevel2Format() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis 2", Color.FromRgb(0xB6, 0x75, 0xE0));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Level3)]
[Name(RainbowClassifications.Level3)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowLevel3Format() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis 3", Color.FromRgb(0x4F, 0xA6, 0xE8));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Level4)]
[Name(RainbowClassifications.Level4)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowLevel4Format() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis 4", Color.FromRgb(0x3F, 0xA4, 0x6A));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Level5)]
[Name(RainbowClassifications.Level5)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowLevel5Format() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis 5", Color.FromRgb(0xE8, 0x73, 0x5A));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Level6)]
[Name(RainbowClassifications.Level6)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowLevel6Format() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis 6", Color.FromRgb(0x2E, 0xA9, 0xA3));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Level7)]
[Name(RainbowClassifications.Level7)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowLevel7Format() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis 7", Color.FromRgb(0xE0, 0x70, 0xB0));

/// <summary>
/// The half-typed state. A script gains and loses unmatched parentheses on almost every keystroke, so
/// this is deliberately quiet — a foreground tint and nothing else. Anything louder (a background, a
/// squiggle) makes ordinary typing flash.
/// </summary>
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = RainbowClassifications.Unmatched)]
[Name(RainbowClassifications.Unmatched)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class RainbowUnmatchedFormat() : RainbowFormatDefinition("SQLExtended Rainbow Parenthesis (unmatched)", Color.FromRgb(0xE0, 0x52, 0x52));
