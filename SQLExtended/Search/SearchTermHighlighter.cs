using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace SQLExtended.Search;

/// <summary>
/// AvalonEdit line transformer that highlights all occurrences of a search term
/// with a background color in the preview editor.
/// </summary>
internal sealed class SearchTermHighlighter : DocumentColorizingTransformer
{
    private string _searchTerm;

    private static readonly SolidColorBrush HighlightBrush =
        new(Color.FromArgb(0x60, 0xFF, 0xD7, 0x00)); // semi-transparent gold

    static SearchTermHighlighter()
    {
        HighlightBrush.Freeze();
    }

    public string SearchTerm
    {
        get => _searchTerm;
        set => _searchTerm = value;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (string.IsNullOrEmpty(_searchTerm))
            return;

        string lineText = CurrentContext.Document.GetText(line);
        int startOffset = line.Offset;
        int searchLen = _searchTerm.Length;
        int index = 0;

        while ((index = lineText.IndexOf(_searchTerm, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            base.ChangeLinePart(
                startOffset + index,
                startOffset + index + searchLen,
                element => element.TextRunProperties.SetBackgroundBrush(HighlightBrush));

            index += searchLen;
        }
    }
}
