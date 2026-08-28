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

namespace SQLExtended.Comments;

/// <summary>
/// Colours the tagged comments in one SQL view.
///
/// <para>Which comments are tagged comes from <see cref="CommentMarkScanner"/>; everything here is the same
/// debounce / off-thread / bounded shape <c>RainbowTagger</c> uses, and for the same reasons — re-lexing on
/// every keystroke is what makes a tagger feel slow, typing is the only thing that invalidates this one, and
/// a multi-megabyte generated script pasted into a query window must not stall the editor.</para>
///
/// <para>Settings are read on the UI thread only — <c>SQLExtendedSettings.Current</c> must not be faulted in
/// from a worker, the rule <c>SQLExtendedLog</c> and <c>PerfRecentDumpDays</c> already follow.</para>
/// </summary>
internal sealed class CommentTagger : ITagger<ClassificationTag>, IDisposable
{
    /// <summary>Long enough that a burst of typing produces one scan; short enough that a pause colours immediately.</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>Above this the feature switches itself off for the buffer, matching the rainbow tagger's limit.</summary>
    private const int MaxBufferChars = 1_000_000;

    private readonly ITextView _view;
    private readonly ITextBuffer _buffer;
    private readonly DispatcherTimer _timer;

    /// <summary>Indexed by <see cref="CommentMarkKind"/>.</summary>
    private readonly ClassificationTag[] _tags;

    /// <summary>The last completed scan, and the snapshot its offsets are measured against. Written on the UI thread only.</summary>
    private IReadOnlyList<CommentMark> _marks = [];
    private ITextSnapshot _marksSnapshot;

    /// <summary>Buffer version of the most recent scan <em>started</em>, so a slow scan cannot overwrite a newer one.</summary>
    private int _latestRequestedVersion = -1;

    /// <summary>Sticky once the buffer has been seen over <see cref="MaxBufferChars"/>: reported once, then silent.</summary>
    private bool _tooLarge;

    /// <summary>Copied here on the UI thread so <see cref="GetTags"/> never reads settings.</summary>
    private bool _enabled;

    private bool _disposed;

    public CommentTagger(ITextView view, ITextBuffer buffer, IClassificationTypeRegistryService registry)
    {
        _view = view;
        _buffer = buffer;

        // A missing classification type means the format definitions did not compose, which otherwise
        // surfaces as an ArgumentNullException from deep inside ClassificationTag naming nothing useful.
        _tags = new ClassificationTag[CommentClassifications.AllNames.Length];
        for (int i = 0; i < _tags.Length; i++)
            _tags[i] = new ClassificationTag(Require(registry, CommentClassifications.AllNames[i]));

        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = DebounceInterval };
        _timer.Tick += OnDebounceElapsed;

        ReadSettings();

        _buffer.Changed += OnBufferChanged;
        _view.Closed += OnViewClosed;
        SQLExtendedSettings.Changed += OnSettingsChanged;

        // Scheduled rather than run inline: CreateTagger is called while the view is still being built,
        // and the editor asks for tags immediately afterwards regardless.
        Schedule();
    }

    private static IClassificationType Require(IClassificationTypeRegistryService registry, string name) =>
        registry.GetClassificationType(name) ?? throw new InvalidOperationException($"Classification type '{name}' is not registered.");

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    // --- settings ---

    /// <summary>UI thread only — the rule the session log follows.</summary>
    private void ReadSettings() => _enabled = SQLExtendedSettings.Current.CommentTagsEnabled;

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

                ReadSettings();

                if (_enabled)
                {
                    // There is one setting and it is the on/off, so coming back on always needs a fresh
                    // scan — there is nothing cached to recolour.
                    Schedule();
                }
                else
                {
                    // Drop the cached scan as well as the tags: leaving it would repaint the old colours
                    // the moment anything else raised TagsChanged.
                    Apply([], _buffer.CurrentSnapshot);
                }
            }
            catch (Exception ex)
            {
                SQLExtendedLog.Error("Comments", "Comment tags could not apply changed settings.", ex);
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
            SQLExtendedLog.Error("Comments", "Comment tag scan could not be started.", ex);
        }
    }

    private void StartScan()
    {
        var snapshot = _buffer.CurrentSnapshot;

        if (snapshot.Length > MaxBufferChars)
        {
            if (_tooLarge) return;

            _tooLarge = true;
            SQLExtendedLog.Info("Comments", $"Comment tags off for this window: {snapshot.Length:N0} characters exceeds the {MaxBufferChars:N0} limit.");
            Apply([], snapshot);
            return;
        }

        _tooLarge = false;
        _latestRequestedVersion = snapshot.Version.VersionNumber;

        string text = snapshot.GetText();

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                var marks = await Task.Run(() => CommentMarkScanner.Scan(text)).ConfigureAwait(true);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (_disposed) return;

                // A scan that started before one that has already landed is stale — its offsets are older
                // than what is on screen, and applying it would flicker the colours backwards.
                if (snapshot.Version.VersionNumber < _latestRequestedVersion && _marksSnapshot != null)
                    return;

                Apply(marks, snapshot);
            }
            catch (Exception ex)
            {
                SQLExtendedLog.Error("Comments", "Comment tag scan failed.", ex);
            }
        });
    }

    /// <summary>Publishes a completed scan and asks the editor to re-tag. UI thread only.</summary>
    private void Apply(IReadOnlyList<CommentMark> marks, ITextSnapshot snapshot)
    {
        _marks = marks;
        _marksSnapshot = snapshot;

        var current = _buffer.CurrentSnapshot;
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(current, 0, current.Length)));
    }

    // --- tagging ---

    public IEnumerable<ITagSpan<ClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (_disposed || !_enabled || spans == null || spans.Count == 0 || _marks.Count == 0 || _marksSnapshot == null)
            yield break;

        var target = spans[0].Snapshot;

        foreach (var mark in _marks)
        {
            // The scan is always at least one edit behind the keystroke that triggered it, so its offsets
            // are translated rather than trusted. Without this, tags land a character out for the whole
            // debounce window — and a comment span is long, so the drift is obvious.
            if (mark.End > _marksSnapshot.Length)
                continue;

            var span = new SnapshotSpan(_marksSnapshot, mark.Start, mark.Length);
            if (_marksSnapshot != target)
                span = span.TranslateTo(target, SpanTrackingMode.EdgeExclusive);

            if (span.Length == 0 || !spans.IntersectsWith(span))
                continue;

            yield return new TagSpan<ClassificationTag>(span, _tags[(int)mark.Kind]);
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

        _marks = [];
        _marksSnapshot = null;
    }
}
