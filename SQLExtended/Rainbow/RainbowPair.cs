namespace SQLExtended.Rainbow;

/// <summary>What a <see cref="RainbowPair"/> was found on. Purely descriptive — both colour from the same palette.</summary>
public enum RainbowKind
{
    Parenthesis,

    /// <summary>A block keyword: BEGIN/END, CASE/END, and the END TRY / END CATCH forms.</summary>
    Block
}

/// <summary>
/// One paired token in a script, with the nesting depth it sits at.
/// Both halves of a pair carry the same <see cref="Depth"/>, so the two tokens colour alike.
/// </summary>
/// <param name="start">Character offset into the script the token starts at.</param>
/// <param name="length">Token length — 1 for a parenthesis, the keyword's length for a block.</param>
/// <param name="depth">Zero-based nesting depth, counted separately per <see cref="RainbowKind"/>.</param>
/// <param name="isMatched">False when the token has no partner — normal while the user is mid-typing.</param>
/// <param name="isOpen">True for the opening token of the pair.</param>
/// <param name="kind">Whether this is a parenthesis or a block keyword.</param>
public readonly struct RainbowPair(int start, int length, int depth, bool isMatched, bool isOpen, RainbowKind kind = RainbowKind.Parenthesis)
{
    public int Start { get; } = start;

    public int Length { get; } = length;

    public int Depth { get; } = depth;

    public bool IsMatched { get; } = isMatched;

    public bool IsOpen { get; } = isOpen;

    public RainbowKind Kind { get; } = kind;

    public int End => Start + Length;

    public override string ToString() => $"{Kind}{(IsOpen ? "<" : ">")}@{Start} depth={Depth}{(IsMatched ? "" : " unmatched")}";
}
