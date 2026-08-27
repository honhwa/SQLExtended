// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Models/IoGrandTotal.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System.Collections.Generic;

namespace StatisticsParser.Core.Models;

public class IoGrandTotal
{
    public List<IoColumn> Columns { get; set; } = new();
    public List<IoGroupTotal> Data { get; set; } = new();
    public IoGroupTotal Total { get; set; } = new();
}
