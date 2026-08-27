using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

public class AliasResolverTests
{
    [Fact]
    public void Resolve_SimpleFromWithAlias_ReturnsAliasMapping()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers c");
        Assert.Single(tables);
        Assert.Equal("dbo", tables[0].Schema);
        Assert.Equal("Customers", tables[0].Table);
        Assert.Equal("c", tables[0].Alias);
    }

    [Fact]
    public void Resolve_FromWithAsAlias_ReturnsAliasMapping()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers AS c");
        Assert.Single(tables);
        Assert.Equal("c", tables[0].Alias);
    }

    [Fact]
    public void Resolve_NoAlias_ReturnsTableNameOnly()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers");
        Assert.Single(tables);
        Assert.Equal("Customers", tables[0].Table);
        Assert.Null(tables[0].Alias);
    }

    [Fact]
    public void Resolve_MultipleJoins_ReturnsAllTables()
    {
        string sql = @"
            SELECT c.Name, o.OrderDate, p.ProductName
            FROM dbo.Customers c
            INNER JOIN dbo.Orders o ON o.CustomerID = c.CustomerID
            LEFT JOIN dbo.Products p ON p.ProductID = o.ProductID";

        var tables = AliasResolver.Resolve(sql);
        Assert.Equal(3, tables.Count);

        Assert.Equal("Customers", tables[0].Table);
        Assert.Equal("c", tables[0].Alias);

        Assert.Equal("Orders", tables[1].Table);
        Assert.Equal("o", tables[1].Alias);

        Assert.Equal("Products", tables[2].Table);
        Assert.Equal("p", tables[2].Alias);
    }

    [Fact]
    public void Resolve_NoSchemaPrefix_DefaultsToNull()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM Customers c");
        Assert.Single(tables);
        Assert.Null(tables[0].Schema);
        Assert.Equal("Customers", tables[0].Table);
    }

    [Fact]
    public void FindByIdentifier_MatchesAlias()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers c");
        var found = AliasResolver.FindByIdentifier(tables, "c");
        Assert.NotNull(found);
        Assert.Equal("Customers", found.Table);
    }

    [Fact]
    public void FindByIdentifier_MatchesTableName()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers");
        var found = AliasResolver.FindByIdentifier(tables, "Customers");
        Assert.NotNull(found);
        Assert.Equal("dbo", found.Schema);
    }

    [Fact]
    public void FindByIdentifier_CaseInsensitive()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers C");
        var found = AliasResolver.FindByIdentifier(tables, "c");
        Assert.NotNull(found);
    }

    [Fact]
    public void FindByIdentifier_NotFound_ReturnsNull()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers c");
        var found = AliasResolver.FindByIdentifier(tables, "x");
        Assert.Null(found);
    }

    [Fact]
    public void Resolve_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(AliasResolver.Resolve(""));
        Assert.Empty(AliasResolver.Resolve(null));
    }

    [Fact]
    public void Resolve_IncompleteSQL_DoesNotThrow()
    {
        // Incomplete SQL should not throw — returns whatever it can parse
        var tables = AliasResolver.Resolve("SELECT c. FROM WHERE");
        Assert.NotNull(tables); // May be empty, but should not throw
    }

    [Fact]
    public void Resolve_TrailingWhereClause_StillFindsTable()
    {
        // Complete FROM clause with trailing incomplete WHERE
        var tables = AliasResolver.Resolve("SELECT * FROM Customers c WHERE c.ID > ");
        Assert.True(tables.Count >= 1, "Should find at least one table");
    }

    [Fact]
    public void Resolve_BracketedNames_ReturnsUnbracketedValues()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM [dbo].[Customer Data] cd");
        Assert.Single(tables);
        Assert.Equal("dbo", tables[0].Schema);
        Assert.Equal("Customer Data", tables[0].Table);
        Assert.Equal("cd", tables[0].Alias);
    }

    [Fact]
    public void ReferenceName_ReturnsAliasWhenPresent()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers c");
        Assert.Equal("c", tables[0].ReferenceName);
    }

    [Fact]
    public void ReferenceName_ReturnsTableNameWhenNoAlias()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers");
        Assert.Equal("Customers", tables[0].ReferenceName);
    }

    [Fact]
    public void Resolve_IncompleteUpdate_FindsTargetViaRegexFallback()
    {
        // ScriptDom can't fully parse an UPDATE with a dangling AND, so the regex
        // fallback must still find the target table — otherwise column completion
        // produces nothing after typing "SET" or "AND".
        string sql = "UPDATE dbo.ClientUsers SET ClientId = 86 WHERE Email LIKE '%x%' AND ";
        var tables = AliasResolver.Resolve(sql);
        Assert.Single(tables);
        Assert.Equal("dbo", tables[0].Schema);
        Assert.Equal("ClientUsers", tables[0].Table);
    }

    [Fact]
    public void Resolve_IncompleteDelete_FindsTargetViaRegexFallback()
    {
        var tables = AliasResolver.Resolve("DELETE FROM dbo.Orders WHERE ");
        Assert.True(tables.Count >= 1, $"Expected at least 1 table, got {tables.Count}");
        Assert.Equal("Orders", tables[0].Table);
    }

    [Fact]
    public void Resolve_IncompleteMerge_FindsTargetViaRegexFallback()
    {
        var tables = AliasResolver.Resolve("MERGE INTO dbo.Target AS t USING ");
        Assert.True(tables.Count >= 1);
        Assert.Equal("Target", tables[0].Table);
    }

    [Fact]
    public void Resolve_ThreePartName_CapturesDatabase()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM OtherDb.dbo.Projects p");
        Assert.Single(tables);
        Assert.Equal("OtherDb", tables[0].Database);
        Assert.Equal("dbo", tables[0].Schema);
        Assert.Equal("Projects", tables[0].Table);
        Assert.Equal("p", tables[0].Alias);
    }

    [Fact]
    public void Resolve_TwoPartName_DatabaseIsNull()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM dbo.Customers");
        Assert.Single(tables);
        Assert.Null(tables[0].Database);
    }

    [Fact]
    public void Resolve_BracketedDatabaseWithDot_CapturesDatabase()
    {
        var tables = AliasResolver.Resolve("SELECT * FROM [DataBaseName].dbo.Projects WHERE ");
        Assert.True(tables.Count >= 1);
        Assert.Equal("DataBaseName", tables[0].Database);
        Assert.Equal("dbo", tables[0].Schema);
        Assert.Equal("Projects", tables[0].Table);
    }

    [Fact]
    public void Resolve_CrossDatabaseUpdate_FindsTargetViaRegexFallback()
    {
        // Malformed enough that ScriptDom fails — the regex fallback must carry the database too.
        string sql = "UPDATE [DataBaseName].dbo.ClientUsers SET ClientId = 86 WHERE Email LIKE '%x%' AND ";
        var tables = AliasResolver.Resolve(sql);
        Assert.True(tables.Count >= 1);
        Assert.Equal("DataBaseName", tables[0].Database);
        Assert.Equal("dbo", tables[0].Schema);
        Assert.Equal("ClientUsers", tables[0].Table);
    }

    [Fact]
    public void Resolve_CrossDatabaseJoin_CapturesBothTables()
    {
        string sql = "SELECT * FROM dbo.Customers c JOIN [DataBaseName].dbo.Projects p ON ";
        var tables = AliasResolver.Resolve(sql);
        Assert.Equal(2, tables.Count);
        Assert.Null(tables[0].Database);
        Assert.Equal("DataBaseName", tables[1].Database);
        Assert.Equal("Projects", tables[1].Table);
        Assert.Equal("p", tables[1].Alias);
    }
}
