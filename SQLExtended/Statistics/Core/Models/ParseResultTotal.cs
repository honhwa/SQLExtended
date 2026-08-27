// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Models/ParseResultTotal.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
namespace StatisticsParser.Core.Models;

public class ParseResultTotal
{
    public TimeTotal ExecutionTotal { get; set; } = new() { RowType = RowType.ExecutionTimeTotal };
    public TimeTotal CompileTotal { get; set; } = new() { RowType = RowType.CompileTimeTotal };
    public IoGrandTotal IoTotal { get; set; } = new();
}
