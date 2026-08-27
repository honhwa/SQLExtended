using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SQLExtended.Monitoring;

/// <summary>
/// A minimal inline trend line for the queue columns, drawn straight onto a DrawingContext.
///
/// Hand-rolled rather than pulled from a charting package on purpose: this extension already had one
/// painful assembly-identity collision from shipping a third-party UI library (see the ProvideBindingPath
/// comment on the package), and everything a sparkline needs is thirty lines of OnRender. It also keeps the
/// VSIX payload unchanged.
///
/// Scales to its own maximum, so each row's line shows that row's shape rather than being flattened by a
/// noisier neighbour. The peak value is printed at the right so an autoscaled line is never misread.
/// </summary>
internal sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Samples
    {
        get => (IReadOnlyList<double>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly Brush FlatBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Typeface LabelTypeface = new Typeface("Segoe UI");

    static Sparkline()
    {
        LabelBrush.Freeze();
        FlatBrush.Freeze();
    }

    /// <summary>
    /// FrameworkElement's default measure reports zero, which inside a DataGrid cell can collapse the element
    /// to nothing. Claim the space the cell offers instead, with fallbacks for the unconstrained case.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) || double.IsNaN(availableSize.Width) ? 100 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) || double.IsNaN(availableSize.Height) ? 16 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var samples = Samples;
        double width = ActualWidth, height = ActualHeight;
        if (samples == null || samples.Count == 0 || width <= 4 || height <= 2) return;

        double max = 0;
        for (int i = 0; i < samples.Count; i++)
            if (samples[i] > max) max = samples[i];

        // Reserve room for the peak label; an autoscaled line without its scale is worse than no line.
        string label = max > 0 ? FormatPeak(max) : null;
        FormattedText text = null;
        if (label != null)
        {
            text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelTypeface, 9, LabelBrush, 1.0);
            text.MaxTextWidth = Math.Max(1, width * 0.45);
        }

        double plotWidth = Math.Max(4, width - (text?.Width ?? 0) - (text != null ? 4 : 0));

        // A queue that has been flat at zero the whole window is the healthy case, and drawing it as a line
        // pinned to the baseline reads as missing data. Show a dim rule instead.
        if (max <= 0)
        {
            var pen = new Pen(FlatBrush, 1);
            dc.DrawLine(pen, new Point(0, height / 2), new Point(plotWidth, height / 2));
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double step = samples.Count > 1 ? plotWidth / (samples.Count - 1) : 0;
            double top = 1.5, bottom = height - 1.5;

            for (int i = 0; i < samples.Count; i++)
            {
                double x = samples.Count > 1 ? i * step : plotWidth / 2;
                double y = bottom - Math.Max(0, Math.Min(1, samples[i] / max)) * (bottom - top);
                var point = new Point(x, y);

                if (i == 0) ctx.BeginFigure(point, false, false);
                else ctx.LineTo(point, true, false);
            }
        }
        geometry.Freeze();

        var linePen = new Pen(LineBrush, 1.25) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, linePen, geometry);

        if (text != null)
            dc.DrawText(text, new Point(plotWidth + 4, (height - text.Height) / 2));
    }

    /// <summary>Compact peak label — these are KB values that routinely run to millions.</summary>
    private static string FormatPeak(double kb)
    {
        if (kb >= 1024d * 1024d) return (kb / (1024d * 1024d)).ToString("0.#", CultureInfo.CurrentUICulture) + "G";
        if (kb >= 1024d) return (kb / 1024d).ToString("0.#", CultureInfo.CurrentUICulture) + "M";
        return kb.ToString("0", CultureInfo.CurrentUICulture) + "K";
    }
}
