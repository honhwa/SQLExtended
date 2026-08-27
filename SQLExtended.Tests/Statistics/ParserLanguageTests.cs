// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core.Tests/ParserLanguageTests.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using StatisticsParser.Core.Models;
using StatisticsParser.Core.Parsing;
using Xunit;

namespace StatisticsParser.Core.Tests;

public class ParserLanguageTests
{
    public static TheoryData<string> AllLanguageCodes => new()
    {
        "en",
        "es",
        "it",
    };

    [Theory]
    [MemberData(nameof(AllLanguageCodes))]
    public void AllLanguageSingletons_LoadAndPopulateColumnVariants(string langValue)
    {
        var lang = langValue switch
        {
            "en" => ParserLanguage.English,
            "es" => ParserLanguage.Spanish,
            "it" => ParserLanguage.Italian,
            _ => null
        };

        Assert.NotNull(lang);
        Assert.Equal(langValue, lang!.LangValue);
        Assert.Contains(lang, ParserLanguage.All);

        Assert.False(string.IsNullOrEmpty(lang.LangName));
        Assert.False(string.IsNullOrEmpty(lang.Table));
        Assert.False(string.IsNullOrEmpty(lang.ExecutionTime));
        Assert.False(string.IsNullOrEmpty(lang.CompileTime));
        Assert.False(string.IsNullOrEmpty(lang.CpuTime));
        Assert.False(string.IsNullOrEmpty(lang.ElapsedTime));
        Assert.False(string.IsNullOrEmpty(lang.Milliseconds));

        Assert.NotEmpty(lang.ErrorMsg);
        Assert.NotEmpty(lang.RowsAffected);
        Assert.NotEmpty(lang.Scan);
        Assert.NotEmpty(lang.Logical);
        Assert.NotEmpty(lang.Physical);
        Assert.NotEmpty(lang.PageServer);
        Assert.NotEmpty(lang.ReadAhead);
        Assert.NotEmpty(lang.PageServerReadAhead);
        Assert.NotEmpty(lang.LobLogical);
        Assert.NotEmpty(lang.LobPhysical);
        Assert.NotEmpty(lang.LobPageServer);
        Assert.NotEmpty(lang.LobReadAhead);
        Assert.NotEmpty(lang.LobPageServerReadAhead);
        Assert.NotEmpty(lang.SegmentReads);
        Assert.NotEmpty(lang.SegmentSkipped);
    }

    [Fact]
    public void All_ContainsThreeLanguagesInOrder()
    {
        Assert.Equal(3, ParserLanguage.All.Count);
        Assert.Same(ParserLanguage.English, ParserLanguage.All[0]);
        Assert.Same(ParserLanguage.Spanish, ParserLanguage.All[1]);
        Assert.Same(ParserLanguage.Italian, ParserLanguage.All[2]);
    }

    [Theory]
    [InlineData("en", "scan count", IoColumn.Scan)]
    [InlineData("en", "Scan Count", IoColumn.Scan)]
    [InlineData("en", "SCAN COUNT", IoColumn.Scan)]
    [InlineData("en", "  scan count  ", IoColumn.Scan)]
    [InlineData("en", "logical reads", IoColumn.Logical)]
    [InlineData("en", "Read-Ahead Reads", IoColumn.ReadAhead)]
    [InlineData("en", "bogus column", IoColumn.NotFound)]
    [InlineData("es", "Recuento de Exámenes", IoColumn.Scan)]
    [InlineData("es", "lecturas lógicas", IoColumn.Logical)]
    [InlineData("es", "lecturas lógicas de LOB", IoColumn.LobLogical)]
    [InlineData("es", "Lecturas Anticipadas", IoColumn.ReadAhead)]
    [InlineData("it", "Conteggio analisi", IoColumn.Scan)]
    [InlineData("it", "letture logiche", IoColumn.Logical)]
    [InlineData("it", "letture logiche LOB", IoColumn.LobLogical)]
    [InlineData("it", "Letture Read-Ahead", IoColumn.ReadAhead)]
    public void DetermineIoColumn_IsCaseInsensitiveAndTrims(string langValue, string input, IoColumn expected)
    {
        var lang = langValue switch
        {
            "en" => ParserLanguage.English,
            "es" => ParserLanguage.Spanish,
            "it" => ParserLanguage.Italian,
            _ => null
        };

        Assert.NotNull(lang);
        Assert.Equal(expected, lang!.DetermineIoColumn(input));
    }

    [Theory]
    [MemberData(nameof(AllLanguageCodes))]
    public void AllLanguageSingletons_PopulateNumberFormatAndDisplayStrings(string langValue)
    {
        var lang = langValue switch
        {
            "en" => ParserLanguage.English,
            "es" => ParserLanguage.Spanish,
            "it" => ParserLanguage.Italian,
            _ => null
        };

        Assert.NotNull(lang);

        Assert.NotNull(lang!.NumberFormat);
        Assert.False(string.IsNullOrEmpty(lang.NumberFormat.ThousandSeparator));
        Assert.False(string.IsNullOrEmpty(lang.NumberFormat.DecimalSeparator));

        Assert.False(string.IsNullOrEmpty(lang.HeaderRowNum));
        Assert.False(string.IsNullOrEmpty(lang.HeaderTable));
        Assert.False(string.IsNullOrEmpty(lang.HeaderScan));
        Assert.False(string.IsNullOrEmpty(lang.HeaderLogical));
        Assert.False(string.IsNullOrEmpty(lang.HeaderPhysical));
        Assert.False(string.IsNullOrEmpty(lang.HeaderPageServer));
        Assert.False(string.IsNullOrEmpty(lang.HeaderReadAhead));
        Assert.False(string.IsNullOrEmpty(lang.HeaderPageServerReadAhead));
        Assert.False(string.IsNullOrEmpty(lang.HeaderLobLogical));
        Assert.False(string.IsNullOrEmpty(lang.HeaderLobPhysical));
        Assert.False(string.IsNullOrEmpty(lang.HeaderLobPageServer));
        Assert.False(string.IsNullOrEmpty(lang.HeaderLobReadAhead));
        Assert.False(string.IsNullOrEmpty(lang.HeaderLobPageServerReadAhead));
        Assert.False(string.IsNullOrEmpty(lang.HeaderPercentRead));
        Assert.False(string.IsNullOrEmpty(lang.HeaderSegmentReads));
        Assert.False(string.IsNullOrEmpty(lang.HeaderSegmentSkipped));
        Assert.False(string.IsNullOrEmpty(lang.CpuLabel));
        Assert.False(string.IsNullOrEmpty(lang.ElapsedLabel));
        Assert.False(string.IsNullOrEmpty(lang.TotalLabel));
        Assert.False(string.IsNullOrEmpty(lang.TotalsLabel));
        Assert.False(string.IsNullOrEmpty(lang.CompileSectionLabel));
        Assert.False(string.IsNullOrEmpty(lang.ExecutionSectionLabel));
    }

    [Fact]
    public void NumberFormat_MatchesUpstreamLocaleSeparators()
    {
        Assert.Equal(",", ParserLanguage.English.NumberFormat.ThousandSeparator);
        Assert.Equal(".", ParserLanguage.English.NumberFormat.DecimalSeparator);

        Assert.Equal(".", ParserLanguage.Spanish.NumberFormat.ThousandSeparator);
        Assert.Equal(",", ParserLanguage.Spanish.NumberFormat.DecimalSeparator);

        Assert.Equal(".", ParserLanguage.Italian.NumberFormat.ThousandSeparator);
        Assert.Equal(",", ParserLanguage.Italian.NumberFormat.DecimalSeparator);
    }

    private const string EnglishDetectSample =
        "(13431682 rows affected)\n" +
        "Table 'Users'. Scan count 5, logical reads 42015, physical reads 1, read-ahead reads 41305.\n" +
        "Table 'Posts'. Scan count 5, logical reads 396629, physical reads 26, read-ahead reads 394952.\n" +
        "\n" +
        " SQL Server Execution Times:\n" +
        "   CPU time = 164456 ms,  elapsed time = 293219 ms.\n";

    private const string SpanishDetectSample =
        "(13431682 filas afectadas)\n" +
        "Tabla 'Users'. Recuento de exámenes 5, lecturas lógicas 42015, lecturas físicas 1, lecturas anticipadas 41305.\n" +
        "Tabla 'Posts'. Recuento de exámenes 5, lecturas lógicas 396629, lecturas físicas 26, lecturas anticipadas 394952.\n" +
        "\n" +
        " Tiempos de ejecución de SQL Server:\n" +
        "   Tiempo de CPU = 164456 ms, tiempo transcurrido = 293219 ms.\n";

    private const string ItalianDetectSample =
        "(25499 righe interessate)\n" +
        "Tabella 'Workfile'. Conteggio analisi 0, letture logiche 0, letture fisiche 0, letture read-ahead 0.\n" +
        "Tabella 'Worktable'. Conteggio analisi 0, letture logiche 0, letture fisiche 0, letture read-ahead 856.\n" +
        "\n" +
        "Tempo di esecuzione SQL Server:\n" +
        " tempo di CPU = 657 ms, tempo trascorso = 944 ms.\n";

    [Fact]
    public void Detect_RecognizesEachLanguageFromMarkers()
    {
        Assert.Same(ParserLanguage.English, ParserLanguage.Detect(EnglishDetectSample));
        Assert.Same(ParserLanguage.Spanish, ParserLanguage.Detect(SpanishDetectSample));
        Assert.Same(ParserLanguage.Italian, ParserLanguage.Detect(ItalianDetectSample));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  \n")]
    [InlineData("just some unrelated text\nwith no statistics markers at all")]
    public void Detect_FallsBackToEnglishWhenNoMarkers(string text)
    {
        Assert.Same(ParserLanguage.English, ParserLanguage.Detect(text));
    }

    [Fact]
    public void Detect_FallsBackToEnglishOnNull()
    {
        Assert.Same(ParserLanguage.English, ParserLanguage.Detect(null!));
    }

    [Fact]
    public void Detect_ItalianTabellaIsNotMisreadAsSpanishTabla()
    {
        // "Tabella" must not be counted as Spanish "Tabla"; an Italian-only sample stays Italian.
        Assert.Same(ParserLanguage.Italian, ParserLanguage.Detect(ItalianDetectSample));
    }
}
