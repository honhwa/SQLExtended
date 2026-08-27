using System;
using System.Collections.Generic;
using SQLExtended.Export;
using Xunit;

namespace SQLExtended.Tests.Export;

/// <summary>
/// Covers the naming rules behind the schema folder export. The interesting cases are all ones where two
/// objects would otherwise land on the same file: the second one silently overwrites the first, and the
/// folder compare the export exists to feed then reports an object as missing from one server.
/// </summary>
public class ExportFileNamingTests
{
    private static HashSet<string> NewUsedSet() => new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void QualifiesWithSchema()
    {
        Assert.Equal("dbo.Customer.sql", ExportFileNaming.UniqueFileName(NewUsedSet(), "dbo", "Customer"));
    }

    [Fact]
    public void OmitsSchemaWhenThereIsNone()
    {
        // Schemas and database-level DDL triggers have no owning schema.
        Assert.Equal("Sales.sql", ExportFileNaming.UniqueFileName(NewUsedSet(), null, "Sales"));
        Assert.Equal("Sales.sql", ExportFileNaming.UniqueFileName(NewUsedSet(), "", "Sales"));
    }

    [Fact]
    public void SameNameInDifferentSchemasStaysDistinct()
    {
        var used = NewUsedSet();
        Assert.Equal("dbo.Order.sql", ExportFileNaming.UniqueFileName(used, "dbo", "Order"));
        Assert.Equal("sales.Order.sql", ExportFileNaming.UniqueFileName(used, "sales", "Order"));
    }

    [Fact]
    public void CaseOnlyDifferenceGetsItsOwnFile()
    {
        // A case-sensitive collation allows both; Windows does not, so the second must not overwrite.
        var used = NewUsedSet();
        Assert.Equal("dbo.Customer.sql", ExportFileNaming.UniqueFileName(used, "dbo", "Customer"));
        Assert.Equal("dbo.customer~2.sql", ExportFileNaming.UniqueFileName(used, "dbo", "customer"));
    }

    [Fact]
    public void ThirdCollisionKeepsCounting()
    {
        var used = NewUsedSet();
        ExportFileNaming.UniqueFileName(used, "dbo", "T");
        Assert.Equal("dbo.t~2.sql", ExportFileNaming.UniqueFileName(used, "dbo", "t"));
        Assert.Equal("dbo.T~3.sql", ExportFileNaming.UniqueFileName(used, "dbo", "T"));
    }

    [Fact]
    public void CollisionAfterSanitizingIsStillResolved()
    {
        // [A/B] and [A\B] are different objects that sanitize to the same name.
        var used = NewUsedSet();
        Assert.Equal("dbo.A_B.sql", ExportFileNaming.UniqueFileName(used, "dbo", "A/B"));
        Assert.Equal("dbo.A_B~2.sql", ExportFileNaming.UniqueFileName(used, "dbo", "A\\B"));
    }

    [Theory]
    [InlineData("A/B", "A_B")]
    [InlineData("A\\B", "A_B")]
    [InlineData("Report:2024", "Report_2024")]
    [InlineData("What?", "What_")]
    [InlineData("a\"b", "a_b")]
    [InlineData("a|b", "a_b")]
    [InlineData("a<b>c", "a_b_c")]
    public void ReplacesCharactersWindowsForbids(string name, string expected)
    {
        Assert.Equal(expected, ExportFileNaming.SanitizeFileName(name));
    }

    [Theory]
    [InlineData("Trailing.")]
    [InlineData("Trailing ")]
    [InlineData("Trailing. . ")]
    public void DropsTrailingDotsAndSpaces(string name)
    {
        // Windows strips these when creating the file, so "Foo." and "Foo" would be the same file
        // without the collision ever being noticed.
        Assert.Equal("Trailing", ExportFileNaming.SanitizeFileName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("   ")]
    public void NeverReturnsAnEmptyName(string name)
    {
        Assert.Equal("_", ExportFileNaming.SanitizeFileName(name));
    }

    [Fact]
    public void TruncatesVeryLongNamesWithoutLosingUniqueness()
    {
        // SQL Server allows 128-character identifiers on both sides of the dot; the full path has to stay
        // inside MAX_PATH, and two names sharing a long prefix must still get separate files.
        var used = NewUsedSet();
        string longName = new string('x', 200);

        string first = ExportFileNaming.UniqueFileName(used, "dbo", longName);
        string second = ExportFileNaming.UniqueFileName(used, "dbo", longName + "different");

        Assert.True(first.Length <= 124, $"expected a truncated name, got {first.Length} chars");
        Assert.NotEqual(first, second);
        Assert.EndsWith("~2.sql", second);
    }

    [Fact]
    public void TypeFolderListIsRecognisedCaseInsensitively()
    {
        // Drives which folders a re-export may clean, so a name that fell out of step would leave stale
        // scripts behind — and a stale script reads as an object that exists on both servers.
        Assert.All(ExportFileNaming.TypeFolders, folder => Assert.True(ExportFileNaming.IsTypeFolder(folder)));
        Assert.True(ExportFileNaming.IsTypeFolder("tables"));
        Assert.True(ExportFileNaming.IsTypeFolder("STORED PROCEDURES"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Data")]
    [InlineData("Table")]
    [InlineData("bin")]
    public void NonTypeFoldersAreNotClaimed(string folder)
    {
        Assert.False(ExportFileNaming.IsTypeFolder(folder));
    }
}
