using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using SQLExtended.Diagnostics;
using System;
using System.ComponentModel.Composition;

namespace SQLExtended.Rainbow;

/// <summary>
/// Attaches a <see cref="RainbowTagger"/> to every SQL view.
///
/// <para>A <b>view</b> tagger rather than a buffer tagger: one tagger per view keeps the debounce timer
/// and the cached scan on the thing that gets closed, so both die with the window.</para>
/// </summary>
[Export(typeof(IViewTaggerProvider))]
[ContentType("SQL")]
[TagType(typeof(ClassificationTag))]
[TextViewRole(PredefinedTextViewRoles.Document)]
[Name("SQLExtended Rainbow Parentheses")]
internal sealed class RainbowTaggerProvider : IViewTaggerProvider
{
    [Import]
    internal IClassificationTypeRegistryService ClassificationRegistry { get; set; }

    public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
    {
        // Only tag the view's own top-level buffer. A projection view asking on behalf of some other
        // buffer would otherwise get offsets measured against the wrong text.
        if (textView?.TextBuffer == null || buffer == null || textView.TextBuffer != buffer || ClassificationRegistry == null)
            return null;

        try
        {
            return textView.Properties.GetOrCreateSingletonProperty(() => new RainbowTagger(textView, buffer, ClassificationRegistry)) as ITagger<T>;
        }
        catch (Exception ex)
        {
            // A tagger that throws out of composition takes the editor's tag aggregator with it.
            SQLExtendedLog.Error("Rainbow", "Failed to create the rainbow parentheses tagger.", ex);
            return null;
        }
    }
}
