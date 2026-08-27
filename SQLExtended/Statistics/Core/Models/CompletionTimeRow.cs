// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Models/CompletionTimeRow.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System;

namespace StatisticsParser.Core.Models;

public class CompletionTimeRow : IResultRow
{
    public RowType RowType => RowType.CompletionTime;
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The label that preceded the timestamp in the message (e.g. "Completion time: ",
    /// "Hora de finalización: "). Taken verbatim from the output, so it is already in
    /// whatever language SSMS emitted, and the rendering layer echoes it back unchanged.
    /// </summary>
    public string Label { get; set; } = "";
}
