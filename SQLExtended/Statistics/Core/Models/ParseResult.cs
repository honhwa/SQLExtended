// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Models/ParseResult.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System.Collections.Generic;
using StatisticsParser.Core.Parsing;

namespace StatisticsParser.Core.Models;

public class ParseResult
{
    public List<IResultRow> Data { get; set; } = new();
    public int TableCount { get; set; }
    public ParseResultTotal Total { get; set; } = new();

    // The language ParseData used. Set by the parser; read by the rendering layer to drive
    // localized headers/labels and locale-aware number formatting.
    public ParserLanguage Language { get; set; } = ParserLanguage.English;
}
