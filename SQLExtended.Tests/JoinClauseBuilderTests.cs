using SQLExtended.Cache.Models;
using SQLExtended.IntelliSense;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// Tests for <c>JoinClauseBuilder</c> — the whole-clause suggestions offered at "JOIN &lt;here&gt;".
///
/// Everything pinned here fails silently in the editor. A predicate built the wrong way round still
/// parses and still runs, returning the wrong rows; an alias reused from the enclosing query turns the
/// join into an accidental self-join that also runs; a composite key paired out of order joins on the
/// wrong column of the right table. None of the three reaches an error message, so none of them can be
/// caught by using the feature — only by asserting the string.
/// </summary>
public class JoinClauseBuilderTests
{
    private static AliasResolver.TableReference Ref(string table, string alias = null, string schema = "dbo") =>
        new() { Schema = schema, Table = table, Alias = alias };

    private static CachedForeignKey Fk(
        string schema, string table, string cols, string refSchema, string refTable, string refCols, string name = "FK_Test") =>
        new()
        {
            SchemaName = schema,
            TableName = table,
            ForeignKeyName = name,
            Columns = cols,
            ReferencedSchema = refSchema,
            ReferencedTable = refTable,
            ReferencedColumns = refCols
        };

    private static IReadOnlyList<JoinClauseBuilder.JoinSuggestion> Build(
        IReadOnlyList<JoinClauseBuilder.Relation> relations,
        IReadOnlyList<AliasResolver.TableReference> scope,
        bool qualify = true) =>
        JoinClauseBuilder.Build(relations, scope, qualify);

    /// <summary>
    /// The worked example from the feature request: FROM [Order] o JOIN — a key declared on Order
    /// (→ Customer) and a key declared on OrderItem pointing back at Order both have to appear, because
    /// the outgoing keys alone cannot see OrderItem at all.
    /// </summary>
    [Fact]
    public void Build_OffersBothDirections()
    {
        var order = Ref("Order", "o");
        var relations = new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "CustomerId", "dbo", "Customer", "Id") },
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.ToScope,
                    Fk = Fk("dbo", "OrderItem", "OrderId", "dbo", "Order", "Id") },
        };

        var results = Build(relations, new[] { order });

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.InsertText == "dbo.Customer c ON c.Id = o.CustomerId");
        Assert.Contains(results, r => r.InsertText == "dbo.OrderItem oi ON oi.OrderId = o.Id");
    }

    /// <summary>
    /// The new table's alias leads the predicate in both directions. Swapping sides with the direction of
    /// the key produces SQL that is equally valid and reads inconsistently in the list, which is exactly
    /// the kind of difference nothing downstream would report.
    /// </summary>
    [Fact]
    public void Build_PutsTheNewAliasFirstInBothDirections()
    {
        var order = Ref("Order", "o");

        var outgoing = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "CustomerId", "dbo", "Customer", "Id") },
        }, new[] { order });

        var incoming = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.ToScope,
                    Fk = Fk("dbo", "OrderItem", "OrderId", "dbo", "Order", "Id") },
        }, new[] { order });

        Assert.StartsWith("c.", outgoing.Single().Predicate);
        Assert.StartsWith("oi.", incoming.Single().Predicate);
    }

    [Theory]
    [InlineData("Customer", "c")]
    [InlineData("OrderItem", "oi")]
    [InlineData("OrderStatus", "os")]
    [InlineData("ORDER_ITEM", "oi")]
    [InlineData("order item", "oi")]
    [InlineData("SalesOrderHeaderDetail", "sohd")]
    public void MakeAlias_InitialsEachWord(string table, string expected)
    {
        Assert.Equal(expected, JoinClauseBuilder.MakeAlias(table, new HashSet<string>()));
    }

    /// <summary>
    /// An alias colliding with one already in the query is the failure that still runs: the predicate
    /// binds to the enclosing table instead of the joined one.
    /// </summary>
    [Fact]
    public void MakeAlias_AvoidsAliasesAlreadyInScope()
    {
        var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "c" };
        Assert.Equal("c2", JoinClauseBuilder.MakeAlias("Customer", taken));
    }

    [Fact]
    public void Build_DoesNotReuseAnAliasFromTheEnclosingQuery()
    {
        // "FROM dbo.Contact c JOIN " — Customer would initial to "c", which is already the Contact alias.
        var contact = Ref("Contact", "c");
        var results = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = contact, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Contact", "CustomerId", "dbo", "Customer", "Id") },
        }, new[] { contact });

        Assert.Equal("c2", results.Single().Alias);
        Assert.Equal("dbo.Customer c2 ON c2.Id = c.CustomerId", results.Single().InsertText);
    }

    /// <summary>
    /// InventoryStock initials to "is". "JOIN dbo.InventoryStock is ON ..." does not parse, and the
    /// parser blames the punctuation beside it rather than the alias.
    /// </summary>
    [Fact]
    public void MakeAlias_SkipsReservedWords()
    {
        string alias = JoinClauseBuilder.MakeAlias("InventoryStock", new HashSet<string>());
        Assert.NotEqual("is", alias);
        Assert.Equal("is2", alias);
    }

    [Fact]
    public void Build_PairsCompositeKeysPositionallyAndJoinsWithAnd()
    {
        var line = Ref("OrderLine", "ol");
        var results = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = line, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "OrderLine", "CompanyId, OrderId", "dbo", "Order", "CompanyId, Id") },
        }, new[] { line });

        Assert.Equal("dbo.[Order] o ON o.CompanyId = ol.CompanyId AND o.Id = ol.OrderId", results.Single().InsertText);
    }

    /// <summary>
    /// Two keys to the same table are alternatives — only one is ever committed — so they share the
    /// table's alias rather than being handed separate ones, which would read as if both could be taken.
    /// </summary>
    [Fact]
    public void Build_SharesOneAliasAcrossKeysToTheSameTable()
    {
        var order = Ref("Order", "o");
        var results = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "BillToAddressId", "dbo", "Address", "Id", "FK_Bill") },
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "ShipToAddressId", "dbo", "Address", "Id", "FK_Ship") },
        }, new[] { order });

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("a", r.Alias));
        Assert.Contains(results, r => r.InsertText == "dbo.Address a ON a.Id = o.BillToAddressId");
        Assert.Contains(results, r => r.InsertText == "dbo.Address a ON a.Id = o.ShipToAddressId");

        // The key names are what tell the two apart once they are on screen.
        Assert.Equal(new[] { "FK_Bill", "FK_Ship" }, results.Select(r => r.ForeignKeyName).OrderBy(x => x).ToArray());
    }

    /// <summary>
    /// A table with no alias is referenced by its name, which is what <c>ReferenceName</c> falls back to.
    /// </summary>
    [Fact]
    public void Build_UsesTheTableNameWhenTheScopeTableHasNoAlias()
    {
        var order = Ref("Order");
        var results = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "CustomerId", "dbo", "Customer", "Id") },
        }, new[] { order });

        Assert.Equal("dbo.Customer c ON c.Id = Order.CustomerId", results.Single().InsertText);
    }

    /// <summary>
    /// Names with spaces and reserved words are bracketed — the alias never is, and a non-reserved
    /// keyword like Date is deliberately left bare (see the quoting notes: bracketing every keyword
    /// would bracket a large share of ordinary column names).
    /// </summary>
    [Fact]
    public void Build_QuotesNamesThatNeedIt()
    {
        var order = Ref("Order", "o");
        var results = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "Ship Date", "dbo", "Calendar Day", "Key") },
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "PostedOn", "dbo", "Calendar", "Date") },
        }, new[] { order });

        Assert.Contains(results, r => r.InsertText == "dbo.[Calendar Day] cd ON cd.[Key] = o.[Ship Date]");
        Assert.Contains(results, r => r.InsertText == "dbo.Calendar c ON c.Date = o.PostedOn");
    }

    [Fact]
    public void Build_OmitsTheSchemaWhenNotQualifying()
    {
        var order = Ref("Order", "o");
        var results = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "CustomerId", "dbo", "Customer", "Id") },
        }, new[] { order }, qualify: false);

        Assert.Equal("Customer c ON c.Id = o.CustomerId", results.Single().InsertText);
    }

    /// <summary>
    /// A key with no columns on one side would otherwise be emitted as "a. = b.", which does not parse.
    /// </summary>
    [Fact]
    public void Build_DropsAKeyWithNoColumns()
    {
        var order = Ref("Order", "o");
        var results = Build(new List<JoinClauseBuilder.Relation>
        {
            new() { ScopeTable = order, Direction = JoinClauseBuilder.FkDirection.FromScope,
                    Fk = Fk("dbo", "Order", "", "dbo", "Customer", "Id") },
        }, new[] { order });

        Assert.Empty(results);
    }

    [Fact]
    public void Build_ReturnsNothingWithoutRelations()
    {
        Assert.Empty(Build(new List<JoinClauseBuilder.Relation>(), new[] { Ref("Order", "o") }));
        Assert.Empty(JoinClauseBuilder.Build(null, new[] { Ref("Order", "o") }, true));
    }
}
