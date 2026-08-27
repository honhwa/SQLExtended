using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SQLExtended.Monitoring.Replication;

/// <summary>
/// Colours a replication agent's run status. Replication has its own vocabulary — Idle is healthy here, and an
/// idle agent is what a working continuous subscription looks like most of the time — so the shared
/// <c>HealthBrushConverter</c>, which knows the HADR <c>*_desc</c> strings, cannot be reused.
/// </summary>
internal sealed class ReplStatusBrushConverter : IValueConverter
{
    private static readonly Brush Good = Freeze(Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly Brush Active = Freeze(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly Brush Warn = Freeze(Color.FromRgb(0xD7, 0xBA, 0x7D));
    private static readonly Brush Bad = Freeze(Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly Brush Unknown = Freeze(Color.FromRgb(0x80, 0x80, 0x80));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!(value is ReplRunStatus status)) return Unknown;

        switch (status)
        {
            // Idle and Succeeded are both the steady state of a working agent: a continuous distribution agent
            // sits Idle between batches, and a scheduled one reports Succeeded after each run.
            case ReplRunStatus.Idle:
            case ReplRunStatus.Succeeded:
                return Good;

            case ReplRunStatus.InProgress:
            case ReplRunStatus.Starting:
                return Active;

            case ReplRunStatus.Retrying:
                return Warn;

            case ReplRunStatus.Failed:
                return Bad;

            default:
                return Unknown;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Colours a subscription's status. Only three values, and only one of them is a problem — Inactive means the
/// subscription has expired and needs reinitializing, which is the most consequential word on the tab.
/// </summary>
internal sealed class ReplSubscriptionStatusBrushConverter : IValueConverter
{
    private static readonly Brush Good = Freeze(Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly Brush Bad = Freeze(Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly Brush Unknown = Freeze(Color.FromRgb(0x80, 0x80, 0x80));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value?.ToString();
        if (string.IsNullOrWhiteSpace(status)) return Unknown;

        if (string.Equals(status, "Inactive", StringComparison.OrdinalIgnoreCase)) return Bad;
        if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) return Good;
        return Unknown;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Colours a diagnostic finding's severity. Same triad as the rest of the monitoring windows.</summary>
internal sealed class ReplSeverityBrushConverter : IValueConverter
{
    private static readonly Brush Critical = Freeze(Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly Brush Warning = Freeze(Color.FromRgb(0xD7, 0xBA, 0x7D));
    private static readonly Brush Information = Freeze(Color.FromRgb(0x4E, 0xC9, 0xB0));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ReplIssueSeverity severity)
        {
            switch (severity)
            {
                case ReplIssueSeverity.Critical: return Critical;
                case ReplIssueSeverity.Warning: return Warning;
                default: return Information;
            }
        }

        return Information;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Formats an hour count as a duration. Distribution retention and "time since last activity" are both in hours,
/// and a bare "37.4" in a column headed Retention is not something anyone should have to interpret.
/// </summary>
internal sealed class ReplHoursConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";

        double hours = System.Convert.ToDouble(value, culture);
        if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0) return "—";

        double seconds = hours * 3600d;
        if (seconds < 60) return "<1m";
        if (seconds < 3600) return TimeSpan.FromSeconds(seconds).ToString(@"m\m", culture);
        if (seconds < 86400) return TimeSpan.FromSeconds(seconds).ToString(@"h\h\ mm\m", culture);
        return TimeSpan.FromSeconds(seconds).ToString(@"d\d\ h\h", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// A 0–1 fraction as a percentage, for how much of the retention window a subscription has used. Values above 1
/// are kept rather than clamped: "140%" says something "100%" does not.
/// </summary>
internal sealed class ReplFractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";

        double fraction = System.Convert.ToDouble(value, culture);
        if (double.IsNaN(fraction) || double.IsInfinity(fraction) || fraction < 0) return "—";

        double percent = fraction * 100d;
        if (percent < 0.1) return "<0.1%";
        return percent.ToString(percent < 10 ? "N1" : "N0", culture) + "%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
