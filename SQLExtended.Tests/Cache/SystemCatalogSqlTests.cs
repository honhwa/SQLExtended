using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLExtended.Cache;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace SQLExtended.Tests.Cache;

/// <summary>
/// Pins the catalog read behind <c>sys.</c> completion.
///
/// <para>Every failure mode of this query is silent. <c>SystemCatalogCache</c> catches the exception,
/// memoises the server as failed and offers nothing — which on screen is indistinguishable from a
/// login that cannot read the catalog, or from the load simply not having finished. So the things
/// worth asserting are the ones a reader cannot check by looking: that it parses at all, that it
/// still returns the two result sets the reader steps through in order, and that it asks for the
/// "all_" views rather than the ones that exclude system objects.</para>
///
/// <para>Parsing cannot tell whether a column exists on a given release. Nothing here has been run
/// against a live instance.</para>
/// </summary>
public class SystemCatalogSqlTests
{
    private static IList<ParseError> Parse(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        IList<ParseError> errors;
        using (var reader = new StringReader(sql)) parser.Parse(reader, out errors);
        return errors;
    }

    [Fact]
    public void ObjectsAndColumns_Parses()
    {
        var errors = Parse(SystemCatalogSql.ObjectsAndColumns);
        Assert.True(errors.Count == 0,
            "system catalog SQL does not parse: " + string.Join("; ", errors.Select(e => $"line {e.Line}: {e.Message}")));
    }

    /// <summary>
    /// The reader takes the objects from the first result set and the columns from the second, moving
    /// between them with a single NextResult. A statement added or lost here shifts that by one and
    /// the columns are read as objects — which yields a populated, entirely wrong completion list
    /// rather than an error.
    /// </summary>
    [Fact]
    public void ObjectsAndColumns_ReturnsExactlyTwoResultSets()
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(SystemCatalogSql.ObjectsAndColumns);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        Assert.Empty(errors);
        var script = Assert.IsType<TSqlScript>(fragment);
        var statements = script.Batches.SelectMany(b => b.Statements).ToList();

        Assert.Equal(2, statements.Count);
        Assert.All(statements, s => Assert.IsType<SelectStatement>(s));
    }

    /// <summary>
    /// <c>sys.objects</c> and <c>sys.columns</c> exclude system objects outright, so either one here
    /// makes the whole feature return nothing while still parsing, connecting and succeeding. This is
    /// the single substitution that would break it in the way nobody would think to check.
    /// </summary>
    [Fact]
    public void ObjectsAndColumns_ReadsTheAllViewsNotTheUserOnlyOnes()
    {
        string sql = SystemCatalogSql.ObjectsAndColumns;

        Assert.Contains("sys.all_objects", sql);
        Assert.Contains("sys.all_columns", sql);
        Assert.DoesNotContain("sys.objects", sql);
        Assert.DoesNotContain("sys.columns", sql);
    }

    /// <summary>
    /// The complement of <c>SchemaCacheLoader</c>, which loads <c>is_ms_shipped = 0</c>. Flipping this
    /// to 0 would load the user objects a second time under the system schemas' name and leave
    /// <c>sys.</c> empty.
    /// </summary>
    [Fact]
    public void ObjectsAndColumns_SelectsShippedObjectsOnly()
    {
        Assert.DoesNotContain("is_ms_shipped = 0", SystemCatalogSql.ObjectsAndColumns);
        Assert.Equal(2, CountOccurrences(SystemCatalogSql.ObjectsAndColumns, "is_ms_shipped = 1"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
