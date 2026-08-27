using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// The pure value conversions the Agent jobs dashboard needs, kept free of SqlClient and WPF so the test
/// project can link this one file rather than the whole subsystem.
///
/// Agent stores timestamps and durations as integers, not as datetime/time. msdb has an undocumented
/// dbo.agent_datetime() to undo the first half of that, but it throws on the zero dates that appear in
/// history rows for jobs that never ran, so the decoding happens here instead.
/// </summary>
internal static class JobValueParser
{
    /// <summary>
    /// Converts Agent's split integer timestamp (run_date as YYYYMMDD, run_time as HHMMSS) to a DateTime.
    /// Returns null for the zero and out-of-range values Agent writes when there is no real run behind the row.
    /// </summary>
    public static DateTime? ToDateTime(int runDate, int runTime)
    {
        if (runDate <= 0) return null;

        int year = runDate / 10000;
        int month = runDate / 100 % 100;
        int day = runDate % 100;
        if (year < 1753 || year > 9999 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;

        if (runTime < 0) runTime = 0;
        int hour = runTime / 10000;
        int minute = runTime / 100 % 100;
        int second = runTime % 100;
        if (hour > 23 || minute > 59 || second > 59) return null;

        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
    }

    /// <summary>
    /// Converts Agent's HHMMSS-as-integer duration to seconds. The hours field is not capped at 24 — a run of
    /// 100 hours is stored as 1000000 — so this is a positional decode, not a time-of-day parse.
    /// </summary>
    public static int DurationToSeconds(int runDuration)
    {
        if (runDuration <= 0) return 0;
        return runDuration / 10000 * 3600 + runDuration / 100 % 100 * 60 + runDuration % 100;
    }

    /// <summary>Splits the configured comma-separated hidden-category list, dropping blanks.</summary>
    public static List<string> ParseCategories(string commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated)) return new List<string>();

        return commaSeparated
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }
}
