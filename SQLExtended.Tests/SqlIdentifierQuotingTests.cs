using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLExtended.IntelliSense;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// A completion that inserts an unbracketed name does not fail as a syntax error you can trace back to the
/// completion list. <c>SELECT Ongoing Qty</c> is a perfectly good SELECT of the column <c>Ongoing</c> under
/// the alias <c>Qty</c>, so it surfaces as "invalid column name Ongoing" — a column nobody typed — or
/// silently returns the wrong column where one named <c>Ongoing</c> also exists. A reserved word fails with
/// the parser pointing at the punctuation beside it. Both are a long way from the cause.
/// </summary>
public class SqlIdentifierQuotingTests
{
    private readonly ITestOutputHelper _output;

    public SqlIdentifierQuotingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static bool Parses(string sql)
    {
        var parser = new TSql170Parser(true);
        IList<ParseError> errors;
        using (var reader = new StringReader(sql))
            parser.Parse(reader, out errors);
        return errors.Count == 0;
    }

    /// <summary>
    /// What the engine actually thinks: can this word stand as an identifier, bare and qualified? Both forms
    /// are asked because a word only needs brackets in the position that rejects it, and requiring brackets
    /// where *either* fails is the safe reading.
    /// </summary>
    private static bool ParserAcceptsAsIdentifier(string word) =>
        Parses($"SELECT t.{word} FROM dbo.T AS t;") && Parses($"SELECT {word} FROM dbo.T;");

    // --- The reserved list is verified, not asserted ---

    /// <summary>
    /// Words Microsoft's "Reserved Keywords (Transact-SQL)" page lists that ScriptDom nonetheless accepts as
    /// identifiers. They stay on our list and get brackets anyway: over-bracketing produces SQL that runs,
    /// and preferring the parser to the documentation here would produce SQL that might not. Naming them
    /// keeps the check below able to catch a word added to the reserved set by mistake — `VALUE` or `NAME`
    /// slipping in would bracket a large share of real column names.
    /// </summary>
    private static readonly HashSet<string> DocumentedReservedThatTheParserAccepts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SECURITYAUDIT", "IDENTITYCOL", "DUMP", "LOAD", "DISK", "ROWGUIDCOL", "PRECISION",
        };

    [Fact]
    public void EveryWordOnTheReservedListIsOneTheParserRejects()
    {
        var wrong = ReservedWords()
            .Where(w => ParserAcceptsAsIdentifier(w) && !DocumentedReservedThatTheParserAccepts.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(wrong.Count == 0,
            "These are on the reserved list but the parser accepts them as identifiers, so bracketing them " +
            "is unnecessary noise. Either drop them, or add them to DocumentedReservedThatTheParserAccepts " +
            "with a reason: " + string.Join(", ", wrong));
    }

    [Fact]
    public void TheDocumentedDivergenceListIsStillAccurate()
    {
        // If a future ScriptDom starts rejecting one of these, it belongs in the plain reserved case and the
        // allowance should shrink — the list is not a place for words to accumulate unexamined.
        var nowRejected = DocumentedReservedThatTheParserAccepts
            .Where(w => !ParserAcceptsAsIdentifier(w))
            .ToList();

        Assert.True(nowRejected.Count == 0,
            "The parser now rejects these, so they no longer need an allowance: " + string.Join(", ", nowRejected));
    }

    [Fact]
    public void NoWordTheParserRejectsIsMissingFromTheReservedList()
    {
        // The candidate universe is every single word the completion engine knows about — keywords and
        // built-in function names — which is a fair approximation of the SQL words that turn up as column
        // names. A word the parser refuses and this list does not know about would be inserted bare.
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in SqlKeywords.GetKeywordsForContext(KeywordContext.None).Select(k => k.Text))
            foreach (var part in word.Split(' ', '\t'))
                if (IsWordLike(part)) candidates.Add(part);
        foreach (var fn in SqlBuiltInFunctions.All.Select(f => f.Name))
            if (IsWordLike(fn)) candidates.Add(fn);

        Assert.NotEmpty(candidates);
        _output.WriteLine($"candidate words: {candidates.Count}");

        var missing = candidates
            .Where(w => !ParserAcceptsAsIdentifier(w) && !SqlIdentifierQuoting.IsReservedKeyword(w))
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(missing.Count == 0,
            "The parser rejects these as identifiers but they are not on the reserved list, so completion " +
            "would insert them bare: " + string.Join(", ", missing));
    }

    private static bool IsWordLike(string s) =>
        !string.IsNullOrEmpty(s) && s.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static IEnumerable<string> ReservedWords()
    {
        // Reached through the public predicate rather than the private set, so the test exercises what
        // callers see. The word list itself comes from the same documentation page the set was built from.
        foreach (var word in AllCandidateWords())
            if (SqlIdentifierQuoting.IsReservedKeyword(word))
                yield return word;
    }

    private static IEnumerable<string> AllCandidateWords()
    {
        foreach (var word in SqlKeywords.GetKeywordsForContext(KeywordContext.None).Select(k => k.Text))
            foreach (var part in word.Split(' ', '\t'))
                if (IsWordLike(part)) yield return part;
        foreach (var fn in SqlBuiltInFunctions.All.Select(f => f.Name))
            if (IsWordLike(fn)) yield return fn;
        foreach (var extra in ExtraReservedProbes) yield return extra;
    }

    /// <summary>
    /// Reserved words the completion lists have no reason to contain, so the verification above would never
    /// reach them. Listed here so both directions still cover the whole set.
    /// </summary>
    private static readonly string[] ExtraReservedProbes =
    {
        "ERRLVL", "LINENO", "TSEQUAL", "SECURITYAUDIT", "IDENTITYCOL", "IDENTITY_INSERT", "OFFSETS",
        "SEMANTICKEYPHRASETABLE", "SEMANTICSIMILARITYTABLE", "SEMANTICSIMILARITYDETAILSTABLE",
        "DUMP", "LOAD", "DISK", "SETUSER", "TEXTSIZE", "ROWGUIDCOL", "FREETEXTTABLE", "CONTAINSTABLE",
        "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML", "UPDATETEXT", "READTEXT", "WRITETEXT",
        "RECONFIGURE", "SHUTDOWN", "TRY_CONVERT", "PRECISION", "VARYING", "NATIONAL", "TABLESAMPLE",
    };

    // --- Behaviour ---

    [Theory]
    [InlineData("OrderNbr")]
    [InlineData("Order_Nbr")]
    [InlineData("Column1")]
    [InlineData("_leading")]
    [InlineData("Value")]      // keyword, but not reserved — a legal column name, left bare
    [InlineData("Name")]
    [InlineData("Status")]
    [InlineData("Type")]
    public void PlainNamesAreLeftAlone(string name)
    {
        Assert.Equal(name, SqlIdentifierQuoting.QuoteIfNeeded(name));
    }

    [Theory]
    [InlineData("Ongoing Qty", "[Ongoing Qty]")]
    [InlineData("Est Ship Date", "[Est Ship Date]")]
    [InlineData("#BO", "[#BO]")]
    [InlineData("PKG_#QTY", "[PKG_#QTY]")]
    [InlineData("Split-ship", "[Split-ship]")]
    [InlineData("2ndAttempt", "[2ndAttempt]")]
    [InlineData("Total (kg)", "[Total (kg)]")]
    [InlineData("Order", "[Order]")]
    [InlineData("Key", "[Key]")]
    [InlineData("group", "[group]")]        // reserved test is case-insensitive
    [InlineData("Percent", "[Percent]")]
    public void NamesThatCannotStandAloneAreBracketed(string name, string expected)
    {
        Assert.Equal(expected, SqlIdentifierQuoting.QuoteIfNeeded(name));
    }

    [Fact]
    public void AClosingBracketInTheNameIsDoubled()
    {
        // The one input that would otherwise close the bracket early and produce SQL that does not parse.
        Assert.Equal("[Odd]]Name]", SqlIdentifierQuoting.QuoteIfNeeded("Odd]Name"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullAndEmptyAreReturnedUnchanged(string name)
    {
        Assert.Equal(name, SqlIdentifierQuoting.QuoteIfNeeded(name));
        Assert.Equal(name, SqlIdentifierQuoting.QuoteObjectIfNeeded(name));
    }

    // --- Object names: the prefixes that must survive ---

    [Theory]
    [InlineData("@Orders")]        // a table variable; [@Orders] is a different thing entirely
    [InlineData("@tv")]
    [InlineData("#tmp")]           // temp tables are legal bare and everyone writes them that way
    [InlineData("##global")]
    [InlineData("Orders")]
    public void ObjectNamesWithMeaningfulPrefixesAreLeftBare(string name)
    {
        Assert.Equal(name, SqlIdentifierQuoting.QuoteObjectIfNeeded(name));
    }

    [Theory]
    [InlineData("My Table", "[My Table]")]
    [InlineData("Order", "[Order]")]
    [InlineData("#my tmp", "[#my tmp]")]      // prefix kept inside the brackets
    [InlineData("##odd tmp", "[##odd tmp]")]
    [InlineData("#Order", "[#Order]")]        // reserved after the prefix still needs them
    public void ObjectNamesThatCannotStandAloneAreBracketed(string name, string expected)
    {
        Assert.Equal(expected, SqlIdentifierQuoting.QuoteObjectIfNeeded(name));
    }

    // --- The assertion that matters: what comes out has to parse as one thing ---

    [Theory]
    [InlineData("Ongoing Qty")]
    [InlineData("Est Ship Date")]
    [InlineData("#BO")]
    [InlineData("Odd]Name")]
    [InlineData("Total (kg)")]
    [InlineData("2ndAttempt")]
    [InlineData("Order")]
    [InlineData("Key")]
    [InlineData("Select")]
    [InlineData("From")]
    public void TheQuotedColumnNameParsesAsASingleUnaliasedReference(string name)
    {
        string quoted = SqlIdentifierQuoting.QuoteIfNeeded(name);
        string sql = $"SELECT t.{quoted} FROM dbo.T AS t;";

        var parser = new TSql170Parser(true);
        IList<ParseError> errors;
        TSqlFragment fragment;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out errors);

        Assert.True(errors.Count == 0, errors.Count == 0 ? "" : $"{sql} -> {errors[0].Message}");

        var visitor = new SelectElementCollector();
        fragment.Accept(visitor);

        // One element carrying no alias. An unbracketed "Ongoing Qty" yields one element named Ongoing
        // aliased Qty — which parses, and is the whole reason this is checked against the tree.
        Assert.Single(visitor.Elements);
        var column = Assert.IsType<SelectScalarExpression>(visitor.Elements[0]);
        Assert.Null(column.ColumnName);

        var reference = Assert.IsType<ColumnReferenceExpression>(column.Expression);
        Assert.Equal(name, reference.MultiPartIdentifier.Identifiers.Last().Value);
    }

    /// <summary>
    /// The other half of the test above, and the reason the fixtures spell their two-word names the way
    /// they do. The hazard is only a hazard while the *unbracketed* form still parses: "Ongoing Qty" has to
    /// come back as the column Ongoing under the alias Qty. A substitute vocabulary that happened to pick a
    /// reserved second word ("Ongoing Order") would still pass every assertion above while quietly no longer
    /// demonstrating anything.
    /// </summary>
    [Theory]
    [InlineData("Ongoing Qty", "Ongoing", "Qty")]
    [InlineData("Est Ship", "Est", "Ship")]
    public void TheUnbracketedNameIsStillReadAsAColumnPlusAnAlias(string name, string column, string alias)
    {
        var parser = new TSql170Parser(true);
        IList<ParseError> errors;
        TSqlFragment fragment;
        using (var reader = new StringReader($"SELECT t.{name} FROM dbo.T AS t;"))
            fragment = parser.Parse(reader, out errors);

        Assert.True(errors.Count == 0, $"'{name}' no longer parses unbracketed - the fixture no longer shows the bug");

        var visitor = new SelectElementCollector();
        fragment.Accept(visitor);

        var element = Assert.IsType<SelectScalarExpression>(Assert.Single(visitor.Elements));
        Assert.Equal(alias, element.ColumnName.Value);
        Assert.Equal(column, ((ColumnReferenceExpression)element.Expression).MultiPartIdentifier.Identifiers.Last().Value);
    }

    /// <summary>The complementary half: the reserved word the fixtures use must still be rejected bare.</summary>
    [Fact]
    public void TheReservedFixtureWordIsStillRejectedBare()
    {
        Assert.False(Parses("SELECT t.Order FROM dbo.T AS t;"));
        Assert.False(Parses("INSERT INTO dbo.T (Order) VALUES (1);"));
    }

    [Theory]
    [InlineData("My Table")]
    [InlineData("Order")]
    [InlineData("#tmp")]
    [InlineData("##global")]
    [InlineData("#my tmp")]
    [InlineData("Odd]Name")]
    public void TheQuotedObjectNameParsesAsASingleTableReference(string name)
    {
        string sql = $"SELECT * FROM {SqlIdentifierQuoting.QuoteObjectIfNeeded(name)};";
        Assert.True(Parses(sql), $"{sql} did not parse");
    }

    [Fact]
    public void AQuotedColumnListParsesInEveryPositionThatRejectsABareReservedWord()
    {
        // The positions the probe showed a bare reserved word failing in. Completion writes column names
        // into all of them, so all of them are checked.
        string order = SqlIdentifierQuoting.QuoteIfNeeded("Order");
        string spaced = SqlIdentifierQuoting.QuoteIfNeeded("Ongoing Qty");

        Assert.True(Parses($"INSERT INTO dbo.T ({order}, {spaced}) VALUES (1, 2);"));
        Assert.True(Parses($"UPDATE dbo.T SET {order} = 1, {spaced} = 2;"));
        Assert.True(Parses($"SELECT 1 FROM dbo.T GROUP BY {order}, {spaced};"));
        Assert.True(Parses($"SELECT a.{order} FROM dbo.A AS a INNER JOIN dbo.B AS b ON a.{order} = b.{spaced};"));
    }

    private sealed class SelectElementCollector : TSqlFragmentVisitor
    {
        public List<SelectElement> Elements { get; } = new List<SelectElement>();

        public override void Visit(QuerySpecification node) => Elements.AddRange(node.SelectElements);
    }
}
