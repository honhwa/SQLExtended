// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Parsing/ParserLanguage.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using StatisticsParser.Core.Models;

namespace StatisticsParser.Core.Parsing;

public sealed class ParserLanguage
{
    public string LangValue { get; set; } = "";
    public string LangName { get; set; } = "";

    public string Table { get; set; } = "";
    public string ExecutionTime { get; set; } = "";
    public string CompileTime { get; set; } = "";
    public string CpuTime { get; set; } = "";
    public string ElapsedTime { get; set; } = "";
    public string Milliseconds { get; set; } = "";
    public IReadOnlyList<string> RowsAffected { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ErrorMsg { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Scan { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Logical { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Physical { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> PageServer { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReadAhead { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> PageServerReadAhead { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> LobLogical { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> LobPhysical { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> LobPageServer { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> LobReadAhead { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> LobPageServerReadAhead { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SegmentReads { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SegmentSkipped { get; set; } = Array.Empty<string>();

    // Locale grouping/decimal separators for display rendering. The parser does not use these —
    // SQL Server emits culture-invariant integers — but the rendering layer formats parsed
    // numbers with the separators of the language the text was parsed in.
    public NumberSeparators NumberFormat { get; set; } = new NumberSeparators(",", ".");

    // Localized display strings for the rendering layer. English values are byte-identical to the
    // strings the VSIX tool window previously hardcoded; es/it values come verbatim from the
    // upstream languagetext-*.json header*/totals/headerrowsaffected keys. Embedded "\n" marks the
    // tool window's column-header line breaks.
    public string HeaderRowNum { get; set; } = "";
    public string HeaderTable { get; set; } = "";
    public string HeaderScan { get; set; } = "";
    public string HeaderLogical { get; set; } = "";
    public string HeaderPhysical { get; set; } = "";
    public string HeaderPageServer { get; set; } = "";
    public string HeaderReadAhead { get; set; } = "";
    public string HeaderPageServerReadAhead { get; set; } = "";
    public string HeaderLobLogical { get; set; } = "";
    public string HeaderLobPhysical { get; set; } = "";
    public string HeaderLobPageServer { get; set; } = "";
    public string HeaderLobReadAhead { get; set; } = "";
    public string HeaderLobPageServerReadAhead { get; set; } = "";
    public string HeaderPercentRead { get; set; } = "";
    public string HeaderSegmentReads { get; set; } = "";
    public string HeaderSegmentSkipped { get; set; } = "";

    public string CpuLabel { get; set; } = "";
    public string ElapsedLabel { get; set; } = "";
    public string TotalLabel { get; set; } = "";
    public string TotalsLabel { get; set; } = "";
    public string CompileSectionLabel { get; set; } = "";
    public string ExecutionSectionLabel { get; set; } = "";

    public static ParserLanguage English { get; } = new ParserLanguage
    {
        LangValue = "en",
        LangName = "English",
        Table = "Table",
        ExecutionTime = "SQL Server Execution Times:",
        CompileTime = "SQL Server parse and compile time:",
        CpuTime = "CPU time = ",
        ElapsedTime = "elapsed time = ",
        Milliseconds = "ms",
        RowsAffected = new[] { "row(s) affected", "row affected", "rows affected" },
        ErrorMsg = new[] { "Msg" },
        Scan = new[] { "scan count" },
        Logical = new[] { "logical reads" },
        Physical = new[] { "physical reads" },
        PageServer = new[] { "page server reads" },
        ReadAhead = new[] { "read-ahead reads" },
        PageServerReadAhead = new[] { "page server read-ahead reads" },
        LobLogical = new[] { "lob logical reads" },
        LobPhysical = new[] { "lob physical reads" },
        LobPageServer = new[] { "lob page server reads" },
        LobReadAhead = new[] { "lob read-ahead reads" },
        LobPageServerReadAhead = new[] { "lob page server read-ahead reads" },
        SegmentReads = new[] { "segment reads" },
        SegmentSkipped = new[] { "segment skipped" },
        NumberFormat = new NumberSeparators(",", "."),
        HeaderRowNum = "Row Num",
        HeaderTable = "Table",
        HeaderScan = "Scan Count",
        HeaderLogical = "Logical Reads",
        HeaderPhysical = "Physical Reads",
        HeaderPageServer = "Page Server Reads",
        HeaderReadAhead = "Read-Ahead Reads",
        HeaderPageServerReadAhead = "Page Server\nRead-Ahead Reads",
        HeaderLobLogical = "LOB \nLogical Reads",
        HeaderLobPhysical = "LOB \nPhysical Reads",
        HeaderLobPageServer = "LOB \nPage Server Reads",
        HeaderLobReadAhead = "LOB \nRead-Ahead Reads",
        HeaderLobPageServerReadAhead = "LOB Page Server\nRead-Ahead Reads",
        HeaderPercentRead = "% Logical Reads\nof Total Reads",
        HeaderSegmentReads = "Segment Reads",
        HeaderSegmentSkipped = "Segment Skipped",
        CpuLabel = "CPU",
        ElapsedLabel = "Elapsed",
        TotalLabel = "Total",
        TotalsLabel = "Totals:",
        CompileSectionLabel = "SQL Server parse and compile time",
        ExecutionSectionLabel = "SQL Server Execution Times"
    };

    public static ParserLanguage Spanish { get; } = new ParserLanguage
    {
        LangValue = "es",
        LangName = "Español",
        Table = "Tabla",
        ExecutionTime = "Tiempos de ejecución de SQL Server:",
        CompileTime = "Tiempo de análisis y compilación de SQL Server:",
        CpuTime = "Tiempo de CPU = ",
        ElapsedTime = "tiempo transcurrido = ",
        Milliseconds = "ms",
        // English fallbacks: a Spanish-language SQL Server session still emits the
        // rows-affected line in English ("(N rows affected)").
        RowsAffected = new[] { "filas afectadas", "fila afectada", "rows affected", "row affected", "row(s) affected" },
        ErrorMsg = new[] { "Mens", "Msg" },
        Scan = new[] { "recuento de exámenes", "número de examen" },
        Logical = new[] { "lecturas lógicas" },
        Physical = new[] { "lecturas físicas" },
        PageServer = new[] { "lecturas de servidor de páginas" },
        ReadAhead = new[] { "lecturas anticipadas" },
        PageServerReadAhead = new[] { "lecturas anticipadas de servidor de páginas" },
        LobLogical = new[] { "lecturas lógicas de lob", "lecturas lógicas de línea de negocio" },
        LobPhysical = new[] { "lecturas físicas de lob", "lecturas físicas de línea de negocio" },
        LobPageServer = new[] { "lecturas de servidor de páginas de línea de negocio" },
        LobReadAhead = new[] { "lecturas anticipadas de lob", "lecturas anticipadas de línea de negocio" },
        LobPageServerReadAhead = new[] { "lecturas anticipadas de servidor de páginas de línea de negocio" },
        SegmentReads = new[] { "lecturas de segmento" },
        SegmentSkipped = new[] { "segmento saltado" },
        NumberFormat = new NumberSeparators(".", ","),
        HeaderRowNum = "Fila Número",
        HeaderTable = "Tabla",
        HeaderScan = "Recuento de exámenes",
        HeaderLogical = "Lecturas lógicas",
        HeaderPhysical = "Lecturas físicas",
        HeaderPageServer = "Lecturas del servidor\nde páginas",
        HeaderReadAhead = "Lecturas anticipadas",
        HeaderPageServerReadAhead = "Lecturas de anticipación\ndel servidor de páginas",
        HeaderLobLogical = "Lecturas lógicas\nde LOB",
        HeaderLobPhysical = "Lecturas físicas\nde LOB",
        HeaderLobPageServer = "Lecturas del servidor\nde páginas de LOB",
        HeaderLobReadAhead = "Lecturas anticipadas\nde LOB",
        HeaderLobPageServerReadAhead = "Lecturas de anticipación del\nservidor de páginas de LOB",
        HeaderPercentRead = "% Lecturas lógicas\ndel Total de Lecturas",
        HeaderSegmentReads = "Lecturas de segmento",
        HeaderSegmentSkipped = "Segmento saltado",
        CpuLabel = "CPU",
        ElapsedLabel = "Transcurrido",
        TotalLabel = "Total",
        TotalsLabel = "Totales:",
        CompileSectionLabel = "Tiempo de análisis y compilación de SQL Server",
        ExecutionSectionLabel = "Tiempos de ejecución de SQL Server"
    };

    public static ParserLanguage Italian { get; } = new ParserLanguage
    {
        LangValue = "it",
        LangName = "Italian",
        Table = "Tabella",
        ExecutionTime = "Tempo di esecuzione SQL Server:",
        CompileTime = "Tempo di analisi e compilazione SQL Server:",
        CpuTime = "tempo di CPU = ",
        ElapsedTime = "tempo trascorso = ",
        Milliseconds = "ms",
        // English fallbacks: an Italian-language SQL Server session still emits the
        // rows-affected line in English ("(N rows affected)").
        RowsAffected = new[] { "righe interessate", "riga interessata", "rows affected", "row affected", "row(s) affected" },
        // "Mes" prefix-matches SQL Server's Italian error lines ("Messaggio NNN, livello ...");
        // "Msg" catches errors an Italian-localized server still emits in English. Error-line
        // detection is a prefix match (see DetermineRowType), so the truncated "Mes" is enough.
        ErrorMsg = new[] { "Mes", "Messaggio", "Msg" },
        Scan = new[] { "conteggio analisi" },
        Logical = new[] { "letture logiche" },
        Physical = new[] { "letture fisiche" },
        PageServer = new[] { "letture server di pagine" },
        ReadAhead = new[] { "letture read-ahead" },
        PageServerReadAhead = new[] { "letture read-ahead server di pagine" },
        LobLogical = new[] { "letture logiche lob" },
        LobPhysical = new[] { "letture fisiche lob" },
        LobPageServer = new[] { "letture lob server di pagine" },
        LobReadAhead = new[] { "letture lob read-ahead" },
        LobPageServerReadAhead = new[] { "letture read-ahead lob server di pagine" },
        SegmentReads = new[] { "letture segmento" },
        SegmentSkipped = new[] { "segmento saltato" },
        NumberFormat = new NumberSeparators(".", ","),
        HeaderRowNum = "Riga",
        HeaderTable = "Tabella",
        HeaderScan = "Conteggio analisi",
        HeaderLogical = "Letture logiche",
        HeaderPhysical = "Letture fisiche",
        HeaderPageServer = "Letture server\ndi pagine",
        HeaderReadAhead = "Letture read-ahead",
        HeaderPageServerReadAhead = "Letture read-ahead\nserver di pagine",
        HeaderLobLogical = "Letture logiche\nLOB",
        HeaderLobPhysical = "Letture fisiche\nLOB",
        HeaderLobPageServer = "Letture server\ndi pagine LOB",
        HeaderLobReadAhead = "Letture LOB\nread-ahead",
        HeaderLobPageServerReadAhead = "Letture read-ahead\nLOB server di pagine",
        HeaderPercentRead = "% Letture logiche\nsul totale",
        HeaderSegmentReads = "Letture segmento",
        HeaderSegmentSkipped = "Segmento saltato",
        CpuLabel = "CPU",
        ElapsedLabel = "Tempo trascorso",
        TotalLabel = "Totale",
        TotalsLabel = "Totali:",
        CompileSectionLabel = "Tempo di analisi e compilazione SQL Server",
        ExecutionSectionLabel = "Tempo di esecuzione SQL Server"
    };

    public static IReadOnlyList<ParserLanguage> All { get; } = new[] { English, Spanish, Italian };

    /// <summary>
    /// Best-effort language detection. Scans <paramref name="text"/> for unambiguous per-language
    /// markers (the IO-line table prefix, the CPU-time fragment, the execution/compile-time
    /// headers, and rows-affected phrases) and returns the language with the strictly highest
    /// marker count. Falls back to <see cref="English"/> on null/empty input, no markers, or a tie.
    /// </summary>
    public static ParserLanguage Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return English;

        var lines = text.Split('\n');

        ParserLanguage best = English;
        int bestScore = 0;
        bool tied = false;

        foreach (var lang in All)
        {
            int score = ScoreLanguage(lang, lines);
            if (score > bestScore)
            {
                bestScore = score;
                best = lang;
                tied = false;
            }
            else if (score == bestScore && score > 0)
            {
                tied = true;
            }
        }

        return (bestScore > 0 && !tied) ? best : English;
    }

    private static int ScoreLanguage(ParserLanguage lang, string[] lines)
    {
        int score = 0;
        foreach (var raw in lines)
        {
            var line = raw;
            if (line.Length > 0 && line[line.Length - 1] == '\r')
                line = line.Substring(0, line.Length - 1);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // IO-line table prefix — the strongest, most frequent marker. Requires a separator
            // char after the prefix so "Tabella" never counts as Spanish "Tabla" and vice versa.
            var trimmedStart = line.TrimStart();
            if (trimmedStart.Length > lang.Table.Length
                && trimmedStart.StartsWith(lang.Table, StringComparison.OrdinalIgnoreCase))
            {
                var ch = trimmedStart[lang.Table.Length];
                if (ch == ' ' || ch == '\'' || ch == '"' || ch == '\t')
                    score++;
            }

            if (lang.CpuTime.Length > 0
                && line.IndexOf(lang.CpuTime, StringComparison.OrdinalIgnoreCase) >= 0)
                score++;

            var trimmed = line.Trim();
            if (string.Equals(trimmed, lang.ExecutionTime, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, lang.CompileTime, StringComparison.OrdinalIgnoreCase))
                score++;

            for (int i = 0; i < lang.RowsAffected.Count; i++)
            {
                if (line.IndexOf(lang.RowsAffected[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score++;
                    break;
                }
            }
        }
        return score;
    }

    public IoColumn DetermineIoColumn(string columnText)
    {
        var trimmed = columnText.Trim();

        if (Match(Scan)) return IoColumn.Scan;
        if (Match(Logical)) return IoColumn.Logical;
        if (Match(Physical)) return IoColumn.Physical;
        if (Match(PageServer)) return IoColumn.PageServer;
        if (Match(ReadAhead)) return IoColumn.ReadAhead;
        if (Match(PageServerReadAhead)) return IoColumn.PageServerReadAhead;
        if (Match(LobLogical)) return IoColumn.LobLogical;
        if (Match(LobPhysical)) return IoColumn.LobPhysical;
        if (Match(LobPageServer)) return IoColumn.LobPageServer;
        if (Match(LobReadAhead)) return IoColumn.LobReadAhead;
        if (Match(LobPageServerReadAhead)) return IoColumn.LobPageServerReadAhead;
        if (Match(SegmentReads)) return IoColumn.SegmentReads;
        if (Match(SegmentSkipped)) return IoColumn.SegmentSkipped;
        return IoColumn.NotFound;

        bool Match(IReadOnlyList<string> variants)
        {
            for (int i = 0; i < variants.Count; i++)
            {
                if (string.Equals(trimmed, variants[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
