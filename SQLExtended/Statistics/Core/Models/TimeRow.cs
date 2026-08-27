// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Models/TimeRow.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
namespace StatisticsParser.Core.Models;

public class TimeRow : IResultRow
{
    public RowType RowType { get; set; } = RowType.ExecutionTime;
    public int CpuMs { get; set; }
    public int ElapsedMs { get; set; }
    public bool Summary { get; set; }
}
