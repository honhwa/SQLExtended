using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// Colours a diagnostic finding's severity. Its vocabulary is this dashboard's own rather than the HADR
/// *_desc strings the shared <c>HealthBrushConverter</c> handles, so it lives with the Always On monitor.
/// The three colours are the same triad the rest of the monitoring windows use.
/// </summary>
internal sealed class AgSeverityBrushConverter : IValueConverter
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
        if (value is AgIssueSeverity severity)
        {
            switch (severity)
            {
                case AgIssueSeverity.Critical: return Critical;
                case AgIssueSeverity.Warning: return Warning;
                default: return Information;
            }
        }

        return Information;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Formats a bytes/second rate. The AG counter object reports its transport volumes in bytes, unlike the HADR
/// DMVs' KB — mixing the two units in one window is how a 1 GB/s link gets read as 1 MB/s.
/// </summary>
internal sealed class AgBytesPerSecondConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "—";

        double bytes = System.Convert.ToDouble(value, culture);
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes < 0) return "—";
        if (bytes == 0) return "0";

        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        int unit = 0;
        while (bytes >= 1024d && unit < units.Length - 1) { bytes /= 1024d; unit++; }
        return bytes.ToString(unit == 0 ? "N0" : "N1", culture) + " " + units[unit];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
