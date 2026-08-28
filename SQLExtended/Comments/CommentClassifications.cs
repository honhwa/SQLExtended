using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using System.Windows.Media;

namespace SQLExtended.Comments;

/// <summary>
/// The classification type names the comment tagger emits, and the colours SSMS starts them at.
///
/// <para>Every format definition is <see cref="UserVisibleAttribute"/>, so all sixteen appear under
/// <b>Tools → Options → Environment → Fonts and Colors</b> and can be recoloured there by hand. The colours
/// below are only the <em>starting</em> values: <c>CommentThemeApplier</c> writes a whole scheme over them,
/// which is how a scheme can be switched at all — once SSMS has a stored value for a classification, that
/// value wins over anything a format definition declares.</para>
///
/// <para><b>The <see cref="OrderAttribute"/> is load-bearing, and more so here than for the parentheses.</b>
/// SSMS definitely classifies comments already, so without ordering after <see cref="Priority.High"/> the
/// built-in comment colour wins: the extension then loads, composes and scans correctly while nothing on
/// screen changes, with nothing anywhere saying why.</para>
/// </summary>
internal static class CommentClassifications
{
    public const string Prefix = "SQLExtended.Comment.";

    public const string Alert = Prefix + "Alert";
    public const string Query = Prefix + "Query";
    public const string Task = Prefix + "Task";
    public const string Highlight = Prefix + "Highlight";
    public const string BannerRule = Prefix + "BannerRule";
    public const string BannerPrefix = Prefix + "BannerPrefix";
    public const string BannerLabel = Prefix + "BannerLabel";
    public const string BannerPunctuation = Prefix + "BannerPunctuation";
    public const string BannerProse = Prefix + "BannerProse";
    public const string BannerSection = Prefix + "BannerSection";
    public const string BannerColumnHeader = Prefix + "BannerColumnHeader";
    public const string BannerDashes = Prefix + "BannerDashes";
    public const string BannerDate = Prefix + "BannerDate";
    public const string BannerAuthor = Prefix + "BannerAuthor";
    public const string BannerTicket = Prefix + "BannerTicket";
    public const string BannerDescription = Prefix + "BannerDescription";

    /// <summary>
    /// All sixteen, <b>in <see cref="CommentMarkKind"/> order</b>, so the tagger and every palette can index
    /// straight into them. A name out of order silently paints every role after it the wrong colour.
    /// </summary>
    public static readonly string[] AllNames =
    [
        Alert, Query, Task, Highlight,
        BannerRule, BannerPrefix, BannerLabel, BannerPunctuation, BannerProse, BannerSection,
        BannerColumnHeader, BannerDashes, BannerDate, BannerAuthor, BannerTicket, BannerDescription
    ];

    /// <summary>Classification name for a scanned mark.</summary>
    public static string NameOf(CommentMarkKind kind) => AllNames[(int)kind];

#pragma warning disable CS0649 // fields are never assigned — MEF exports them by attribute, which is the documented pattern
    [Export(typeof(ClassificationTypeDefinition))] [Name(Alert)] internal static ClassificationTypeDefinition AlertType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(Query)] internal static ClassificationTypeDefinition QueryType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(Task)] internal static ClassificationTypeDefinition TaskType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(Highlight)] internal static ClassificationTypeDefinition HighlightType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerRule)] internal static ClassificationTypeDefinition BannerRuleType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerPrefix)] internal static ClassificationTypeDefinition BannerPrefixType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerLabel)] internal static ClassificationTypeDefinition BannerLabelType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerPunctuation)] internal static ClassificationTypeDefinition BannerPunctuationType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerProse)] internal static ClassificationTypeDefinition BannerProseType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerSection)] internal static ClassificationTypeDefinition BannerSectionType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerColumnHeader)] internal static ClassificationTypeDefinition BannerColumnHeaderType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerDashes)] internal static ClassificationTypeDefinition BannerDashesType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerDate)] internal static ClassificationTypeDefinition BannerDateType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerAuthor)] internal static ClassificationTypeDefinition BannerAuthorType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerTicket)] internal static ClassificationTypeDefinition BannerTicketType;
    [Export(typeof(ClassificationTypeDefinition))] [Name(BannerDescription)] internal static ClassificationTypeDefinition BannerDescriptionType;
#pragma warning restore CS0649
}

/// <summary>
/// Base for the sixteen formats. <b>No background is set on any of them</b> — a tagged comment can be many
/// lines long, and a background would fight the selection and the current-line highlight down its whole
/// length. That is also why the handout's "tinted banner" scheme carries a warning rather than a fill.
/// </summary>
internal abstract class CommentFormatDefinition : ClassificationFormatDefinition
{
    protected CommentFormatDefinition(string displayName, Color foreground, bool bold = false)
    {
        DisplayName = displayName;
        ForegroundColor = foreground;
        IsBold = bold;
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.Alert)]
[Name(CommentClassifications.Alert)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentAlertFormat() : CommentFormatDefinition("SQLExtended Comment (! alert)", Color.FromRgb(0xE0, 0x52, 0x52));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.Query)]
[Name(CommentClassifications.Query)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentQueryFormat() : CommentFormatDefinition("SQLExtended Comment (? query)", Color.FromRgb(0x4F, 0xA6, 0xE8));

/// <summary>The only bold tag: a todo is the one asking to be acted on rather than read past.</summary>
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.Task)]
[Name(CommentClassifications.Task)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentTaskFormat() : CommentFormatDefinition("SQLExtended Comment (todo)", Color.FromRgb(0xD8, 0xA4, 0x00), bold: true);

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.Highlight)]
[Name(CommentClassifications.Highlight)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentHighlightFormat() : CommentFormatDefinition("SQLExtended Comment (* highlight)", Color.FromRgb(0x3F, 0xA4, 0x6A));

// --- banner roles ---

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerRule)]
[Name(CommentClassifications.BannerRule)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerRuleFormat() : CommentFormatDefinition("SQLExtended Comment (banner rule)", Color.FromRgb(0x3A, 0x3A, 0x3A));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerPrefix)]
[Name(CommentClassifications.BannerPrefix)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerPrefixFormat() : CommentFormatDefinition("SQLExtended Comment (banner prefix)", Color.FromRgb(0x3A, 0x3A, 0x3A));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerLabel)]
[Name(CommentClassifications.BannerLabel)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerLabelFormat() : CommentFormatDefinition("SQLExtended Comment (banner label)", Color.FromRgb(0xC5, 0x86, 0xC0), bold: true);

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerPunctuation)]
[Name(CommentClassifications.BannerPunctuation)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerPunctuationFormat() : CommentFormatDefinition("SQLExtended Comment (banner punctuation)", Color.FromRgb(0x6A, 0x6A, 0x6A));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerProse)]
[Name(CommentClassifications.BannerProse)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerProseFormat() : CommentFormatDefinition("SQLExtended Comment (banner prose)", Color.FromRgb(0xA8, 0xA8, 0xA8));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerSection)]
[Name(CommentClassifications.BannerSection)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerSectionFormat() : CommentFormatDefinition("SQLExtended Comment (banner section)", Color.FromRgb(0xC5, 0x86, 0xC0), bold: true);

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerColumnHeader)]
[Name(CommentClassifications.BannerColumnHeader)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerColumnHeaderFormat() : CommentFormatDefinition("SQLExtended Comment (banner column header)", Color.FromRgb(0x7A, 0x9E, 0x6E));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerDashes)]
[Name(CommentClassifications.BannerDashes)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerDashesFormat() : CommentFormatDefinition("SQLExtended Comment (banner dashes)", Color.FromRgb(0x3A, 0x3A, 0x3A));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerDate)]
[Name(CommentClassifications.BannerDate)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerDateFormat() : CommentFormatDefinition("SQLExtended Comment (banner date)", Color.FromRgb(0xB5, 0xCE, 0xA8));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerAuthor)]
[Name(CommentClassifications.BannerAuthor)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerAuthorFormat() : CommentFormatDefinition("SQLExtended Comment (banner author)", Color.FromRgb(0x4E, 0xC9, 0xB0));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerTicket)]
[Name(CommentClassifications.BannerTicket)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerTicketFormat() : CommentFormatDefinition("SQLExtended Comment (banner ticket)", Color.FromRgb(0x6A, 0x6A, 0x6A));

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CommentClassifications.BannerDescription)]
[Name(CommentClassifications.BannerDescription)]
[UserVisible(true)]
[Order(After = Priority.High)]
internal sealed class CommentBannerDescriptionFormat() : CommentFormatDefinition("SQLExtended Comment (banner description)", Color.FromRgb(0xC4, 0xA4, 0x84));
