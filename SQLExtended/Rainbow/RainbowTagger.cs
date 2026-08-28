using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using SQLExtended.Diagnostics;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SQLExtended.Rainbow;

/// <summary>
/// Colours the parentheses in one SQL view by nesting depth.
///
/// <para>The depth itself comes from <see cref="RainbowPairScanner"/>; everything here is about doing
/// that work often enough to look live and rarely enough to stay out of the way:</para>
/// <list type="bullet">
/// <item><b>Debounced.</b> Re-lexing on every keystroke is what makes a tagger feel slow, and typing is
/// the only thing that invalidates this one.</item>
/// <item><b>Off the UI thread.</b> The lex is the expensive half and never runs on it.</item>
/// <item><b>Bounded.</b> A generated script pasted into a query window is a real case, and past a size
/// there is no colouring worth the pause.</item>
/// </list>
///
/// <para>Settings are read on the UI thread only — <c>SQLExtendedSettings.Current</c> must not be faulted
/// in from a worker, the rule <c>SQLExtendedLog</c> and <c>PerfRecentDumpDays</c> already follow.</para>
/// </summary>
internal sealed class RainbowTagger : ITagger<ClassificationTag>, IDisposable
{
    /// <summary>Long enough that a burst of typing produces one scan; short enough that a pause colours immediately.</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Above this the feature switches itself off for the buffer. Chosen so an ordinary script — even a
    /// long one — is never affected, while a multi-megabyte generated script cannot stall the editor.
    /// </summary>
    private const int MaxBufferChars = 1_000_000;

    private readonly ITextView _view;
    private readonly ITextBuffer _buffer;
    private readonly DispatcherTimer _timer;

    /// <summary>Indexed by palette slot; <see cref="_unmatchedTag"/> is deliberately not one of them.</summary>
    private readonly ClassificationTag[] _levelTags;
    private readonly ClassificationTag _unmatchedTag;

    /// <summary>The last completed scan, and the snapshot its offsets are measured against. Written on the UI thread only.</summary>
    private IReadOnlyList<RainbowPair> _pairs = [];
    private ITextSnapshot _pairsSnapshot;

    /// <summary>Buffer version of the most recent scan <em>started</em>, so a slow scan cannot overwrite a newer one.</summary>
    private int _latestRequestedVersion = -1;

    /// <summary>Sticky once the buffer has been seen over <see cref="MaxBufferChars"/>: reported once, then silent.</summary>
    private bool _tooLarge;

    /// <summary>Settings, copied here on the UI thread so <see cref="GetTags"/> never reads them.</summary>
    private bool _enabled;
    private int _levels;
    private bool _highlightUnmatched;
    private bool _includeBlocks;

    private bool _disposed;

    public RainbowTagger(ITextView view, ITextBuffer buffer, IClassificationTypeRegistryService registry)
    {
        _view = view;
        _buffer = buffer;

        // A missing classification type means the format definitions did not compose, which otherwise
        // surfaces as an ArgumentNullException from deep inside ClassificationTag naming nothing useful.
        _levelTags = new ClassificationTag[RainbowClassifications.LevelNames.Length];
        for (int i = 0; i < _levelTags.Length; i++)
            _levelTags[i] = new ClassificationTag(Require(registry, RainbowClassifications.LevelNames[i]));

        _unmatchedTag = new ClassificationTag(Require(registry, RainbowClassifications.Unmatched));

        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = DebounceInterval };
        _timer.Tick += OnDebounceElapsed;

        ReadSettings();

        _buffer.Changed += OnBufferChanged;
        _view.Closed += OnViewClosed;
        SQLExtendedSettings.Changed += OnSettingsChanged;

        // The first scan is scheduled rather than run inline: CreateTagger is called while the view is
        // still being built, and the editor asks for tags immediately afterwards regardless.
        Schedule();
    }

    private static IClassificationType Require(IClassificationTypeRegistryService registry, string name) =>
        registry.GetClassificationType(name) ?? throw new InvalidOperationException($"Classification type '{name}' is not registered.");

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    // --- settings ---

    /// <summary>Copies the settings this tagger reacts to. UI thread only — the rule the session log follows.</summary>
    private void ReadSettings()
    {
        var settings = SQLExtendedSettings.Current;

        _enabled = settings.RainbowParensEnabled;
        _levels = settings.RainbowParensLevels;
        _highlightUnmatched = settings.RainbowParensHighlightUnmatched;
        _includeBlocks = settings.RainbowParensIncludeBlocks;
    }

    private void OnSettingsChanged(object sender, EventArgs e)
    {
        if (_disposed) return;

        // Save() raises this on whichever thread called it, and everything below touches view state.
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed) return;

                bool wasEnabled = _enabled;
                bool hadBlocks = _includeBlocks;
                ReadSettings();

                if (!_enabled)
                {
                    // Drop the cached scan as well as the tags: leaving it would repaint the old
                    // colours the moment anything else raised TagsChanged.
                    Apply([], _buffer.CurrentSnapshot);
                    return;
                }

                // Re-tagging is enough for a palette-size or unmatched change — GetTags recolours from
                // the cached scan. Coming back from off has nothing cached to recolour, and turning
                // blocks on or off changes what the scan itself collects, so both need a fresh one.
                if (wasEnabled && hadBlocks == _includeBlocks && _pairsSnapshot != null)
                    Apply(_pairs, _pairsSnapshot);
                else
                    Schedule();
            }
            catch (Exception ex)
            {
                SQLExtendedLog.Error("Rainbow", "Rainbow parentheses could not apply changed settings.", ex);
            }
        });
    }

    // --- scheduling ---

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e) => Schedule();

    private void Schedule()
    {
        if (_disposed || !_enabled) return;

        // Restarting rather than letting it run means a continuous typist gets one scan when they stop.
        _timer.Stop();
        _timer.Start();
    }

    private void OnDebounceElapsed(object sender, EventArgs e)
    {
        _timer.Stop();
        if (_disposed) return;

        try
        {
            StartScan();
        }
        catch (Exception ex)
        {
            SQLExtendedLog.Error("Rainbow", "Rainbow parentheses scan could not be started.", ex);
        }
    }

    private void StartScan()
    {
        var snapshot = _buffer.CurrentSnapshot;

        if (snapshot.Length > MaxBufferChars)
        {
            if (_tooLarge) return;

            _tooLarge = true;
            SQLExtendedLog.Info("Rainbow", $"Rainbow parentheses off for this window: {snapshot.Length:N0} characters exceeds the {MaxBufferChars:N0} limit.");
            Apply([], snapshot);
            return;
        }

        _tooLarge = false;
        _latestRequestedVersion = snapshot.Version.VersionNumber;

        string text = snapshot.GetText();
        bool includeBlocks = _includeBlocks;

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                var pairs = await Task.Run(() => RainbowPairScanner.Scan(text, includeBlocks)).ConfigureAwait(true);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (_disposed) return;

                // A scan that started before one that has already landed is stale — its offsets are
                // older than what is on screen, and applying it would flicker the colours backwards.
                if (snapshot.Version.VersionNumber < _latestRequestedVersion && _pairsSnapshot != null)
                    return;

                Apply(pairs, snapshot);
            }
            catch (Exception ex)
            {
                SQLExtendedLog.Error("Rainbow", "Rainbow parentheses scan failed.", ex);
            }
        });
    }

    /// <summary>Publishes a completed scan and asks the editor to re-tag. UI thread only.</summary>
    private void Apply(IReadOnlyList<RainbowPair> pairs, ITextSnapshot snapshot)
    {
        _pairs = pairs;
        _pairsSnapshot = snapshot;

        var current = _buffer.CurrentSnapshot;
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(current, 0, current.Length)));
    }

    // --- tagging ---

    public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (_disposed || !_enabled || spans == null || spans.Count == 0 || _pairs.Count == 0 || _pairsSnapshot == null)
            yield break;

        var target = spans[0].Snapshot;

        foreach (var pair in _pairs)
        {
            // The scan is always at least one edit behind the keystroke that triggered it, so its
            // offsets are translated rather than trusted. Without this, tags land a character out
            // for the whole debounce window — visible, and wrong in a way that reads as a bug in
            // the depth logic rather than in the timing.
            if (pair.End > _pairsSnapshot.Length)
                continue;

            var span = new SnapshotSpan(_pairsSnapshot, pair.Start, pair.Length);
            if (_pairsSnapshot != target)
                span = span.TranslateTo(target, SpanTrackingMode.EdgeExclusive);

            if (span.Length == 0 || !spans.IntersectsWith(span))
                continue;

            if (!pair.IsMatched && !_highlightUnmatched)
                continue;

            var tag = pair.IsMatched ? _levelTags[RainbowPairScanner.ColorIndex(pair.Depth, _levels)] : _unmatchedTag;
            yield return new TagSpan<ClassificationTag>(span, tag);
        }
    }

    // --- teardown ---

    private void OnViewClosed(object sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= OnDebounceElapsed;
        _buffer.Changed -= OnBufferChanged;
        _view.Closed -= OnViewClosed;
        SQLExtendedSettings.Changed -= OnSettingsChanged;

        _pairs = [];
        _pairsSnapshot = null;
    }
}
