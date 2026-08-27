using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using SQLExtended.Settings;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// The Find in Results window: a term, two steps, and a line saying what was found.
///
/// <para><b>Everything is driven from here only while the window is visible.</b> Like the aggregates pane it
/// watches its own <c>IsVisibleChanged</c> — which also covers being tabbed behind another pane — and takes
/// its highlights back off the grids when hidden. A results grid left tinted with no window on screen to
/// explain it is not something the user can undo.</para>
///
/// <para><b>Typing is debounced.</b> Each keystroke would otherwise start a fresh scan of the whole result
/// set, and the ones for the prefixes of a word are all thrown away.</para>
/// </summary>
public partial class GridFindControl : UserControl, IGridFindHost
{
    /// <summary>Quiet period after typing stops before the search runs. Long enough to swallow the prefixes
    /// of a word being typed, short enough to feel like it is keeping up.</summary>
    private const int TypingDebounceMs = 300;

    /// <summary>How often to look for grids that have appeared or gone since the last check — the same
    /// polling the aggregates window does, and for the same reason: SSMS builds a fresh grid per result set
    /// on every execution and raises no event for it.</summary>
    private const int PollSeconds = 2;

    private readonly GridFindController _controller;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _pollTimer;
    private bool _loading;

    public GridFindControl()
    {
        InitializeComponent();

        _controller = new GridFindController(this);

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TypingDebounceMs) };
        _debounceTimer.Tick += (_, __) =>
        {
            _debounceTimer.Stop();
            RunSearch();
        };

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PollSeconds) };
        _pollTimer.Tick += (_, __) => SafeSync();

        LoadOptions();
        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, __) => Shutdown();
    }

    /// <summary>Puts the caret in the box and selects what is there, so re-invoking the command is the same
    /// gesture as Ctrl+F anywhere else: press it, type over the old term.</summary>
    public void FocusSearchBox()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TermBox.Focus();
            TermBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void LoadOptions()
    {
        _loading = true;
        try
        {
            var settings = SQLExtendedSettings.Current;
            MatchCaseCheck.IsChecked = settings.GridFindMatchCase;
            WholeCellCheck.IsChecked = settings.GridFindWholeCell;
            RegexCheck.IsChecked = settings.GridFindUseRegex;
            HighlightAllCheck.IsChecked = settings.GridFindHighlightAll;
            AllResultSetsCheck.IsChecked = settings.GridFindAllResultSets;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] Grid find settings load failed: {ex}");
        }
        finally
        {
            _loading = false;
        }
    }

    private GridFindOptions CurrentOptions() => new()
    {
        MatchCase = MatchCaseCheck.IsChecked == true,
        WholeCell = WholeCellCheck.IsChecked == true,
        UseRegex = RegexCheck.IsChecked == true,
        HighlightAll = HighlightAllCheck.IsChecked == true,
        AllResultSets = AllResultSetsCheck.IsChecked == true
    };

    private void SaveOptions()
    {
        try
        {
            var settings = SQLExtendedSettings.Current;
            settings.GridFindMatchCase = MatchCaseCheck.IsChecked == true;
            settings.GridFindWholeCell = WholeCellCheck.IsChecked == true;
            settings.GridFindUseRegex = RegexCheck.IsChecked == true;
            settings.GridFindHighlightAll = HighlightAllCheck.IsChecked == true;
            settings.GridFindAllResultSets = AllResultSetsCheck.IsChecked == true;
            settings.Save();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] Grid find settings save failed: {ex}");
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _pollTimer.Start();
            SafeCall(() => _controller.Resume());
        }
        else
        {
            _pollTimer.Stop();
            _debounceTimer.Stop();
            SafeCall(() => _controller.Suspend());
        }
    }

    private void Shutdown()
    {
        _pollTimer.Stop();
        _debounceTimer.Stop();
        SafeCall(() => _controller.Dispose());
    }

    private void RunSearch()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SafeCall(() => _controller.ApplyOptions(TermBox.Text, CurrentOptions()));
    }

    private void SafeSync() => SafeCall(() => _controller.SyncGrids());

    /// <summary>The window is the last thing between an unexpected failure and SSMS, so nothing from the
    /// controller is allowed out. A failure that leaves the status line stale is survivable; one that
    /// escapes into the shell's dispatcher is not.</summary>
    private void SafeCall(Action action)
    {
        try { action(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SQLExtended] Grid find failed: {ex}"); }
    }

    private void Term_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading)
            return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void Term_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Enter must act on the term as typed, not on whatever the debounce timer has got round to.
            _debounceTimer.Stop();
            RunSearch();
            SafeCall(() => _controller.Step(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Clear();
            e.Handled = true;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e) => SafeCall(() => _controller.Step(forward: true));

    private void Previous_Click(object sender, RoutedEventArgs e) => SafeCall(() => _controller.Step(forward: false));

    private void Clear_Click(object sender, RoutedEventArgs e) => Clear();

    private void Clear()
    {
        _debounceTimer.Stop();
        TermBox.Clear();
        RunSearch();
        TermBox.Focus();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        SaveOptions();
        _debounceTimer.Stop();
        RunSearch();
    }

    void IGridFindHost.ReportStatus(GridFindStatus status)
    {
        if (status == null)
            return;

        StatusText.Text = status.Text ?? string.Empty;
        StatusText.Foreground = status.IsError
            ? new SolidColorBrush(Color.FromRgb(0xF0, 0xC6, 0x74))
            : new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        NextButton.IsEnabled = status.CanStep;
        PrevButton.IsEnabled = status.CanStep;
    }
}
