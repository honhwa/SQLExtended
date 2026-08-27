using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SQLExtended.Monitoring;

/// <summary>
/// Colours any of the HADR *_desc health/state strings. One converter covers synchronization_health_desc,
/// connected_state_desc, operational_state_desc, database_state_desc and synchronization_state_desc because
/// they share a vocabulary: the good value is HEALTHY / CONNECTED / ONLINE / SYNCHRONIZED, the warning value
/// is PARTIALLY_HEALTHY / SYNCHRONIZING, and anything else is bad.
/// </summary>
internal sealed class HealthBrushConverter : IValueConverter
{
    private static readonly Brush Good = Freeze(Color.FromRgb(0x4E, 0xC9, 0xB0));
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
        string state = value?.ToString();
        if (string.IsNullOrWhiteSpace(state)) return Unknown;

        switch (state.Trim().ToUpperInvariant())
        {
            case "HEALTHY":
            case "CONNECTED":
            case "ONLINE":
            case "SYNCHRONIZED":
            case "PRIMARY":
            case "NO_FAILURE":
            case "NONE":
                return Good;

            case "PARTIALLY_HEALTHY":
            case "SYNCHRONIZING":
            case "REVERTING":
            case "INITIALIZING":
            case "RESOLVING":
            case "ONLINE_IN_PROGRESS":
                return Warn;

            case "NOT_HEALTHY":
            case "DISCONNECTED":
            case "OFFLINE":
            case "NOT_SYNCHRONIZING":
            case "FAILED":
            case "FAILED_NO_QUORUM":
            case "SUSPENDED":
            case "RECOVERY_PENDING":
            case "RESTORING":
            case "ERROR":
                return Bad;

            default:
                return Unknown;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a KB count (the unit every HADR queue and rate column uses) as KB/MB/GB.</summary>
internal sealed class KilobytesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double kb = System.Convert.ToDouble(value, culture);
        if (kb <= 0) return "0";
        if (kb >= 1024d * 1024d) return (kb / (1024d * 1024d)).ToString("N2", culture) + " GB";
        if (kb >= 1024d) return (kb / 1024d).ToString("N1", culture) + " MB";
        return kb.ToString("N0", culture) + " KB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a KB/sec rate. Zero is shown explicitly — a stalled rate is a finding, not a blank.</summary>
internal sealed class RateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double kb = System.Convert.ToDouble(value, culture);
        if (kb <= 0) return "0 KB/s";
        if (kb >= 1024d) return (kb / 1024d).ToString("N1", culture) + " MB/s";
        return kb.ToString("N0", culture) + " KB/s";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a raw byte count — used by the seeding tab, which reports bytes rather than KB.</summary>
internal sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double bytes = System.Convert.ToDouble(value, culture);
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (bytes >= 1024d && unit < units.Length - 1) { bytes /= 1024d; unit++; }
        return bytes.ToString(unit == 0 ? "N0" : "N2", culture) + " " + units[unit];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a byte/sec rate for the seeding tab.</summary>
internal sealed class ByteRateConverter : IValueConverter
{
    private static readonly BytesConverter Bytes = new BytesConverter();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        if (System.Convert.ToDouble(value, culture) <= 0) return "stalled";
        return Bytes.Convert(value, targetType, parameter, culture) + "/s";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a second count as a compact duration — the RPO/RTO estimates and seeding ETA.</summary>
internal sealed class SecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double seconds = System.Convert.ToDouble(value, culture);
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "—";
        if (seconds < 1) return "<1s";
        if (seconds < 60) return seconds.ToString("N0", culture) + "s";
        if (seconds < 3600) return TimeSpan.FromSeconds(seconds).ToString(@"m\m\ ss\s", culture);
        if (seconds < 86400) return TimeSpan.FromSeconds(seconds).ToString(@"h\h\ mm\m", culture);
        return TimeSpan.FromSeconds(seconds).ToString(@"d\d\ h\h", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Local time-of-day with date only when it is not today, so the grids stay narrow.</summary>
internal sealed class TimestampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!(value is DateTime when)) return "—";
        if (when == default) return "—";
        return when.Date == DateTime.Today ? when.ToString("HH:mm:ss", culture) : when.ToString("yyyy-MM-dd HH:mm:ss", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Renders a nullable bool as a tick, cross, or dash rather than True/False/blank.</summary>
internal sealed class BoolGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        return System.Convert.ToBoolean(value, culture) ? "✔" : "✘";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Plain thousands-separated integer, em dash for null. The default for every count column.</summary>
internal sealed class NumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        return System.Convert.ToDouble(value, culture).ToString("N0", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A 0–100 value as a percentage. Sub-0.1% collapses to "&lt;0.1%" rather than a misleading "0.0%".</summary>
internal sealed class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double percent = System.Convert.ToDouble(value, culture);
        if (percent <= 0) return "0%";
        if (percent < 0.1) return "<0.1%";
        return percent.ToString("N1", culture) + "%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// A millisecond duration. Used for both I/O latency (single-digit ms matters, so sub-millisecond values keep
/// a decimal) and accumulated wait time (where hours are common).
/// </summary>
internal sealed class MillisecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double ms = System.Convert.ToDouble(value, culture);
        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0) return "—";
        if (ms == 0) return "0";
        if (ms < 10) return ms.ToString("N1", culture) + " ms";
        if (ms < 1000) return ms.ToString("N0", culture) + " ms";
        if (ms < 60000) return (ms / 1000d).ToString("N1", culture) + " s";
        if (ms < 3600000) return TimeSpan.FromMilliseconds(ms).ToString(@"m\m\ ss\s", culture);
        if (ms < 86400000) return TimeSpan.FromMilliseconds(ms).ToString(@"h\h\ mm\m", culture);
        return TimeSpan.FromMilliseconds(ms).ToString(@"d\d\ h\h", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Milliseconds accumulated per second of wall clock — what a cumulative wait-time counter means once it has been
/// differenced. Deliberately never rescaled to seconds: "1.2 s/s" reads as a ratio and invites exactly the wrong
/// reading, whereas "1,200 ms/s" stays what it is, the wait piling up each second across all transactions.
/// </summary>
internal sealed class MillisecondRateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double ms = System.Convert.ToDouble(value, culture);
        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0) return "—";
        if (ms == 0) return "0";
        return (ms < 10 ? ms.ToString("N1", culture) : ms.ToString("N0", culture)) + " ms/s";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A per-second rate, e.g. batch requests/sec. Keeps a decimal below 10 so small rates stay legible.</summary>
internal sealed class PerSecondConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";
        double rate = System.Convert.ToDouble(value, culture);
        if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0) return "—";
        return (rate < 10 ? rate.ToString("N1", culture) : rate.ToString("N0", culture)) + "/s";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Collapses a query's whitespace onto one line for grid display. Multi-line T-SQL in a fixed-height row shows
/// only its first line otherwise, which is usually the least informative part ("SELECT" or a comment banner).
/// </summary>
internal sealed class SingleLineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return "—";

        var builder = new System.Text.StringBuilder(text.Length);
        bool lastWasSpace = false;
        foreach (char c in text)
        {
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace && lastWasSpace) continue;
            builder.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Substitutes an em dash for null or empty strings so empty cells read as deliberate.</summary>
internal sealed class DashIfEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? "—" : text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
