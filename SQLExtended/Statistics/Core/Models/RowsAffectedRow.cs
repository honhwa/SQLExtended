// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Models/RowsAffectedRow.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
namespace StatisticsParser.Core.Models;

public class RowsAffectedRow : IResultRow
{
    public RowType RowType => RowType.RowsAffected;
    public int Count { get; set; }

    /// <summary>
    /// The phrase that followed the count in the message (e.g. "rows affected",
    /// "filas afectadas"). Taken verbatim from the output, so it is already
    /// correctly singular/plural and in whatever language SQL Server emitted.
    /// </summary>
    public string Label { get; set; } = "";
}
