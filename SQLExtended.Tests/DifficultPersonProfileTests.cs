using SQLExtended.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// Tests for the "DifficultPerson" T-SQL layout profile — the collection of switches that together
/// produce leading-comma / stacked-SELECT / stacked-CTE / river-WHERE formatting.
/// These lock in the behavior of the switches added for that profile.
/// </summary>
public class DifficultPersonProfileTests
{
    private readonly ITestOutputHelper _output;
    public DifficultPersonProfileTests(ITestOutputHelper output) => _output = output;

    /// <summary>The full profile: every switch that reproduces DifficultPerson's preferences.</summary>
    private static FormatterOptions Profile() => new FormatterOptions
    {
        KeywordCase = CasingOption.Upper,
        IndentStyle = IndentStyleOption.Tabs,
        IndentSize = 4,
        CommaPosition = CommaPositionOption.LeadingComma,
        LeadingCommaKeepIndent = true,
        SelectColumnLayout = SelectColumnLayoutOption.StackedFirstOnNewLine,
        JoinLayout = JoinLayoutOption.NewLine,
        JoinOnSameLine = true,
        NormalizeJoinKeywords = true,
        AlignFromAndJoins = true,
        WhereConditionLayout = WhereConditionLayoutOption.NewLinePerCondition,
        IndentBetweenConditions = false,
        AliasStyle = AliasStyleOption.ColumnEquals,
        BracketQuoting = BracketQuotingOption.RemoveBrackets,
        CteStackedLayout = true,
        AlignColumnDefinitionFields = false,
        NewLineBeforeCloseParenthesis = true,
        BlankLinesBetweenStatements = 1,
        MaxLineWidth = 120,
    };

    private string Format(string sql, FormatterOptions opts = null)
    {
        var result = new SqlFormatterService(opts ?? Profile()).Format(sql);
        _output.WriteLine(result.FormattedSql);
        Assert.True(result.Success, result.ErrorMessage);
        return result.FormattedSql.Replace("\r\n", "\n");
    }

    [Fact]
    public void Select_Join_Where_MatchesProfile()
    {
        var sql = "select p.PropertyGuid, p.Name as PropName, p.Type, p.SourceUrl from Properties p " +
                  "join Owners o on p.PropertyGuid = o.PropertyGuid left join Agents a on a.Id = p.AgentId " +
                  "where p.Status = 'Active' and p.CreatedDate > '2024-01-01' and o.Type = 'Premium'";

        var expected =
            "SELECT\n" +
            "\tp.PropertyGuid\n" +
            "\t, PropName = p.Name\n" +
            "\t, p.Type\n" +
            "\t, p.SourceUrl\n" +
            "FROM Properties AS p\n" +
            "INNER JOIN Owners AS o ON p.PropertyGuid = o.PropertyGuid\n" +
            "LEFT JOIN Agents AS a ON a.Id = p.AgentId\n" +
            "WHERE p.Status = 'Active'\n" +
            "AND p.CreatedDate > '2024-01-01'\n" +
            "AND o.Type = 'Premium';";

        Assert.Equal(expected, Format(sql));
    }

    [Fact]
    public void Cte_StackedLayout_MatchesProfile()
    {
        var sql = "with RankedPhones as (select PropertyGuid, Phone, row_number() over " +
                  "(partition by PropertyGuid order by Created desc) as rn from Phones), " +
                  "AggregatedEmails as (select PropertyGuid, Email from Emails) " +
                  "select r.PropertyGuid, r.Phone from RankedPhones r " +
                  "join AggregatedEmails e on e.PropertyGuid = r.PropertyGuid";

        var expected =
            "WITH RankedPhones AS (\n" +
            "\tSELECT\n" +
            "\t\tPropertyGuid\n" +
            "\t\t, Phone\n" +
            "\t\t, rn = ROW_NUMBER() OVER (PARTITION BY PropertyGuid ORDER BY Created DESC)\n" +
            "\tFROM Phones\n" +
            ")\n" +
            "\n" +
            ", AggregatedEmails AS (\n" +
            "\tSELECT\n" +
            "\t\tPropertyGuid\n" +
            "\t\t, Email\n" +
            "\tFROM Emails\n" +
            ")\n" +
            "\n" +
            "SELECT\n" +
            "\tr.PropertyGuid\n" +
            "\t, r.Phone\n" +
            "FROM RankedPhones AS r\n" +
            "INNER JOIN AggregatedEmails AS e ON e.PropertyGuid = r.PropertyGuid;";

        Assert.Equal(expected, Format(sql));
    }

    [Fact]
    public void CreateTable_LeadingCommasAndMarginParen()
    {
        var sql = "create table Properties (PropertyGuid uniqueidentifier, PropName nvarchar(200), " +
                  "SourceUrl nvarchar(500))";

        var expected =
            "CREATE TABLE Properties (\n" +
            "\tPropertyGuid UNIQUEIDENTIFIER\n" +
            "\t, PropName NVARCHAR(200)\n" +
            "\t, SourceUrl NVARCHAR(500)\n" +
            ");";

        Assert.Equal(expected, Format(sql));
    }

    [Fact]
    public void NormalizeJoinKeywords_TrimsOuter_KeepsFullOuter()
    {
        var opts = Profile();
        var sql = "select a.Id from A a left outer join B b on a.Id = b.Id " +
                  "right outer join C c on a.Id = c.Id full outer join D d on a.Id = d.Id";
        var result = Format(sql, opts);

        Assert.Contains("LEFT JOIN B", result);
        Assert.Contains("RIGHT JOIN C", result);
        Assert.Contains("FULL OUTER JOIN D", result);
        Assert.DoesNotContain("LEFT OUTER JOIN", result);
        Assert.DoesNotContain("RIGHT OUTER JOIN", result);
    }

    [Fact]
    public void RemoveBrackets_KeepsBracketsOnKeywordColumns()
    {
        var opts = Profile();
        var sql = "select [Name], [Type], [Source], [Order], [CustomerId] from [dbo].[Customers]";
        var result = Format(sql, opts);

        // Keyword-named columns keep their brackets...
        Assert.Contains("[Name]", result);
        Assert.Contains("[Type]", result);
        Assert.Contains("[Source]", result);
        Assert.Contains("[Order]", result);
        // ...plain identifiers lose them.
        Assert.Contains("CustomerId", result);
        Assert.DoesNotContain("[CustomerId]", result);
    }

    [Fact]
    public void WhereConditions_StayIndented_WhenIndentBetweenConditionsTrue()
    {
        var opts = Profile();
        opts.IndentBetweenConditions = true; // default behavior — AND/OR keep ScriptDom indentation
        var sql = "select a.Id from A a where a.X = 1 and a.Y = 2";
        var result = Format(sql, opts);

        // The AND line should NOT be flush with WHERE when indentation is enabled.
        Assert.Contains("WHERE a.X = 1", result);
        Assert.DoesNotContain("\nAND a.Y = 2", result);
    }

    [Fact]
    public void Subquery_And_StringParens_AreNotMangled()
    {
        var opts = Profile();
        var sql = "select o.Id, (select sum(x) from Lines l where l.OrderId = o.Id) as Total " +
                  "from Orders o where o.Note = ')' and o.Other = '('";
        var result = Format(sql, opts);

        Assert.Contains("(SELECT SUM(x)", result);
        Assert.Contains("Total = (SELECT SUM(x)", result); // multi-line subquery alias is now converted
        Assert.DoesNotContain(") AS Total", result);
        Assert.Contains("o.Note = ')'", result);
        Assert.Contains("o.Other = '('", result);
    }

    [Fact]
    public void ColumnEquals_MultiLineSubqueryAlias_IsConverted()
    {
        var opts = Profile();
        var sql = "select o.Id, (select sum(l.Qty) from Lines l where l.OrderId = o.Id) as Total from Orders o";
        var result = Format(sql, opts);

        Assert.Contains("Total = (SELECT SUM(l.Qty)", result);
        Assert.DoesNotContain("AS Total", result);
    }

    [Fact]
    public void ColumnEquals_NestedSubqueryAlias_IsConvertedRecursively()
    {
        var opts = Profile();
        var sql = "select (select p.Name as PersonName from People p where p.Id = o.PersonId) as OwnerName from Orders o";
        var result = Format(sql, opts);

        // Both the outer and the inner alias are rewritten to "alias = expr" form.
        Assert.Contains("OwnerName = (SELECT", result);
        Assert.Contains("PersonName = p.Name", result);
        Assert.DoesNotContain("AS OwnerName", result);
        Assert.DoesNotContain("AS PersonName", result);
    }

    [Fact]
    public void ColumnEquals_CastAsIsNotMistakenForAlias()
    {
        var opts = Profile();
        var sql = "select cast(o.Total as decimal(10,2)) as Amount from Orders o";
        var result = Format(sql, opts);

        Assert.Contains("Amount = CAST", result);   // outer alias rewritten
        Assert.Contains("AS DECIMAL", result);       // inner CAST "AS" preserved
        Assert.DoesNotContain("AS Amount", result);  // alias no longer in AS form
    }
}
