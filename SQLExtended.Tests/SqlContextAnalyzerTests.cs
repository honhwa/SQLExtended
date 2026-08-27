using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

public class SqlContextAnalyzerTests
{
    [Theory]
    [InlineData("SELECT * FROM ")]
    [InlineData("SELECT * FROM dbo.Orders o INNER JOIN ")]
    [InlineData("INSERT INTO ")]
    [InlineData("UPDATE ")]
    [InlineData("DELETE FROM ")]
    [InlineData("SELECT * FROM MyDb.dbo.")]          // three-part: database.schema.<object>
    [InlineData("SELECT * FROM MyDb.dbo.Cust")]      // three-part with partial object
    [InlineData("SELECT * FROM MyDb.")]              // database.<schema-or-object>
    public void Analyze_TableNameContext_ReturnsTableName(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, result.Type);
    }

    [Theory]
    [InlineData("SELECT * FROM ", new string[0])]
    [InlineData("SELECT * FROM Cust", new string[0])]
    [InlineData("SELECT * FROM dbo.", new[] { "dbo" })]
    [InlineData("SELECT * FROM dbo.Cust", new[] { "dbo" })]
    [InlineData("SELECT * FROM MyDb.dbo.", new[] { "MyDb", "dbo" })]
    [InlineData("SELECT * FROM MyDb.dbo.Cust", new[] { "MyDb", "dbo" })]
    [InlineData("SELECT * FROM [My Db].[dbo].", new[] { "My Db", "dbo" })]
    public void GetQualifierParts_ReturnsTypedPrefixSegments(string textBefore, string[] expected)
    {
        var parts = SqlCompletionContext.GetQualifierParts(textBefore);
        Assert.Equal(expected, parts.ToArray());
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders", 8, null, 1)]                 // bare star, cursor after '*'
    [InlineData("SELECT TOP 10 * FROM dbo.Orders", 15, null, 1)]         // TOP N
    [InlineData("SELECT DISTINCT * FROM dbo.Orders", 17, null, 1)]       // DISTINCT
    [InlineData("SELECT o.* FROM dbo.Orders o", 10, "o", 3)]             // alias.*
    [InlineData("SELECT [o].* FROM dbo.Orders o", 12, "o", 5)]           // bracketed alias
    [InlineData("SELECT #tmp.* FROM #tmp", 13, "#tmp", 6)]               // temp table
    [InlineData("SELECT a, * FROM dbo.Orders", 11, null, 1)]             // after list comma
    [InlineData("SELECT a, t.* FROM dbo.Orders t", 13, "t", 3)]          // alias.* after comma
    public void Analyze_CursorAfterSelectStar_ReturnsStarExpansion(string sql, int cursor, string expectedPrefix, int expectedLength)
    {
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.StarExpansion, result.Type);
        Assert.Equal(expectedPrefix, result.DotPrefix);
        Assert.Equal(expectedLength, result.StarReplaceLength);
    }

    [Theory]
    [InlineData("SELECT COUNT(*", 14)]                                   // COUNT(*) — not a select-list star
    [InlineData("SELECT price *", 14)]                                   // multiplication
    [InlineData("SELECT *", 8)]                                          // no FROM clause in statement
    [InlineData("SELECT * FROM dbo.Orders", 9)]                          // cursor past the star (on the space)
    public void Analyze_NotASelectListStar_DoesNotReturnStarExpansion(string sql, int cursor)
    {
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.StarExpansion, result.Type);
    }

    [Fact]
    public void Analyze_InsideLineComment_ReturnsNone()
    {
        string sql = "SELECT * FROM dbo.Orders -- pick the ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.None, result.Type);
    }

    [Fact]
    public void Analyze_AfterLineCommentOnNextLine_ResumesCompletion()
    {
        string sql = "-- a comment\nSELECT * FROM ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, result.Type);
    }

    [Fact]
    public void Analyze_InsideBlockComment_ReturnsNone()
    {
        string sql = "SELECT * FROM /* choose a ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.None, result.Type);
    }

    [Fact]
    public void Analyze_AfterClosedBlockComment_ResumesCompletion()
    {
        string sql = "/* header */ SELECT * FROM ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, result.Type);
    }

    [Fact]
    public void Analyze_CommentMarkerInsideString_NotTreatedAsComment()
    {
        string sql = "SELECT '--not a comment' FROM ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, result.Type);
    }

    [Fact]
    public void Analyze_AfterAliasDot_ReturnsColumnAfterDot()
    {
        string sql = "SELECT c. FROM dbo.Customers c";
        // Cursor is right after "c." at position 9
        int cursor = sql.IndexOf("c.") + 2;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnAfterDot, result.Type);
        Assert.Equal("c", result.DotPrefix);
    }

    [Theory]
    [InlineData("SELECT #t. FROM #t", "#t.", "#t")]            // local temp table
    [InlineData("SELECT ##g. FROM ##g", "##g.", "##g")]        // global temp table
    [InlineData("SELECT @v. FROM @v v", "@v.", "@v")]          // table variable
    public void Analyze_AfterLocalTableDot_KeepsSigilInPrefix(string sql, string dotToken, string expectedPrefix)
    {
        int cursor = sql.IndexOf(dotToken) + dotToken.Length;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnAfterDot, result.Type);
        Assert.Equal(expectedPrefix, result.DotPrefix);
    }

    [Fact]
    public void Analyze_AfterTableNameDot_ReturnsColumnAfterDot()
    {
        // When there's a FROM clause and the dot is after an alias/table ref in SELECT
        string sql = "SELECT Customers. FROM dbo.Customers";
        int cursor = sql.IndexOf("Customers.") + "Customers.".Length;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnAfterDot, result.Type);
        Assert.Equal("Customers", result.DotPrefix);
    }

    [Fact]
    public void Analyze_InSelectList_ReturnsColumnInContext()
    {
        string sql = "SELECT  FROM dbo.Customers c";
        // Cursor is at position 7 (after "SELECT ")
        var result = SqlContextAnalyzer.Analyze(sql, 7);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_AfterSelectComma_ReturnsColumnInContext()
    {
        string sql = "SELECT c.Name, FROM dbo.Customers c";
        int cursor = sql.IndexOf(", ") + 2;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_InWhereClause_ReturnsColumnInContext()
    {
        string sql = "SELECT * FROM dbo.Customers c WHERE ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_AfterAnd_ReturnsColumnInContext()
    {
        string sql = "SELECT * FROM dbo.Customers c WHERE c.Name = 'test' AND ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_InOrderBy_ReturnsColumnInContext()
    {
        string sql = "SELECT * FROM dbo.Customers c ORDER BY ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_InGroupBy_ReturnsColumnInContext()
    {
        string sql = "SELECT * FROM dbo.Customers c GROUP BY ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_InOnClause_ReturnsJoinOnCondition()
    {
        string sql = "SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.JoinOnCondition, result.Type);
        // The just-joined table's alias is captured for FK pairing.
        Assert.Equal("o", result.JoinedTableReference);
    }

    [Fact]
    public void Analyze_InOnClause_NoAlias_CapturesTableName()
    {
        string sql = "SELECT * FROM dbo.Customers JOIN dbo.Orders ON ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.JoinOnCondition, result.Type);
        Assert.Equal("Orders", result.JoinedTableReference);
    }

    [Fact]
    public void Analyze_InOnClause_PartialTyping_ReturnsJoinOnCondition()
    {
        string sql = "SELECT * FROM dbo.Customers c INNER JOIN dbo.Orders o ON Cust";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.JoinOnCondition, result.Type);
        Assert.Equal("o", result.JoinedTableReference);
    }

    [Fact]
    public void Analyze_SecondJoinOn_BindsToNearestJoin()
    {
        string sql = "SELECT * FROM A a JOIN B b ON a.Id = b.AId JOIN C c ON ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.JoinOnCondition, result.Type);
        Assert.Equal("c", result.JoinedTableReference);
    }

    [Fact]
    public void Analyze_AfterJoinTable_OffersOnKeyword()
    {
        // Typing "ON" right after a joined table: the keyword context must include
        // AfterJoin so "ON" is offered (and exact-matched), rather than the item
        // manager hard-selecting an unrelated first item.
        string before = "SELECT * FROM A a JOIN B b ";
        var ctx = SqlKeywords.DetectContext(before);
        Assert.True((ctx & KeywordContext.AfterJoin) != 0);
        var keywords = SqlKeywords.GetKeywordsForContext(ctx);
        Assert.Contains(keywords, k => k.Text == "ON");
    }

    [Fact]
    public void Analyze_AfterEquals_ReturnsColumnInContext()
    {
        string sql = "SELECT * FROM dbo.Customers c WHERE c.ID = ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_NoContext_ReturnsKeyword()
    {
        string sql = "BEGIN ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.Keyword, result.Type);
    }

    [Fact]
    public void Analyze_EmptyInput_ReturnsNone()
    {
        var result = SqlContextAnalyzer.Analyze("", 0);
        Assert.Equal(SqlContextAnalyzer.CompletionType.None, result.Type);
    }

    [Fact]
    public void Analyze_SchemaDoInFromContext_ReturnsTableNotColumn()
    {
        // "FROM dbo." should be table completion, not column completion
        string sql = "SELECT * FROM dbo.";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        // Should be TableName (handled by table completion with schema prefix)
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.ColumnAfterDot, result.Type);
    }

    [Fact]
    public void Analyze_StatementExtraction_HandlesBatchSeparator()
    {
        string sql = "SELECT 1\r\nGO\r\nSELECT  FROM dbo.Customers c";
        // Cursor in second statement after "SELECT "
        int cursor = sql.IndexOf("SELECT  FROM") + 7;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
        // Statement text should only contain the second statement
        Assert.DoesNotContain("GO", result.StatementText);
    }

    [Fact]
    public void Analyze_SelectDistinct_ReturnsColumnInContext()
    {
        string sql = "SELECT DISTINCT  FROM dbo.Customers";
        int cursor = sql.IndexOf("DISTINCT ") + "DISTINCT ".Length;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_SelectTopN_ReturnsColumnInContext()
    {
        string sql = "SELECT TOP 10  FROM dbo.Customers";
        int cursor = sql.IndexOf("10 ") + 3;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    // --- Phase 4: Stored Procedure context tests ---

    [Theory]
    [InlineData("EXEC ")]
    [InlineData("EXECUTE ")]
    [InlineData("exec ")]
    [InlineData("execute ")]
    public void Analyze_AfterExec_ReturnsProcedureName(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ProcedureName, result.Type);
    }

    [Fact]
    public void Analyze_ExecWithPartialName_ReturnsProcedureName()
    {
        string sql = "EXEC usp_";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ProcedureName, result.Type);
    }

    [Fact]
    public void Analyze_ExecWithSchemaPrefix_ReturnsProcedureName()
    {
        string sql = "EXEC dbo.";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        // EXEC is in TableContextBeforeDot, so dot check is skipped.
        // Then ProcedureContextPattern matches "EXEC dbo." → ProcedureName
        Assert.Equal(SqlContextAnalyzer.CompletionType.ProcedureName, result.Type);
    }

    [Fact]
    public void Analyze_ExecWithSchemaAndPartialName_ReturnsProcedureName()
    {
        string sql = "EXEC dbo.usp_Get";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ProcedureName, result.Type);
    }

    // --- Phase 4: Function context tests ---
    // Functions in SELECT/WHERE with schema prefix are detected as ColumnAfterDot.
    // The completion source then resolves: if the prefix is not an alias, show functions.

    [Fact]
    public void Analyze_FunctionInSelect_ReturnsColumnAfterDot()
    {
        string sql = "SELECT dbo.";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnAfterDot, result.Type);
        Assert.Equal("dbo", result.DotPrefix);
    }

    [Fact]
    public void Analyze_FunctionInWhere_ReturnsColumnAfterDot()
    {
        string sql = "SELECT * FROM dbo.Customers WHERE dbo.";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnAfterDot, result.Type);
        Assert.Equal("dbo", result.DotPrefix);
    }

    // --- USE <database> context ---

    [Theory]
    [InlineData("USE ")]
    [InlineData("use ")]
    [InlineData("USE\n")]
    [InlineData("GO\nUSE ")]
    [InlineData("USE Mas")]
    [InlineData("USE [")]
    public void Analyze_AfterUse_ReturnsDatabaseName(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.DatabaseName, result.Type);
    }

    [Fact]
    public void Analyze_UseInsideIdentifier_DoesNotReturnDatabaseName()
    {
        // "USE" embedded in a longer word should not trigger database completion.
        string sql = "SELECT MISUSE";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.DatabaseName, result.Type);
    }

    // --- Statement boundary detection: prevents tables from one statement leaking
    //     into completion for the next when there's no ';' or GO between them. ---

    [Fact]
    public void Analyze_UpdateAfterSelectNoSemicolon_StatementContainsOnlyUpdate()
    {
        // SELECT above the UPDATE has no trailing semicolon. Previously both
        // statements were treated as one, and the SELECT's table leaked into
        // column completion for the UPDATE.
        string sql = "SELECT * FROM dbo.Clients\r\nUPDATE dbo.ClientUsers SET ClientId = 86 WHERE Email = 'x' AND ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
        Assert.DoesNotContain("Clients", result.StatementText);
        Assert.Contains("ClientUsers", result.StatementText);
    }

    [Fact]
    public void Analyze_SelectAfterDeleteNoSemicolon_StatementContainsOnlySelect()
    {
        string sql = "DELETE FROM dbo.Logs WHERE ID < 10\r\nSELECT  FROM dbo.Customers";
        int cursor = sql.IndexOf("SELECT ") + "SELECT ".Length;
        var result = SqlContextAnalyzer.Analyze(sql, cursor);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
        Assert.DoesNotContain("Logs", result.StatementText);
        Assert.Contains("Customers", result.StatementText);
    }

    [Fact]
    public void Analyze_SubquerySelect_DoesNotSplitStatement()
    {
        // SELECT inside parens is a subquery, not a new statement boundary.
        string sql = "SELECT * FROM dbo.Customers c WHERE c.ID IN (\r\nSELECT CustomerID FROM dbo.Orders\r\n) AND ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
        // Both tables must remain in scope for an AND clause referring to either
        Assert.Contains("Customers", result.StatementText);
        Assert.Contains("Orders", result.StatementText);
    }

    // --- COLLATE context ---

    [Theory]
    [InlineData("SELECT Name FROM dbo.Customers ORDER BY Name COLLATE ")]
    [InlineData("SELECT Name FROM dbo.Customers ORDER BY Name COLLATE Latin1_Gen")]           // partial name re-trigger
    [InlineData("SELECT * FROM dbo.Customers WHERE Name = @p COLLATE ")]
    [InlineData("ALTER TABLE dbo.Customers ALTER COLUMN Name varchar(50) COLLATE ")]
    [InlineData("CREATE TABLE #t (Name varchar(50) COLLATE ")]
    [InlineData("select name collate ")]                                                       // case-insensitive
    public void Analyze_AfterCollate_ReturnsCollationName(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.CollationName, result.Type);
    }

    [Fact]
    public void Analyze_CollateWordWithoutTrailingSpace_IsNotCollationContext()
    {
        // Still typing the COLLATE keyword itself — no collation list yet.
        string sql = "SELECT Name FROM dbo.Customers ORDER BY Name COLLATE";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.CollationName, result.Type);
    }

    // --- Cross-database references ---

    [Theory]
    [InlineData("SELECT * FROM [DataBaseName].dbo.")]        // bracketed db containing a dot
    [InlineData("SELECT * FROM OtherDb.dbo.")]
    [InlineData("SELECT * FROM [DataBaseName].dbo.Proj")]    // partial table typed
    public void Analyze_DotAfterDatabaseQualifiedSchema_ReturnsTableName(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, result.Type);
    }

    [Fact]
    public void Analyze_WhereAfterCrossDatabaseFrom_ReturnsColumnInContext()
    {
        string sql = "SELECT * \r\nFROM [DataBaseName].dbo.Projects\r\nWHERE ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnInContext, result.Type);
    }

    [Fact]
    public void Analyze_OnAfterCrossDatabaseJoin_ReturnsJoinOnCondition()
    {
        string sql = "SELECT * FROM dbo.Customers c JOIN [DataBaseName].dbo.Projects p ON ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.JoinOnCondition, result.Type);
        Assert.Equal("p", result.JoinedTableReference);
    }

    [Fact]
    public void Analyze_InsertIntoCrossDatabaseTable_CapturesDatabase()
    {
        string sql = "INSERT INTO [DataBaseName].dbo.Projects ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.InsertColumnTemplate, result.Type);
        Assert.Equal("DataBaseName", result.TargetDatabase);
        Assert.Equal("dbo", result.TargetSchema);
        Assert.Equal("Projects", result.TargetTable);
    }

    [Fact]
    public void Analyze_InsertIntoTwoPartTable_DatabaseIsNull()
    {
        string sql = "INSERT INTO dbo.Customers ";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.InsertColumnTemplate, result.Type);
        Assert.Null(result.TargetDatabase);
        Assert.Equal("dbo", result.TargetSchema);
        Assert.Equal("Customers", result.TargetTable);
    }
}
