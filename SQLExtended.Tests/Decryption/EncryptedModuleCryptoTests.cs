using System;
using System.Text;
using SQLExtended.Decryption;
using Xunit;

namespace SQLExtended.Tests.Decryption;

/// <summary>
/// The arithmetic behind reading a WITH ENCRYPTION module back out. Everything here is checkable without a
/// server: a stand-in keystream stands in for SQL Server's, which is exactly what the technique assumes
/// about it — a stream that depends on the object, not on the text.
///
/// The validation tests matter most. If any assumption about the storage layout is wrong the XOR still
/// returns a string, and a string of noise reaching the schema cache or a comparison export is the one
/// failure of this feature nobody would catch by reading the output.
/// </summary>
public class EncryptedModuleCryptoTests
{
    /// <summary>
    /// Stands in for SQL Server's keystream. What it does is irrelevant; what matters is that the same
    /// object always gets the same stream regardless of the text or its length, which is the single
    /// property the whole technique rests on.
    /// </summary>
    private static byte[] Encrypt(string plain, int seed)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(plain);
        var cipher = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            cipher[i] = (byte)(bytes[i] ^ (byte)((seed * 31) + (i * 7) + 13));
        return cipher;
    }

    /// <summary>
    /// Models the server: the statement is submitted with ALTER, but what gets stored and encrypted is the
    /// CREATE form of the same body. Every round-trip test goes through here, because reproducing that
    /// asymmetry is the whole point — decrypting against the ALTER text was a real bug, and it fails in the
    /// least obvious way possible (right for five characters, then one out of step forever).
    /// </summary>
    private static byte[] StoreDummy(string body, int seed)
        => Encrypt(EncryptedModuleCrypto.StoredPlaintext(body), seed);

    [Fact]
    public void Xor_RecoversTheOriginalDefinition()
    {
        const string original = "CREATE PROCEDURE [dbo].[GetCustomer] @id int WITH ENCRYPTION AS SELECT * FROM dbo.Customer WHERE Id = @id";
        string body = EncryptedModuleCrypto.BuildDummyBody("P", "dbo", "GetCustomer", null, null, original.Length);

        string recovered = EncryptedModuleCrypto.Xor(
            Encrypt(original, 42), StoreDummy(body, 42), EncryptedModuleCrypto.StoredPlaintext(body));

        Assert.Equal(original, recovered);
    }

    [Fact]
    public void Xor_AgainstTheAlterTextInsteadOfTheStoredTextProducesGarbage()
    {
        // The regression this locks down. ALTER is one character shorter than CREATE, so using the executed
        // statement as the mask decrypts the leading keyword and then runs out of step — and the result is
        // still a string, which is why it went unnoticed.
        const string original = "CREATE PROCEDURE [dbo].[GetCustomer] WITH ENCRYPTION AS SELECT 1";
        string body = EncryptedModuleCrypto.BuildDummyBody("P", "dbo", "GetCustomer", null, null, original.Length);

        string wrong = EncryptedModuleCrypto.Xor(
            Encrypt(original, 11), StoreDummy(body, 11), EncryptedModuleCrypto.AlterStatement(body));

        Assert.NotEqual(original, wrong);
        Assert.False(EncryptedModuleCrypto.LooksLikeModuleDefinition(wrong, "GetCustomer"));
    }

    [Fact]
    public void Xor_RecoversNonAsciiAndMultiLineText()
    {
        string original = "CREATE VIEW [dbo].[Kunden] WITH ENCRYPTION AS\r\n  SELECT N'Grüße — ünïcode' AS Note\r\n  FROM dbo.Kunde";
        string body = EncryptedModuleCrypto.BuildDummyBody("V", "dbo", "Kunden", null, null, original.Length);

        Assert.Equal(original, EncryptedModuleCrypto.Xor(
            Encrypt(original, 7), StoreDummy(body, 7), EncryptedModuleCrypto.StoredPlaintext(body)));
    }

    [Fact]
    public void Xor_RecoversTheWholeOriginalWhenTheDummyIsLonger()
    {
        // The minimum valid body can exceed a very short original. The keystream is position-based, so the
        // extra ciphertext is simply unused — the original still comes back whole.
        const string original = "CREATE VIEW v WITH ENCRYPTION AS SELECT 1 x";
        string body = EncryptedModuleCrypto.BuildDummyBody("V", "dbo", "v", null, null, original.Length);
        string stored = EncryptedModuleCrypto.StoredPlaintext(body);

        Assert.True(stored.Length > original.Length);
        Assert.Equal(original, EncryptedModuleCrypto.Xor(Encrypt(original, 3), StoreDummy(body, 3), stored));
    }

    [Fact]
    public void Xor_ReturnsNullWhenTheDummyIsShorterThanTheOriginal()
    {
        // A partial decryption is indistinguishable from a truncated definition, so it must not be returned.
        const string original = "CREATE PROCEDURE [dbo].[p] WITH ENCRYPTION AS SELECT 1";
        const string dummy = "ALTER PROCEDURE [dbo].[p] AS RETURN";

        Assert.Null(EncryptedModuleCrypto.Xor(Encrypt(original, 1), Encrypt(dummy, 1), dummy));
    }

    [Fact]
    public void Xor_ReturnsNullOnEmptyOrMissingInput()
    {
        Assert.Null(EncryptedModuleCrypto.Xor(null, new byte[4], "ab"));
        Assert.Null(EncryptedModuleCrypto.Xor(new byte[4], null, "ab"));
        Assert.Null(EncryptedModuleCrypto.Xor(Array.Empty<byte>(), new byte[4], "ab"));
    }

    [Fact]
    public void AlterAndStoredFormsShareOneBodyAndDifferOnlyInTheKeyword()
    {
        string body = EncryptedModuleCrypto.BuildDummyBody("P", "dbo", "p", null, null, 0);

        Assert.Equal("ALTER PROCEDURE [dbo].[p] WITH ENCRYPTION AS RETURN", EncryptedModuleCrypto.AlterStatement(body));
        Assert.Equal("CREATE PROCEDURE [dbo].[p] WITH ENCRYPTION AS RETURN", EncryptedModuleCrypto.StoredPlaintext(body));
    }

    [Theory]
    [InlineData("P", " PROCEDURE [dbo].[p] WITH ENCRYPTION AS RETURN")]
    [InlineData("V", " VIEW [dbo].[p] WITH ENCRYPTION AS SELECT 1 AS c")]
    [InlineData("FN", " FUNCTION [dbo].[p]() RETURNS int WITH ENCRYPTION AS BEGIN RETURN 1 END")]
    [InlineData("IF", " FUNCTION [dbo].[p]() RETURNS TABLE WITH ENCRYPTION AS RETURN SELECT 1 AS c")]
    [InlineData("TF", " FUNCTION [dbo].[p]() RETURNS @r TABLE (c int) WITH ENCRYPTION AS BEGIN RETURN END")]
    public void BuildDummyBody_UsesTheFormEachTypeRequires(string type, string expected)
    {
        // ALTER FUNCTION cannot move a function between its scalar, inline and multi-statement forms, so
        // each type needs the shape it already has — a generic body would simply fail to compile.
        Assert.Equal(expected, EncryptedModuleCrypto.BuildDummyBody(type, "dbo", "p", null, null, 0));
    }

    [Fact]
    public void BuildDummyBody_NamesTheTriggersTable()
    {
        string body = EncryptedModuleCrypto.BuildDummyBody("TR", "dbo", "tr_Audit", "sales", "Order", 0);
        Assert.Equal(" TRIGGER [dbo].[tr_Audit] ON [sales].[Order] WITH ENCRYPTION AFTER INSERT AS RETURN", body);
    }

    [Fact]
    public void BuildDummyBody_ReturnsNullForATriggerWithNoKnownTable()
    {
        // ALTER TRIGGER has to name the table. Without one there is no statement to write, and guessing
        // would ALTER the wrong object.
        Assert.Null(EncryptedModuleCrypto.BuildDummyBody("TR", "dbo", "tr_Audit", null, null, 0));
    }

    [Fact]
    public void BuildDummyBody_ReturnsNullForAnUnsupportedType()
    {
        Assert.Null(EncryptedModuleCrypto.BuildDummyBody("U", "dbo", "t", null, null, 0));
        Assert.Null(EncryptedModuleCrypto.BuildDummyBody(null, "dbo", "t", null, null, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(40)]
    public void BuildDummyBody_PadsSoTheStoredFormHitsTheRequestedLength(int extra)
    {
        // The target is the length of the CREATE form, not of the body — that is the string whose length
        // has to line up with the original's ciphertext.
        string minimum = EncryptedModuleCrypto.StoredPlaintext(EncryptedModuleCrypto.BuildDummyBody("P", "dbo", "p", null, null, 0));
        int target = minimum.Length + extra;

        string padded = EncryptedModuleCrypto.StoredPlaintext(EncryptedModuleCrypto.BuildDummyBody("P", "dbo", "p", null, null, target));

        Assert.Equal(target, padded.Length);
        Assert.StartsWith(minimum, padded, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDummyBody_PadsWithACommentSoTheBodyStaysValid()
    {
        string body = EncryptedModuleCrypto.BuildDummyBody("P", "dbo", "p", null, null, 200);
        Assert.Contains(" --", body, StringComparison.Ordinal);
        Assert.EndsWith("-", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Quote_DoublesClosingBracketsSoTheDummyCannotBeInjectedInto()
    {
        // The dummy is executed as dynamic SQL, so this is the only thing between a hostile object name and
        // that statement running.
        Assert.Equal("[dbo].[x]] DROP TABLE y --]", EncryptedModuleCrypto.Quote("dbo", "x] DROP TABLE y --"));
        Assert.Equal("[p]", EncryptedModuleCrypto.Quote(null, "p"));
    }

    [Fact]
    public void BuildDummyBody_QuotesTheObjectNameItEmbeds()
    {
        string body = EncryptedModuleCrypto.BuildDummyBody("P", "dbo", "x] DROP TABLE y --", null, null, 0);
        Assert.Equal("ALTER PROCEDURE [dbo].[x]] DROP TABLE y --] WITH ENCRYPTION AS RETURN",
            EncryptedModuleCrypto.AlterStatement(body));
    }

    [Theory]
    [InlineData("CREATE PROCEDURE [dbo].[p] AS SELECT 1")]
    [InlineData("  \r\n ALTER PROC dbo.p AS SELECT 1")]
    [InlineData("-- header comment\r\nCREATE PROCEDURE dbo.p AS SELECT 1")]
    [InlineData("/* header\r\n   block */ CREATE PROCEDURE dbo.p AS SELECT 1")]
    public void LooksLikeModuleDefinition_AcceptsRealDefinitions(string text)
        => Assert.True(EncryptedModuleCrypto.LooksLikeModuleDefinition(text, "p"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("你好 garbage that decrypted wrong")]
    [InlineData("SELECT 1 FROM dbo.p")]
    [InlineData("CREATEPROCEDURE dbo.p AS SELECT 1")]
    [InlineData("-- only a comment")]
    public void LooksLikeModuleDefinition_RejectsAnythingElse(string text)
        => Assert.False(EncryptedModuleCrypto.LooksLikeModuleDefinition(text, "p"));

    [Fact]
    public void LooksLikeModuleDefinition_RejectsADefinitionForADifferentObject()
    {
        // Right shape, wrong object — which is what a keystream taken from the wrong object would produce.
        Assert.False(EncryptedModuleCrypto.LooksLikeModuleDefinition("CREATE PROCEDURE dbo.SomethingElse AS SELECT 1", "GetCustomer"));
    }

    [Fact]
    public void NormalizeToCreate_RewritesOnlyTheLeadingAlter()
    {
        Assert.Equal("CREATE PROCEDURE dbo.p AS ALTER TABLE dbo.t ADD c int",
            EncryptedModuleCrypto.NormalizeToCreate("ALTER PROCEDURE dbo.p AS ALTER TABLE dbo.t ADD c int"));

        Assert.Equal("\r\n  CREATE VIEW v AS SELECT 1 x",
            EncryptedModuleCrypto.NormalizeToCreate("\r\n  ALTER VIEW v AS SELECT 1 x"));
    }

    [Fact]
    public void NormalizeToCreate_LeavesACreateAlone()
    {
        const string script = "CREATE PROCEDURE dbo.p AS SELECT 1";
        Assert.Equal(script, EncryptedModuleCrypto.NormalizeToCreate(script));
    }

    [Fact]
    public void Concat_JoinsChunksInTheOrderGiven()
    {
        var joined = EncryptedModuleCrypto.Concat(new[] { new byte[] { 1, 2 }, new byte[] { 3 }, new byte[0], new byte[] { 4, 5 } });
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, joined);
    }

    [Fact]
    public void Concat_ReturnsNullWhenThereIsNothingToJoin()
    {
        Assert.Null(EncryptedModuleCrypto.Concat(null));
        Assert.Null(EncryptedModuleCrypto.Concat(Array.Empty<byte[]>()));
    }

    [Theory]
    [InlineData("ADMIN:HOST", "HOST")]
    [InlineData("admin:HOST\\SQL2022", "HOST\\SQL2022")]
    [InlineData("tcp:HOST,1433", "HOST")]
    [InlineData("HOST,49152", "HOST")]
    [InlineData("  HOST\\SQL2022  ", "HOST\\SQL2022")]
    public void StripDacUnsupportedParts_ReducesToWhatTheDacCanResolve(string source, string expected)
    {
        // The DAC listens on its own port, which SQL Browser resolves from the instance name; carrying over
        // an explicit port would connect to the normal endpoint instead and it would not be a DAC at all.
        Assert.Equal(expected, DacConnectionFactory.StripDacUnsupportedParts(source));
    }

    [Fact]
    public void BuildConnectionString_AsksForTheDacAndTurnsPoolingOff()
    {
        string dac = DacConnectionFactory.BuildConnectionString(
            "Data Source=HOST\\SQL2022,1433;Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True", "AdventureWorks");

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(dac);

        Assert.Equal("ADMIN:HOST\\SQL2022", builder.DataSource);
        Assert.Equal("AdventureWorks", builder.InitialCatalog);

        // An instance permits one DAC at a time, and a pooled connection stays open after Dispose — the
        // next attempt would be refused by a connection nobody is using.
        Assert.False(builder.Pooling);
        Assert.False(builder.MultipleActiveResultSets);
        Assert.True(builder.TrustServerCertificate);
    }
}
