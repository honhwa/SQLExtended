// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Models/IoColumn.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
namespace StatisticsParser.Core.Models;

public enum IoColumn
{
    NotFound,
    Table,
    Scan,
    Logical,
    Physical,
    PageServer,
    ReadAhead,
    PageServerReadAhead,
    LobLogical,
    LobPhysical,
    LobPageServer,
    LobReadAhead,
    LobPageServerReadAhead,
    PercentRead,
    SegmentReads,
    SegmentSkipped
}
