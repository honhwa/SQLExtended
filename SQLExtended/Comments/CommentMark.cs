namespace SQLExtended.Comments;

/// <summary>
/// Every role this feature colours. The first four are the comment tags, from
/// <see href="https://github.com/madskristensen/CommentsVS">CommentsVS</see>' set — its fifth, <c>//</c>
/// for a struck-out commented-out line, is deliberately absent: the strike-through was not wanted, and
/// <c>//</c> does not start a comment in T-SQL anyway. The rest are the parts of a banner header.
///
/// <para><b>The order is load-bearing.</b> <c>CommentClassifications.AllNames</c> and every palette in
/// <c>CommentThemes</c> are indexed by it, so a value inserted in the middle silently shifts every colour
/// after it onto the wrong role. Append, never insert.</para>
/// </summary>
public enum CommentMarkKind
{
    /// <summary><c>-- ! something is wrong here</c></summary>
    Alert,

    /// <summary><c>-- ? why is this a left join</c></summary>
    Query,

    /// <summary><c>-- todo: index this</c></summary>
    Task,

    /// <summary><c>-- * the interesting bit</c></summary>
    Highlight,

    /// <summary>A full-width rule of stars, the opening <c>/***</c> and closing <c>***/</c> included.</summary>
    BannerRule,

    /// <summary>The leading <c>**</c> on a content line. Its own role so the box outline can recede without taking the text with it.</summary>
    BannerPrefix,

    /// <summary>A field label before its colon — the <c>Description</c> of <c>** Description : …</c>.</summary>
    BannerLabel,

    /// <summary>The <c>:</c> separating a label from its text.</summary>
    BannerPunctuation,

    /// <summary>Free text — what follows a label, and any content line that is none of the other shapes.</summary>
    BannerProse,

    /// <summary>A standalone section heading on its own line, such as <c>Change History</c>.</summary>
    BannerSection,

    /// <summary>The <c>Date  Author  Ticket  Description</c> row above a change table.</summary>
    BannerColumnHeader,

    /// <summary>The <c>-----  -----</c> rule under a column header.</summary>
    BannerDashes,

    /// <summary>Column 1 of a change row.</summary>
    BannerDate,

    /// <summary>Column 2 of a change row.</summary>
    BannerAuthor,

    /// <summary>Column 3 of a change row. Absent on rows that skip it.</summary>
    BannerTicket,

    /// <summary>The last column of a change row — the free-text description of the change.</summary>
    BannerDescription
}

/// <summary>
/// One coloured run in a script: where it starts, how long it is, and which role it plays. A plain comment
/// produces no mark at all and keeps SSMS's own comment colour.
/// </summary>
/// <param name="start">Character offset into the script the run starts at.</param>
/// <param name="length">
/// Length of the run with trailing whitespace removed. A single-line comment's token runs to the end of the
/// line, and colouring that newline stretches the tag across the rest of the line on screen.
/// </param>
/// <param name="kind">The role.</param>
public readonly struct CommentMark(int start, int length, CommentMarkKind kind)
{
    public int Start { get; } = start;

    public int Length { get; } = length;

    public CommentMarkKind Kind { get; } = kind;

    public int End => Start + Length;

    public override string ToString() => $"{Kind}@{Start}+{Length}";
}
