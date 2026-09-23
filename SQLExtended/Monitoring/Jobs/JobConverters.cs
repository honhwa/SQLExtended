using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// Colours a run outcome. Agent's vocabulary (Succeeded / Failed / Retry / Cancelled) does not overlap the
/// HADR state words <see cref="HealthBrushConverter"/> knows, so this stays with the Jobs dashboard rather
/// than being bolted onto the shared converter — but it reuses the same good / degraded / bad triad, because
/// a red that means one thing on one tab and something else on another is worse than no colour at all.
/// </summary>
internal sealed class JobOutcomeBrushConverter : IValueConverter
{
    private static Brush Good => Theme.ThemeManager.Get("SqlxGood");
    private static Brush Warn => Theme.ThemeManager.Get("SqlxWarn");
    private static Brush Bad => Theme.ThemeManager.Get("SqlxBad");
    private static Brush Running => Theme.ThemeManager.Get("SqlxHeading");
    private static Brush Unknown => Theme.ThemeManager.Get("SqlxTextMuted");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is JobRunOutcome outcome)
        {
            switch (outcome)
            {
                case JobRunOutcome.Succeeded: return Good;
                case JobRunOutcome.Failed: return Bad;
                case JobRunOutcome.Retry:
                case JobRunOutcome.Cancelled: return Warn;
                case JobRunOutcome.InProgress: return Running;
                default: return Unknown;
            }
        }

        // Also used for the Status column, which is a plain string.
        switch (value?.ToString())
        {
            case "Running": return Running;
            case "Idle": return Good;
            case "Disabled": return Unknown;
            default: return Unknown;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
