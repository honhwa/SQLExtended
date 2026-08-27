using System;
using SQLExtended.Snippets;
using Xunit;

namespace SQLExtended.Tests;

public class SnippetPlaceholderResolverTests
{
    [Fact]
    public void Resolve_NullBody_ReturnsNull()
    {
        Assert.Null(SnippetPlaceholderResolver.Resolve(null));
    }

    [Fact]
    public void Resolve_EmptyBody_ReturnsEmpty()
    {
        Assert.Equal("", SnippetPlaceholderResolver.Resolve(""));
    }

    [Fact]
    public void Resolve_NoPlaceholders_ReturnsUnchanged()
    {
        string body = "SELECT * FROM dbo.Customers";
        Assert.Equal(body, SnippetPlaceholderResolver.Resolve(body));
    }

    [Fact]
    public void Resolve_DatePlaceholder_ReturnsCurrentDate()
    {
        string result = SnippetPlaceholderResolver.Resolve("-- $date$");
        Assert.Equal($"-- {DateTime.Now:yyyy-MM-dd}", result);
    }

    [Fact]
    public void Resolve_TimePlaceholder_ReturnsTime()
    {
        string result = SnippetPlaceholderResolver.Resolve("-- $time$");
        // Just verify the format (HH:mm:ss)
        Assert.Matches(@"-- \d{2}:\d{2}:\d{2}", result);
    }

    [Fact]
    public void Resolve_DateTimePlaceholder_ReturnsDateTime()
    {
        string result = SnippetPlaceholderResolver.Resolve("-- $datetime$");
        Assert.Matches(@"-- \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", result);
    }

    [Fact]
    public void Resolve_YearPlaceholder_ReturnsCurrentYear()
    {
        string result = SnippetPlaceholderResolver.Resolve("$year$");
        Assert.Equal(DateTime.Now.Year.ToString(), result);
    }

    [Fact]
    public void Resolve_MonthPlaceholder_ReturnsTwoDigitMonth()
    {
        string result = SnippetPlaceholderResolver.Resolve("$month$");
        Assert.Equal(DateTime.Now.Month.ToString("D2"), result);
    }

    [Fact]
    public void Resolve_DayPlaceholder_ReturnsTwoDigitDay()
    {
        string result = SnippetPlaceholderResolver.Resolve("$day$");
        Assert.Equal(DateTime.Now.Day.ToString("D2"), result);
    }

    [Fact]
    public void Resolve_UserPlaceholder_ReturnsUserName()
    {
        string result = SnippetPlaceholderResolver.Resolve("-- Author: $user$");
        Assert.Equal($"-- Author: {Environment.UserName}", result);
    }

    [Fact]
    public void Resolve_MachinePlaceholder_ReturnsMachineName()
    {
        string result = SnippetPlaceholderResolver.Resolve("$machine$");
        Assert.Equal(Environment.MachineName, result);
    }

    [Fact]
    public void Resolve_GuidPlaceholder_ReturnsValidGuid()
    {
        string result = SnippetPlaceholderResolver.Resolve("$guid$");
        Assert.True(Guid.TryParse(result, out _));
    }

    [Fact]
    public void Resolve_GuidPlaceholder_ReturnsUniqueValues()
    {
        string result1 = SnippetPlaceholderResolver.Resolve("$guid$");
        string result2 = SnippetPlaceholderResolver.Resolve("$guid$");
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void Resolve_MultiplePlaceholders_ResolvesAll()
    {
        string body = "-- $date$ | $user$ | Change description";
        string result = SnippetPlaceholderResolver.Resolve(body);

        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), result);
        Assert.Contains(Environment.UserName, result);
        Assert.Contains("| Change description", result);
    }

    [Fact]
    public void Resolve_UnknownPlaceholder_LeavesUnchanged()
    {
        string body = "SELECT $unknown_placeholder$ FROM table";
        string result = SnippetPlaceholderResolver.Resolve(body);
        Assert.Equal(body, result);
    }

    [Fact]
    public void Resolve_MixedKnownAndUnknown_ResolvesKnownOnly()
    {
        string body = "$date$ $unknown$ $user$";
        string result = SnippetPlaceholderResolver.Resolve(body);

        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), result);
        Assert.Contains("$unknown$", result);
        Assert.Contains(Environment.UserName, result);
    }

    [Fact]
    public void Resolve_CaseInsensitive_ResolvesUpperCase()
    {
        // Placeholders are lowercased internally, so $DATE$ should also resolve
        string result = SnippetPlaceholderResolver.Resolve("$DATE$");
        Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), result);
    }

    [Fact]
    public void Resolve_DbNameWithoutConnection_LeavesPlaceholder()
    {
        // No ConnectionInfoProvider set, so $dbname$ should remain
        var original = SnippetPlaceholderResolver.ConnectionInfoProvider;
        try
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = null;
            string result = SnippetPlaceholderResolver.Resolve("USE [$dbname$]");
            Assert.Equal("USE [$dbname$]", result);
        }
        finally
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = original;
        }
    }

    [Fact]
    public void Resolve_DbNameWithProvider_ResolvesValue()
    {
        var original = SnippetPlaceholderResolver.ConnectionInfoProvider;
        try
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = () => ("AdventureWorks", "localhost");
            string result = SnippetPlaceholderResolver.Resolve("USE [$dbname$]");
            Assert.Equal("USE [AdventureWorks]", result);
        }
        finally
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = original;
        }
    }

    [Fact]
    public void Resolve_ServerWithProvider_ResolvesValue()
    {
        var original = SnippetPlaceholderResolver.ConnectionInfoProvider;
        try
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = () => ("TestDB", "sql-prod-01");
            string result = SnippetPlaceholderResolver.Resolve("-- Server: $server$");
            Assert.Equal("-- Server: sql-prod-01", result);
        }
        finally
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = original;
        }
    }

    [Fact]
    public void Resolve_ProviderThrows_LeavesPlaceholder()
    {
        var original = SnippetPlaceholderResolver.ConnectionInfoProvider;
        try
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = () => throw new InvalidOperationException("no connection");
            string result = SnippetPlaceholderResolver.Resolve("$dbname$ $server$");
            Assert.Equal("$dbname$ $server$", result);
        }
        finally
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = original;
        }
    }

    [Fact]
    public void Resolve_SingleDollarSign_NotTreatedAsPlaceholder()
    {
        string body = "WHERE price > $100";
        Assert.Equal(body, SnippetPlaceholderResolver.Resolve(body));
    }

    [Fact]
    public void Resolve_HeaderSnippet_ResolvesAllPlaceholders()
    {
        var original = SnippetPlaceholderResolver.ConnectionInfoProvider;
        try
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = () => ("MyDB", "MyServer");

            string body = "-- Author: $user$\n-- Date: $date$\n-- Database: $dbname$";
            string result = SnippetPlaceholderResolver.Resolve(body);

            Assert.Contains(Environment.UserName, result);
            Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), result);
            Assert.Contains("MyDB", result);
            Assert.DoesNotContain("$user$", result);
            Assert.DoesNotContain("$date$", result);
            Assert.DoesNotContain("$dbname$", result);
        }
        finally
        {
            SnippetPlaceholderResolver.ConnectionInfoProvider = original;
        }
    }

    [Fact]
    public void HasPlaceholders_WithPlaceholders_ReturnsTrue()
    {
        Assert.True(SnippetPlaceholderResolver.HasPlaceholders("-- $date$ header"));
    }

    [Fact]
    public void HasPlaceholders_WithoutPlaceholders_ReturnsFalse()
    {
        Assert.False(SnippetPlaceholderResolver.HasPlaceholders("SELECT * FROM table"));
    }

    [Fact]
    public void HasPlaceholders_Null_ReturnsFalse()
    {
        Assert.False(SnippetPlaceholderResolver.HasPlaceholders(null));
    }

    [Fact]
    public void HasPlaceholders_SingleDollar_ReturnsFalse()
    {
        Assert.False(SnippetPlaceholderResolver.HasPlaceholders("$100"));
    }

    [Fact]
    public void BuiltInPlaceholders_ContainsExpectedEntries()
    {
        var placeholders = SnippetPlaceholderResolver.BuiltInPlaceholders;
        Assert.True(placeholders.Count >= 11);

        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var p in placeholders)
            names.Add(p.Name);

        Assert.Contains("date", names);
        Assert.Contains("time", names);
        Assert.Contains("datetime", names);
        Assert.Contains("user", names);
        Assert.Contains("dbname", names);
        Assert.Contains("server", names);
        Assert.Contains("guid", names);
        Assert.Contains("year", names);
        Assert.Contains("month", names);
        Assert.Contains("day", names);
        Assert.Contains("machine", names);
    }

    // --- GetCustomPlaceholderNames tests ---

    [Fact]
    public void GetCustomPlaceholderNames_NullBody_ReturnsEmpty()
    {
        Assert.Empty(SnippetPlaceholderResolver.GetCustomPlaceholderNames(null));
    }

    [Fact]
    public void GetCustomPlaceholderNames_NoPlaceholders_ReturnsEmpty()
    {
        Assert.Empty(SnippetPlaceholderResolver.GetCustomPlaceholderNames("SELECT * FROM table"));
    }

    [Fact]
    public void GetCustomPlaceholderNames_OnlySystemPlaceholders_ReturnsEmpty()
    {
        Assert.Empty(SnippetPlaceholderResolver.GetCustomPlaceholderNames("$date$ $user$ $dbname$"));
    }

    [Fact]
    public void GetCustomPlaceholderNames_CustomPlaceholders_ReturnsNames()
    {
        var names = SnippetPlaceholderResolver.GetCustomPlaceholderNames("SELECT TOP $count$ FROM $table$");
        Assert.Equal(2, names.Count);
        Assert.Equal("count", names[0]);
        Assert.Equal("table", names[1]);
    }

    [Fact]
    public void GetCustomPlaceholderNames_MixedSystemAndCustom_ReturnsOnlyCustom()
    {
        var names = SnippetPlaceholderResolver.GetCustomPlaceholderNames("-- $date$ $user$\nSELECT TOP $count$ FROM $table$");
        Assert.Equal(2, names.Count);
        Assert.Equal("count", names[0]);
        Assert.Equal("table", names[1]);
    }

    [Fact]
    public void GetCustomPlaceholderNames_DuplicatePlaceholders_ReturnsDistinct()
    {
        var names = SnippetPlaceholderResolver.GetCustomPlaceholderNames("WITH $cte$ AS (...) SELECT * FROM $cte$");
        Assert.Single(names);
        Assert.Equal("cte", names[0]);
    }

    // --- ResolveSystemOnly tests ---

    [Fact]
    public void ResolveSystemOnly_ResolvesDateLeavesCustom()
    {
        string result = SnippetPlaceholderResolver.ResolveSystemOnly("-- $date$ SELECT TOP $count$");
        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), result);
        Assert.Contains("$count$", result);
    }

    [Fact]
    public void ResolveSystemOnly_NullBody_ReturnsNull()
    {
        Assert.Null(SnippetPlaceholderResolver.ResolveSystemOnly(null));
    }

    [Fact]
    public void ResolveSystemOnly_NoPlaceholders_ReturnsSame()
    {
        Assert.Equal("SELECT 1", SnippetPlaceholderResolver.ResolveSystemOnly("SELECT 1"));
    }

    // --- Resolve with custom defaults ---

    [Fact]
    public void ResolveWithDefaults_SubstitutesCustomValues()
    {
        var defaults = new System.Collections.Generic.Dictionary<string, string>
        {
            { "count", "100" },
            { "table", "MyTable" }
        };
        string result = SnippetPlaceholderResolver.Resolve("SELECT TOP $count$ FROM $table$", defaults);
        Assert.Equal("SELECT TOP 100 FROM MyTable", result);
    }

    [Fact]
    public void ResolveWithDefaults_SystemAndCustomMixed()
    {
        var defaults = new System.Collections.Generic.Dictionary<string, string>
        {
            { "table", "MyTable" }
        };
        string result = SnippetPlaceholderResolver.Resolve("-- $date$ SELECT FROM $table$", defaults);
        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), result);
        Assert.Contains("MyTable", result);
        Assert.DoesNotContain("$table$", result);
    }

    [Fact]
    public void ResolveWithDefaults_NullDefaults_BehavesLikeResolve()
    {
        string result = SnippetPlaceholderResolver.Resolve("$unknown$", null);
        Assert.Equal("$unknown$", result);
    }

    [Fact]
    public void ResolveWithDefaults_CaseInsensitiveLookup()
    {
        var defaults = new System.Collections.Generic.Dictionary<string, string>
        {
            { "Count", "50" }
        };
        string result = SnippetPlaceholderResolver.Resolve("TOP $count$", defaults);
        Assert.Equal("TOP 50", result);
    }
}
