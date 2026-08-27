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
    private static readonly Brush Good = Freeze(Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly Brush Warn = Freeze(Color.FromRgb(0xD7, 0xBA, 0x7D));
    private static readonly Brush Bad = Freeze(Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly Brush Running = Freeze(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly Brush Unknown = Freeze(Color.FromRgb(0x80, 0x80, 0x80));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

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
