using System;
using System.Collections.Generic;
using System.Globalization;

namespace SQLExtended.ResultsGrid.Aggregates;

/// <summary>
/// What a column's selected values turned out to be. Decided by the values themselves, not by the SQL
/// type — the grid only ever hands us rendered text (see <see cref="GridAggregateAccumulator"/>).
/// </summary>
internal enum GridValueKind
{
    /// <summary>Nothing but NULLs and blanks — no type to infer, so no Sum/Min/Max is offered.</summary>
    Empty,
    Numeric,
    DateTime,
    Text
}

/// <summary>Aggregates for one selected column (or, for the summary row, for the whole selection).</summary>
internal sealed class GridColumnAggregate
{
    public string ColumnName { get; set; }

    /// <summary>0-based data column index, so the display can keep grid order. -1 for the summary row.</summary>
    public int Ordinal { get; set; } = -1;

    public GridValueKind Kind { get; set; }

    /// <summary>Every selected cell, including NULLs and blanks.</summary>
    public long Cells { get; set; }

    /// <summary>Cells that are not SQL NULL — the SQL <c>COUNT(col)</c> answer.</summary>
    public long NonNull { get; set; }

    public long Nulls { get; set; }

    /// <summary>Non-NULL cells whose text is empty or whitespace. Excluded from type inference.</summary>
    public long Blanks { get; set; }

    /// <summary>Distinct non-NULL values, compared as text — the SQL <c>COUNT(DISTINCT col)</c> answer.</summary>
    public long DistinctCount { get; set; }

    /// <summary>Exact sum. Set when the column is numeric and every value fit a <see cref="decimal"/>.</summary>
    public decimal? SumDecimal { get; set; }

    /// <summary>Fallback sum, set only when <see cref="SumDecimal"/> could not be (overflow, or a value
    /// outside decimal's range such as a float rendered in scientific notation).</summary>
    public double? SumDouble { get; set; }

    public decimal? AverageDecimal { get; set; }
    public double? AverageDouble { get; set; }

    /// <summary>Most decimal places seen in any parsed value, so a money column's total still shows cents.</summary>
    public int Scale { get; set; }

    /// <summary>The extreme values as the grid rendered them — never reformatted, so nothing is implied
    /// about precision that the cell did not already show.</summary>
    public string MinText { get; set; }
    public string MaxText { get; set; }

    public long TotalChars { get; set; }
    public long MaxChars { get; set; }

    /// <summary>Sum/Average were computed in floating point and may be off in the last digits.</summary>
    public bool Approximate => SumDouble.HasValue;
}

/// <summary>The whole selection: one row per column, plus a combined row when more than one is selected.</summary>
internal sealed class GridAggregateResult
{
    public List<GridColumnAggregate> Columns { get; } = new();

    /// <summary>Every selected cell treated as one set. Null when only one column is selected (it would
    /// duplicate the single row).</summary>
    public GridColumnAggregate Combined { get; set; }

    public long TotalCells { get; set; }
}

/// <summary>
/// Accumulates one column's selected cells. Fed one cell at a time so a column's row and the "all
/// selected" row can be built in a single pass over the grid rather than by buffering every value twice.
///
/// <para><b>Everything here parses rendered text, because that is all the grid has.</b>
/// <c>IGridStorage.GetCellDataAsString</c> is the only value accessor SSMS exposes — there is no typed
/// path to the underlying data — so a "numeric" column is one whose displayed values all parse as
/// numbers, not one whose SQL type is numeric. Two consequences worth knowing before trusting a figure:
/// a <c>varchar</c> column holding digits will be summed, and <c>DistinctCount</c> counts distinct
/// *renderings*, so two <c>datetime2(7)</c> values that differ below the displayed precision count once.</para>
///
/// <para><b>Sums are computed in <see cref="decimal"/> wherever they fit.</b> The columns people select
/// and total are money and decimal columns, and accumulating those in <see cref="double"/> loses cents
/// silently — the failure mode being a total that is nearly right. Floating point is used only when a
/// value cannot be a decimal at all (a <c>float</c> rendered as <c>1E+300</c>) or the running total
/// overflows, and the result then carries <see cref="GridColumnAggregate.Approximate"/> so the display
/// can say so.</para>
/// </summary>
internal sealed class GridAggregateAccumulator
{
    private long _cells, _nulls, _blanks, _totalChars, _maxChars;

    /// <summary>Non-NULL, non-blank cells — the population type inference has to explain.</summary>
    private long _values;
    private long _numericValues, _dateValues;

    private readonly HashSet<string> _distinct = new(StringComparer.Ordinal);

    private decimal _sumDec;
    private double _sumDbl;
    private bool _decimalUsable = true;
    private int _scale;

    private double _minNum, _maxNum;
    private string _minNumText, _maxNumText;
    private DateTime _minDate, _maxDate;
    private string _minDateText, _maxDateText;
    private string _minText, _maxText;

    /// <summary>Adds one selected cell. <paramref name="value"/> is null for a SQL NULL, which is what
    /// separates it from an empty string — a distinction the grid's own "NULL" rendering throws away.</summary>
    public void Add(string value)
    {
        _cells++;

        if (value == null)
        {
            _nulls++;
            return;
        }

        _distinct.Add(value);
        _totalChars += value.Length;
        if (value.Length > _maxChars)
            _maxChars = value.Length;

        if (value.Length == 0 || string.IsNullOrWhiteSpace(value))
        {
            _blanks++;
            return;
        }

        _values++;

        if (_minText == null || string.CompareOrdinal(value, _minText) < 0)
            _minText = value;
        if (_maxText == null || string.CompareOrdinal(value, _maxText) > 0)
            _maxText = value;

        if (TryParseNumber(value, out double dbl, out decimal? dec, out int scale))
        {
            _numericValues++;
            if (scale > _scale)
                _scale = scale;

            _sumDbl += dbl;
            if (_decimalUsable)
            {
                if (dec.HasValue)
                {
                    try { _sumDec += dec.Value; }
                    catch (OverflowException) { _decimalUsable = false; }
                }
                else
                {
                    _decimalUsable = false;
                }
            }

            if (_minNumText == null || dbl < _minNum) { _minNum = dbl; _minNumText = value; }
            if (_maxNumText == null || dbl > _maxNum) { _maxNum = dbl; _maxNumText = value; }
        }
        else if (TryParseDate(value, out DateTime dt))
        {
            _dateValues++;
            if (_minDateText == null || dt < _minDate) { _minDate = dt; _minDateText = value; }
            if (_maxDateText == null || dt > _maxDate) { _maxDate = dt; _maxDateText = value; }
        }
    }

    public GridColumnAggregate Build(string columnName, int ordinal)
    {
        var result = new GridColumnAggregate
        {
            ColumnName = columnName,
            Ordinal = ordinal,
            Cells = _cells,
            Nulls = _nulls,
            NonNull = _cells - _nulls,
            Blanks = _blanks,
            DistinctCount = _distinct.Count,
            TotalChars = _totalChars,
            MaxChars = _maxChars
        };

        // A column counts as numeric (or as dates) only when *every* value it holds is one. A single
        // stray value is the difference between a total that means something and one that quietly
        // ignored rows.
        if (_values == 0)
            result.Kind = GridValueKind.Empty;
        else if (_numericValues == _values)
            result.Kind = GridValueKind.Numeric;
        else if (_dateValues == _values)
            result.Kind = GridValueKind.DateTime;
        else
            result.Kind = GridValueKind.Text;

        switch (result.Kind)
        {
            case GridValueKind.Numeric:
                result.Scale = _scale;
                result.MinText = _minNumText;
                result.MaxText = _maxNumText;
                if (_decimalUsable)
                {
                    result.SumDecimal = _sumDec;
                    result.AverageDecimal = _sumDec / _values;
                }
                else
                {
                    result.SumDouble = _sumDbl;
                    result.AverageDouble = _sumDbl / _values;
                }
                break;

            case GridValueKind.DateTime:
                result.MinText = _minDateText;
                result.MaxText = _maxDateText;
                break;

            case GridValueKind.Text:
                result.MinText = _minText;
                result.MaxText = _maxText;
                break;
        }

        return result;
    }

    /// <summary>
    /// Parses a rendered number. <paramref name="dec"/> is set only when the value is exactly
    /// representable as a <see cref="decimal"/>, which is what lets the caller keep an exact running total.
    ///
    /// <para><see cref="NumberStyles.AllowThousands"/> is deliberately <b>not</b> used. The grid renders
    /// numerics unseparated (<c>1234.5600</c>), so it buys nothing — and it actively breaks non-English
    /// locales, where .NET reads the invariant parse of <c>"1,5"</c> as a group separator and returns
    /// <b>15</b>. Current culture is tried first for the same reason: the grid renders with it.</para>
    /// </summary>
    internal static bool TryParseNumber(string text, out double dbl, out decimal? dec, out int scale)
    {
        dec = null;
        scale = 0;

        const NumberStyles Styles = NumberStyles.Float;
        CultureInfo culture = CultureInfo.CurrentCulture;

        if (!double.TryParse(text, Styles, culture, out dbl))
        {
            culture = CultureInfo.InvariantCulture;
            if (!double.TryParse(text, Styles, culture, out dbl))
                return false;
        }

        if (decimal.TryParse(text, Styles, culture, out decimal parsed))
            dec = parsed;

        scale = DecimalPlaces(text, culture);
        return true;
    }

    /// <summary>Digits after the decimal separator, so a summed money column still formats to cents.
    /// Scientific notation reports 0 — the exponent makes a digit count meaningless.</summary>
    private static int DecimalPlaces(string text, CultureInfo culture)
    {
        if (text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0)
            return 0;

        string separator = culture.NumberFormat.NumberDecimalSeparator;
        int at = text.LastIndexOf(separator, StringComparison.Ordinal);
        if (at < 0)
            return 0;

        int places = 0;
        for (int i = at + separator.Length; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
                return places;
            places++;
        }
        return places;
    }

    /// <summary>
    /// Parses a rendered date, but only for text already shaped like one.
    ///
    /// <para><see cref="DateTime.TryParse(string, IFormatProvider, DateTimeStyles, out DateTime)"/> alone is
    /// far too willing: under an English culture it accepts <c>"1,5"</c> (a German-rendered decimal) as a
    /// date, so a text column would be labelled Date/time and its Min/Max ordered chronologically. Every
    /// shape SQL Server renders a date, time, datetime2 or datetimeoffset in carries a <c>-</c> or a
    /// <c>:</c>, so requiring one costs nothing real and removes the whole class of false positives.</para>
    /// </summary>
    private static bool TryParseDate(string text, out DateTime value)
    {
        value = default;
        if (text.Length < 6 || text.IndexOfAny(DateSeparators) < 0)
            return false;

        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out value) ||
               DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static readonly char[] DateSeparators = { '-', ':', '/' };
}

/// <summary>
/// Renders aggregates for display. Kept apart from the accumulator (and free of WPF) so the arithmetic
/// and its presentation can be tested separately — a total that is right but shown to the wrong number
/// of decimal places is still a wrong answer to the person reading it.
/// </summary>
internal static class GridAggregateFormat
{
    /// <summary>Beyond this, grouped fixed-point notation is unreadable and loses the magnitude; below
    /// its reciprocal, fixed-point rounds a small number to a row of zeros.</summary>
    private const double FixedPointCeiling = 1e15;
    private const double FixedPointFloor = 1e-4;

    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Kind(GridValueKind kind) => kind switch
    {
        GridValueKind.Numeric => "Number",
        GridValueKind.DateTime => "Date/time",
        GridValueKind.Text => "Text",
        _ => "—"
    };

    /// <summary>
    /// The sum, at the same number of decimal places as the widest value that went into it — so totalling
    /// a money column yields cents rather than a rounded-looking whole number.
    /// </summary>
    public static string Sum(GridColumnAggregate column)
    {
        if (column == null)
            return null;
        if (column.SumDecimal.HasValue)
            return Fixed(column.SumDecimal.Value, column.Scale);
        return column.SumDouble.HasValue ? Approximate(column.SumDouble.Value, column.Scale) : null;
    }

    /// <summary>The mean, never below two decimal places — an average of integers that reads "3" when it
    /// is 3.33 is the one rounding nobody expects.</summary>
    public static string Average(GridColumnAggregate column)
    {
        if (column == null)
            return null;
        int places = Math.Min(6, Math.Max(2, column.Scale));
        if (column.AverageDecimal.HasValue)
            return Fixed(column.AverageDecimal.Value, places);
        return column.AverageDouble.HasValue ? Approximate(column.AverageDouble.Value, places) : null;
    }

    private static string Fixed(decimal value, int places) =>
        value.ToString("N" + Math.Min(10, Math.Max(0, places)), CultureInfo.CurrentCulture);

    private static string Approximate(double value, int places)
    {
        double magnitude = Math.Abs(value);
        if (magnitude >= FixedPointCeiling || (magnitude > 0d && magnitude < FixedPointFloor))
            return value.ToString("G6", CultureInfo.CurrentCulture);
        return value.ToString("N" + Math.Min(10, Math.Max(0, places)), CultureInfo.CurrentCulture);
    }
}
