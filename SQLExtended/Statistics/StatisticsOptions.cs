using SQLExtended.Settings;
using StatisticsParser.Core.Formatting;
using StatisticsParser.Core.Parsing;

namespace SQLExtended.Statistics;

/// <summary>
/// Adapts <see cref="SQLExtendedSettings"/> to the vendored parser core's inputs, so the rest of the statistics code never
/// reaches into settings directly and the two <see cref="StatisticsLanguageOption"/>/<see cref="ParserLanguage"/>
/// vocabularies stay mapped in one place.
/// </summary>
internal static class StatisticsOptions
{
    public static bool SuppressZeroColumns => SQLExtendedSettings.Current.StatisticsSuppressZeroColumns;

    public static bool FormatTempTableNames => SQLExtendedSettings.Current.StatisticsFormatTempTableNames;

    /// <summary>
    /// The parser language to use for <paramref name="text"/>: the explicitly configured one, or — for
    /// <see cref="StatisticsLanguageOption.Auto"/> — whatever <see cref="ParserLanguage.Detect"/> finds in the output.
    /// </summary>
    public static ParserLanguage ResolveLanguage(string text) => SQLExtendedSettings.Current.StatisticsLanguage switch
    {
        StatisticsLanguageOption.English => ParserLanguage.English,
        StatisticsLanguageOption.Spanish => ParserLanguage.Spanish,
        StatisticsLanguageOption.Italian => ParserLanguage.Italian,
        _ => ParserLanguage.Detect(text)
    };

    /// <summary>Applies the temp-table name cleanup the user asked for, in the order upstream applies it.</summary>
    public static string FormatTableName(string name)
    {
        if (!FormatTempTableNames) return name;
        return TableNameFormatter.FormatForDisplay(TableNameFormatter.StripGeneratedSuffix(name));
    }
}
