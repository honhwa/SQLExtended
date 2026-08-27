// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core.Tests/ParserMixedLanguageTests.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;
using StatisticsParser.Core.Models;
using StatisticsParser.Core.Parsing;
using Xunit;

namespace StatisticsParser.Core.Tests;

// The SSMS Messages tab mixes two independently-localized sources: the SQL Server engine
// emits STATISTICS IO/TIME output (and error message bodies) in the session language, while
// SSMS itself formats the rows-affected line, the error header, and the completion-time line
// in its own UI language. SET LANGUAGE changes only the former. These tests cover the case
// where the two differ — the parser detects the session language from the STATISTICS output
// but must still recognize the client-emitted lines regardless of that detected language.
public class ParserMixedLanguageTests
{
    // English STATISTICS output (SET LANGUAGE English) but Italian SSMS UI: the
    // rows-affected, error-header, and completion-time lines are Italian.
    private const string EnglishSessionItalianClientSample =
        "SQL Server parse and compile time: \n" +
        "   CPU time = 110 ms, elapsed time = 117 ms.\n" +
        "\n" +
        "(2 righe interessate)\n" +
        "Table 'Comments'. Scan count 9, logical reads 64777, physical reads 0, read-ahead reads 0.\n" +
        "Table 'Posts'. Scan count 9, logical reads 38633, physical reads 0, read-ahead reads 0.\n" +
        "\n" +
        " SQL Server Execution Times:\n" +
        "   CPU time = 7219 ms,  elapsed time = 1073 ms.\n" +
        "Messaggio 207, livello 16, stato 1, riga 45\n" +
        "Invalid column name 'scores'.\n" +
        "\n" +
        "Ora di completamento: 2026-05-19T13:34:31.9931250-04:00\n";

    [Fact]
    public void Detect_EnglishSessionItalianClient_DetectsEnglishFromStatisticsOutput()
    {
        // Detection keys off the engine-emitted STATISTICS markers (the session language),
        // so the Italian client lines must not flip detection away from English.
        Assert.Same(ParserLanguage.English, ParserLanguage.Detect(EnglishSessionItalianClientSample));
    }

    [Fact]
    public void ParseData_EnglishSessionItalianClient_RecognizesItalianClientLines()
    {
        var result = Parser.ParseData(EnglishSessionItalianClientSample, ParserLanguage.English);

        // The Italian "(2 righe interessate)" line must classify as RowsAffected, not InfoRow.
        var rowsAffected = Assert.Single(result.Data.OfType<RowsAffectedRow>());
        Assert.Equal(2, rowsAffected.Count);
        Assert.Equal("righe interessate", rowsAffected.Label);

        // The Italian "Messaggio ..." header and its English body are both captured as errors.
        var errors = result.Data.OfType<ErrorRow>().Select(r => r.Text).ToList();
        Assert.Equal(
            new[] { "Messaggio 207, livello 16, stato 1, riga 45", "Invalid column name 'scores'." },
            errors);

        // The Italian "Ora di completamento: " line is parsed and its label echoed verbatim.
        var completion = Assert.Single(result.Data.OfType<CompletionTimeRow>());
        var expected = DateTimeOffset.Parse(
            "2026-05-19T13:34:31.9931250-04:00",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
        Assert.Equal(expected, completion.Timestamp);
        Assert.Equal("Ora di completamento: ", completion.Label);

        // None of the client-emitted lines should fall through to a plain InfoRow.
        Assert.Empty(result.Data.OfType<InfoRow>());

        // The English STATISTICS output is still parsed normally with the detected language.
        var group = Assert.Single(result.Data.OfType<IoGroup>());
        Assert.Equal(2, group.Data.Count);
    }

    // The symmetric case: English STATISTICS output but Spanish SSMS UI.
    private const string EnglishSessionSpanishClientSample =
        "(2 filas afectadas)\n" +
        "Table 'Posts'. Scan count 1, logical reads 100, physical reads 0, read-ahead reads 0.\n" +
        "\n" +
        " SQL Server Execution Times:\n" +
        "   CPU time = 5 ms,  elapsed time = 5 ms.\n" +
        "Mensaje 207, nivel 16, estado 1, línea 1\n" +
        "Nombre de columna 'scores' no válido.\n" +
        "\n" +
        "Hora de finalización: 2026-05-19T13:13:00.7217148-04:00\n";

    [Fact]
    public void ParseData_EnglishSessionSpanishClient_RecognizesSpanishClientLines()
    {
        Assert.Same(ParserLanguage.English, ParserLanguage.Detect(EnglishSessionSpanishClientSample));

        var result = Parser.ParseData(EnglishSessionSpanishClientSample, ParserLanguage.English);

        var rowsAffected = Assert.Single(result.Data.OfType<RowsAffectedRow>());
        Assert.Equal(2, rowsAffected.Count);
        Assert.Equal("filas afectadas", rowsAffected.Label);

        var errors = result.Data.OfType<ErrorRow>().Select(r => r.Text).ToList();
        Assert.Equal(
            new[] { "Mensaje 207, nivel 16, estado 1, línea 1", "Nombre de columna 'scores' no válido." },
            errors);

        var completion = Assert.Single(result.Data.OfType<CompletionTimeRow>());
        Assert.Equal("Hora de finalización: ", completion.Label);

        Assert.Empty(result.Data.OfType<InfoRow>());
    }
}
