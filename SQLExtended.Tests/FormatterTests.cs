using System;
using SQLExtended.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// Tests for the SQL formatter. Each test formats raw SQL and asserts the output.
/// To add a new test case: copy an existing test, paste your raw SQL into the input,
/// and set the expected output to what you want. Run with: dotnet test --filter "ClassName=FormatterTests"
/// </summary>
public class FormatterTests
{
    private readonly ITestOutputHelper _output;

    public FormatterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Helper: formats SQL with the given options, prints both input and output, and returns the result.
    /// </summary>
    private string FormatAndPrint(string inputSql, FormatterOptions options = null)
    {
        options = options ?? DefaultOptions();
        var service = new SqlFormatterService(options);
        var result = service.Format(inputSql);

        _output.WriteLine("=== INPUT ===");
        _output.WriteLine(inputSql);
        _output.WriteLine("");
        _output.WriteLine("=== FORMATTED ===");
        _output.WriteLine(result.FormattedSql);

        if (!result.Success)
            _output.WriteLine($"[PARSE ERROR: {result.ErrorMessage}]");

        return result.FormattedSql;
    }

    /// <summary>
    /// Default options used by most tests. Adjust as needed.
    /// </summary>
    private static FormatterOptions DefaultOptions() => new FormatterOptions
    {
        KeywordCase = CasingOption.Upper,
        IndentStyle = IndentStyleOption.Spaces,
        IndentSize = 4,
        CommaPosition = CommaPositionOption.LeadingComma,
        SelectColumnLayout = SelectColumnLayoutOption.StackedIndented,
        JoinLayout = JoinLayoutOption.NewLine,
        JoinOnSameLine = false,
        AlignFromAndJoins = true,
        BlankLineBeforeStatement = true,
        BlankLinesBetweenStatements = 1,
        MaxLineWidth = 120,
        InsertColumnsPerLine = 4,
        InsertValuesPerLine = 4,
        AlignColumnDefinitionFields = true,
        TrailingSemicolon = SemicolonOption.Unchanged,
        BracketQuoting = BracketQuotingOption.Unchanged,
        AliasStyle = AliasStyleOption.Unchanged,
    };

    // ───────────────────────────────────────────────
    //  Blank line before statements
    // ───────────────────────────────────────────────

    [Fact]
    public void BlankLine_Before_AlterTable_After_Select()
    {
        var sql =
            "SELECT DISTINCT customer AS CustPN INTO ##zzTemp00_cust FROM ##zzTemp02_order ORDER BY customer;\r\n" +
            "ALTER TABLE ##zzTemp00_cust ADD Name NVARCHAR(500) NULL, DOB SMALLDATETIME NULL;";

        var result = FormatAndPrint(sql);

        // There should be a blank line between the SELECT and ALTER statements
        Assert.Contains("ALTER TABLE", result);
        // Find the ALTER line and check the line before it is blank
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("ALTER", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(string.IsNullOrWhiteSpace(lines[i - 1]),
                    $"Expected blank line before ALTER, but found: [{lines[i - 1]}]");
                break;
            }
        }
    }

    [Fact]
    public void BlankLine_Not_Before_InsertSelect()
    {
        var sql = "INSERT INTO #tmp (Col1, Col2) SELECT Col1, Col2 FROM dbo.Source";

        var result = FormatAndPrint(sql);

        // SELECT is part of INSERT — should NOT have a blank line before it
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Assert.False(string.IsNullOrWhiteSpace(lines[i - 1]),
                    "Should not have blank line before SELECT in INSERT...SELECT");
                break;
            }
        }
    }

    [Fact]
    public void BlankLine_Between_Multiple_Statements()
    {
        var sql =
            "SELECT 1;\r\n" +
            "SELECT 2;\r\n" +
            "UPDATE dbo.T SET Col = 1;\r\n" +
            "DELETE FROM dbo.T;\r\n" +
            "ALTER TABLE dbo.T ADD NewCol INT NULL;";

        var result = FormatAndPrint(sql);

        _output.WriteLine("");
        _output.WriteLine("=== LINE BY LINE ===");
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
            _output.WriteLine($"L{i:D2}: [{lines[i]}]");
    }

    // ───────────────────────────────────────────────
    //  Leading commas with comments
    // ───────────────────────────────────────────────

    [Fact]
    public void LeadingCommas_AlterTable_With_Comments()
    {
        var sql =
            "ALTER TABLE ##zzTemp00_cust\r\n" +
            "ADD Name NVARCHAR(500) NULL, -- from main\r\n" +
            "    DOB SMALLDATETIME NULL, -- from main\r\n" +
            "    Mob NVARCHAR(500) NULL, -- from main\r\n" +
            "    --\r\n" +
            "    ord_financial_customerStatus INT NULL, -- status\r\n" +
            "    --\r\n" +
            "    xDate SMALLDATETIME NULL, -- coalesce\r\n" +
            "    SD1 SMALLDATETIME NULL, -- from shipping\r\n" +
            "    RSD SMALLDATETIME NULL";

        var result = FormatAndPrint(sql);

        // Comments should be on the same line as their column, not on separate lines
        Assert.Contains("-- from main", result);
        Assert.Contains("-- status", result);
        // Verify comments are on a line that also has NULL (same line as column def)
        var allLines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        bool foundCommentOnColumnLine = false;
        foreach (var l in allLines)
        {
            if (l.Contains("NULL") && l.Contains("-- from main"))
            {
                foundCommentOnColumnLine = true;
                break;
            }
        }
        Assert.True(foundCommentOnColumnLine, "Comment '-- from main' should be on the same line as a column definition");
        // Standalone -- dividers should remain on their own lines
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        bool hasDivider = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().TrimEnd() == "--")
            {
                hasDivider = true;
                break;
            }
        }
        Assert.True(hasDivider, "Standalone -- dividers should stay on their own line");
    }

    // ───────────────────────────────────────────────
    //  INSERT wrapping
    // ───────────────────────────────────────────────

    [Fact]
    public void Insert_Columns_Wrap_At_ColumnsPerLine()
    {
        var sql =
            "INSERT INTO #products (PRODUCT_ID, PRODUCT_NAME_NUMBER, PRODUCT_NAME, " +
            "INVENTORY_PACKAGE_PK, ISSUE_PACKAGE_PK, INV_TO_RECIPE_PRIMARY_CONV, " +
            "PRODUCT_TYPE, CATEGORY_NAME, SUBCATEGORY_NAME, MICROCATEGORY_NAME, WEIGHT_UNIT) " +
            "SELECT 1, 'a', 'b', 1, 1, 1.0, 'c', 'd', 'e', 'f', 'kg'";

        var result = FormatAndPrint(sql);

        // With 4 columns per line, the first data line after ( should have at most 4 columns
        Assert.Contains("PRODUCT_ID", result);
        // Should NOT be all on one line
        Assert.DoesNotContain("PRODUCT_ID, PRODUCT_NAME_NUMBER, PRODUCT_NAME, INVENTORY_PACKAGE_PK, ISSUE_PACKAGE_PK", result);
    }

    [Fact]
    public void Insert_ParenthesesOnSameLine()
    {
        var options = DefaultOptions();
        options.InsertParenthesesOnSameLine = true;

        var sql =
            "INSERT INTO #products (PRODUCT_ID, PRODUCT_NAME_NUMBER, PRODUCT_NAME, " +
            "INVENTORY_PACKAGE_PK, ISSUE_PACKAGE_PK) SELECT 1, 'a', 'b', 1, 1";

        var result = FormatAndPrint(sql, options);

        // ( should be on the same line as INSERT INTO
        Assert.Contains("INSERT INTO #products (", result);
    }

    // ───────────────────────────────────────────────
    //  FROM / JOIN alignment
    // ───────────────────────────────────────────────

    [Fact]
    public void From_And_Joins_Aligned()
    {
        var sql =
            "SELECT c.CustomerID, o.OrderID " +
            "FROM dbo.Customers c " +
            "INNER JOIN dbo.Orders o ON c.CustomerID = o.CustomerID " +
            "LEFT JOIN dbo.OrderDetails od ON o.OrderID = od.OrderID";

        var result = FormatAndPrint(sql);

        // FROM and JOIN lines should start at the same indent level
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int? fromIndent = null;
        foreach (var line in lines)
        {
            var stripped = line.TrimStart();
            if (stripped.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase))
                fromIndent = line.Length - stripped.Length;
            if (stripped.StartsWith("INNER JOIN", StringComparison.OrdinalIgnoreCase) ||
                stripped.StartsWith("LEFT JOIN", StringComparison.OrdinalIgnoreCase))
            {
                int joinIndent = line.Length - stripped.Length;
                Assert.Equal(fromIndent, joinIndent);
            }
        }
    }

    // ───────────────────────────────────────────────
    //  Comma alignment
    // ───────────────────────────────────────────────

    [Fact]
    public void LeadingComma_Columns_Aligned()
    {
        var sql = "SELECT col1, col2, col3, col4 FROM dbo.T";

        var result = FormatAndPrint(sql);

        // With leading commas, ", col2" should have the comma 2 spaces back
        // so col2 aligns with col1
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int? firstColPos = null;
        foreach (var line in lines)
        {
            var stripped = line.TrimStart();
            if (stripped.StartsWith(", "))
            {
                // The content after ", " should start at the same position as the first column
                int commaContentStart = line.IndexOf(", ") + 2;
                if (firstColPos != null)
                    Assert.Equal(firstColPos.Value, commaContentStart);
            }
            else if (stripped.StartsWith("col1", StringComparison.OrdinalIgnoreCase))
            {
                firstColPos = line.Length - stripped.Length;
            }
            else if (stripped.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                     stripped.Contains("col1"))
            {
                // col1 is on the SELECT line — find its position
                firstColPos = line.IndexOf("col1", StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.NotNull(firstColPos);
    }

    // ───────────────────────────────────────────────
    //  Blank lines — no semicolons
    // ───────────────────────────────────────────────

    [Fact]
    public void BlankLines_Work_Without_Semicolons()
    {
        var options = DefaultOptions();
        options.TrailingSemicolon = SemicolonOption.Never;
        options.BlankLinesBetweenStatements = 2;

        var sql =
            "SELECT 1;\r\n" +
            "SELECT 2;\r\n" +
            "ALTER TABLE dbo.T ADD Col INT NULL;";

        var result = FormatAndPrint(sql, options);

        // Count consecutive blank lines between statements
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int maxConsecutiveBlanks = 0;
        int currentBlanks = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                currentBlanks++;
            else
            {
                if (currentBlanks > maxConsecutiveBlanks)
                    maxConsecutiveBlanks = currentBlanks;
                currentBlanks = 0;
            }
        }

        Assert.Equal(2, maxConsecutiveBlanks);
    }

    [Fact]
    public void BlankLines_Count_Is_Respected()
    {
        var options = DefaultOptions();
        options.TrailingSemicolon = SemicolonOption.Unchanged;
        options.BlankLinesBetweenStatements = 3;

        var sql = "SELECT 1;\r\nSELECT 2;";

        var result = FormatAndPrint(sql, options);

        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int blankCount = 0;
        bool foundFirst = false;
        foreach (var line in lines)
        {
            if (foundFirst && string.IsNullOrWhiteSpace(line))
                blankCount++;
            else if (foundFirst && !string.IsNullOrWhiteSpace(line))
                break;

            if (line.TrimStart().StartsWith("SELECT 1"))
                foundFirst = true;
        }

        _output.WriteLine($"Blank lines between statements: {blankCount}");
        Assert.Equal(3, blankCount);
    }

    // ───────────────────────────────────────────────
    //  Semicolon placement
    // ───────────────────────────────────────────────

    [Fact]
    public void Semicolon_Before_InlineComment_Not_After()
    {
        var sql =
            "UPDATE d SET d.CustPN = o.customer " +
            "FROM ##zzTemp02_order o " +
            "INNER JOIN ##zzTemp01_odet d ON d.orderid = o.orderId -- customers";

        var options = DefaultOptions();
        options.TrailingSemicolon = SemicolonOption.Always;

        var result = FormatAndPrint(sql, options);

        // Semicolon should appear BEFORE the comment, not after
        var lines = result.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.Contains("-- customers"))
            {
                _output.WriteLine($"Comment line: [{line}]");
                // Should NOT end with ";", the ; should be before the --
                Assert.DoesNotMatch(@"--.*;\s*$", line);
                // Should have ; before the --
                Assert.Matches(@";\s*--", line);
                break;
            }
        }
    }

    [Fact]
    public void AliasStyle_ColumnEquals_RewritesAsAliasToEqualsForm_CRLF()
    {
        // Reproduces the bug where CRLF line endings caused only the last item
        // (which was already in `col = expr` form) to look correct.
        var sql =
            "SELECT '' AS Title\r\n" +
            "     , NULL AS Body\r\n" +
            "     , 0 AS DisplayOrder\r\n" +
            "     , DeletedBy = 0;\r\n";

        var opts = DefaultOptions();
        opts.AliasStyle = AliasStyleOption.ColumnEquals;

        var result = FormatAndPrint(sql, opts);

        Assert.Contains("Title = ''", result);
        Assert.Contains("Body = NULL", result);
        Assert.Contains("DisplayOrder = 0", result);
        Assert.Contains("DeletedBy = 0", result);
        Assert.DoesNotContain(" AS Title", result);
        Assert.DoesNotContain(" AS Body", result);
        Assert.DoesNotContain(" AS DisplayOrder", result);
    }

    [Fact]
    public void AliasStyle_ColumnEquals_LeavesTableAliasesAlone()
    {
        var sql = "SELECT c.Name AS CustomerName FROM dbo.Customers AS c;";

        var opts = DefaultOptions();
        opts.AliasStyle = AliasStyleOption.ColumnEquals;

        var result = FormatAndPrint(sql, opts);

        Assert.Contains("CustomerName = c.Name", result);
        // Table alias on FROM must not be touched
        Assert.Contains("AS c", result);
    }

    // ───────────────────────────────────────────────
    //  Add your test cases below
    // ───────────────────────────────────────────────

    [Fact]
    public void Sandbox_PasteYourSqlHere()
    {
        // Paste your raw SQL between the quotes and run:
        //   dotnet test --filter "Sandbox_PasteYourSqlHere" -v n
        var sql = "SELECT 1";

        var result = FormatAndPrint(sql);

        // Add assertions or just inspect the output
        Assert.NotNull(result);
    }
}
