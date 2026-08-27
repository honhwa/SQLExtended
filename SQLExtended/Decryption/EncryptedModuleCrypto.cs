using System;
using System.Text;
using System.Text.RegularExpressions;

namespace SQLExtended.Decryption;

/// <summary>
/// The arithmetic behind reading a <c>WITH ENCRYPTION</c> module back out: building the throwaway
/// definition that is ALTERed over the original, recovering the plaintext from the two ciphertexts, and
/// deciding whether what came back is really a module definition.
///
/// SQL Server does not encrypt module text with anything you can hold a key for — it XORs it against a
/// keystream derived from (database family GUID, object id, subobject id). Nothing in that derivation
/// depends on the text or its length, so two different definitions of the *same object* are masked by the
/// same keystream, and:
///
///     plain(i) = cipher_original(i) XOR cipher_dummy(i) XOR plain_dummy(i)
///
/// recovers the original a character at a time, with no key ever being computed. That is the whole trick,
/// and it is why <see cref="ModuleDecryptionService"/> has to briefly ALTER the object (inside a
/// transaction it always rolls back) to a definition whose plaintext it already knows.
///
/// Deriving the keystream directly from the family GUID would avoid the ALTER altogether, but the exact
/// byte layout that goes into it is undocumented and could not be verified here — a wrong guess would
/// produce plausible-looking garbage. The ALTER route is verifiable against any instance, so it is the one
/// implemented.
///
/// This file is deliberately free of SqlClient and WPF so the test project can link it.
/// </summary>
internal static class EncryptedModuleCrypto
{
    /// <summary>
    /// SMO's stand-in for an encrypted module. It scripts this *instead of throwing*, which means an export
    /// that does not notice it writes a file that is identical for every encrypted object — two different
    /// procedures would compare equal in a folder diff.
    /// </summary>
    public const string SmoEncryptedMarker = "Encrypted object is not transferable";

    /// <summary>
    /// Longest module text this will attempt, in characters. A blob far larger than any real definition
    /// means the row was not what we thought it was; allocating from it is not worth the risk.
    /// </summary>
    private const int MaxDefinitionChars = 8 * 1024 * 1024;

    /// <summary>
    /// The keyword SQL Server stores a module's text under. <b>This is the single most important detail in
    /// this file, and getting it wrong is silent.</b>
    ///
    /// The throwaway definition has to be applied with ALTER — the object already exists. But what the
    /// engine stores, and therefore what is encrypted and what the XOR must be run against, is the
    /// <c>CREATE</c> form of that same statement. Reconstructing the plaintext from the ALTER text produces
    /// a string that is correct for five characters and then off by one for the rest of the module, which
    /// is indistinguishable from a wrong key.
    ///
    /// This is empirical — established against a live instance, and the reason every published version of
    /// this technique builds its statement as a keyword plus a shared body rather than as one string.
    /// </summary>
    public const string StoredKeyword = "CREATE";

    /// <summary>The keyword the statement is actually executed with. See <see cref="StoredKeyword"/>.</summary>
    public const string ExecutedKeyword = "ALTER";

    /// <summary>
    /// Builds the *body* of the throwaway definition — everything after the leading keyword, starting with
    /// a space, so that <see cref="AlterStatement"/> and <see cref="StoredPlaintext"/> can put the two
    /// different keywords in front of one shared body. Two things matter about it and nothing else does:
    ///
    /// 1. It must compile. ALTER cannot change a function between scalar, inline and multi-statement forms,
    ///    so each gets its own shape rather than one generic body.
    /// 2. Its exact text must be known here, character for character, because it is one of the three terms
    ///    in the XOR.
    ///
    /// <paramref name="targetTotalLength"/> is the length of the finished <see cref="StoredPlaintext"/>,
    /// not of the body. Matching the original's length is not strictly required — the keystream is
    /// position-based, not length-based — but it costs nothing and keeps the two ciphertexts the same size,
    /// so a length mismatch stays a real signal. When the minimum valid body is longer than the original (a
    /// short view, a trigger on a long table name) the dummy is left longer and the extra ciphertext is
    /// simply not used.
    ///
    /// Returns null for an object type this cannot write a body for.
    /// </summary>
    public static string BuildDummyBody(string objectType, string schema, string name, string parentSchema, string parentName, int targetTotalLength)
    {
        string qualified = Quote(schema, name);

        string body = (objectType ?? "").Trim().ToUpperInvariant() switch
        {
            // A body of "RETURN" rather than nothing: an empty procedure body is legal, but a single
            // unambiguous statement leaves nothing for a future parser change to disagree about.
            "P" or "PC" or "RF" => $" PROCEDURE {qualified} WITH ENCRYPTION AS RETURN",
            "V" => $" VIEW {qualified} WITH ENCRYPTION AS SELECT 1 AS c",
            "FN" => $" FUNCTION {qualified}() RETURNS int WITH ENCRYPTION AS BEGIN RETURN 1 END",
            "IF" => $" FUNCTION {qualified}() RETURNS TABLE WITH ENCRYPTION AS RETURN SELECT 1 AS c",
            "TF" => $" FUNCTION {qualified}() RETURNS @r TABLE (c int) WITH ENCRYPTION AS BEGIN RETURN END",
            // A DML trigger has to name its table, and ALTER TRIGGER cannot move it to another one.
            "TR" when !string.IsNullOrEmpty(parentName) =>
                $" TRIGGER {qualified} ON {Quote(parentSchema, parentName)} WITH ENCRYPTION AFTER INSERT AS RETURN",
            _ => null,
        };

        return body == null ? null : Pad(body, Math.Max(0, targetTotalLength - StoredKeyword.Length));
    }

    /// <summary>The statement to execute: the body applied with ALTER, because the object exists.</summary>
    public static string AlterStatement(string body) => body == null ? null : ExecutedKeyword + body;

    /// <summary>
    /// The plaintext the engine actually stores for that statement, which is the same body under
    /// <see cref="StoredKeyword"/>. This — not <see cref="AlterStatement"/> — is the term that goes into
    /// the XOR.
    /// </summary>
    public static string StoredPlaintext(string body) => body == null ? null : StoredKeyword + body;

    /// <summary>
    /// Pads to the target length with a trailing single-line comment. Under three characters short there is
    /// no room for the comment marker, so trailing spaces make up the difference instead — they are just as
    /// invisible to the parser and just as known to the caller, which is all the padding has to be.
    /// </summary>
    private static string Pad(string head, int targetLength)
    {
        int pad = targetLength - head.Length;
        if (pad <= 0) return head;
        if (pad < 3) return head + new string(' ', pad);
        return head + " --" + new string('-', pad - 3);
    }

    /// <summary>
    /// Bracket-quotes a possibly schema-less name, doubling any embedded <c>]</c>. The dummy is executed as
    /// dynamic SQL, so this is the only thing standing between an object called <c>x] DROP TABLE y --</c>
    /// and that statement running.
    /// </summary>
    public static string Quote(string schema, string name)
    {
        string quotedName = "[" + (name ?? "").Replace("]", "]]") + "]";
        return string.IsNullOrEmpty(schema) ? quotedName : "[" + schema.Replace("]", "]]") + "]." + quotedName;
    }

    /// <summary>
    /// Recovers the original definition from the two ciphertexts and the dummy's known plaintext. Both blobs
    /// are UTF-16LE, so every character is one little-endian pair of bytes and the XOR is done per character
    /// rather than per byte — a surrogate pair survives untouched either way, but the character view is what
    /// makes the length checks mean anything.
    ///
    /// Returns null when the inputs cannot produce a complete answer: a partial decryption looks exactly
    /// like a definition that has been truncated, and letting one reach the cache or an export would be
    /// worse than reporting nothing.
    /// </summary>
    public static string Xor(byte[] originalCipher, byte[] dummyCipher, string dummyPlain)
    {
        if (originalCipher == null || dummyCipher == null || dummyPlain == null) return null;

        int originalChars = originalCipher.Length / 2;
        if (originalChars == 0 || originalChars > MaxDefinitionChars) return null;

        // The dummy has to cover the original in both its forms — the ciphertext supplies the keystream,
        // the plaintext supplies the mask. Either falling short leaves the tail unrecoverable.
        if (dummyCipher.Length / 2 < originalChars || dummyPlain.Length < originalChars) return null;

        var chars = new char[originalChars];
        for (int i = 0; i < originalChars; i++)
        {
            int original = originalCipher[i * 2] | (originalCipher[(i * 2) + 1] << 8);
            int dummy = dummyCipher[i * 2] | (dummyCipher[(i * 2) + 1] << 8);
            chars[i] = (char)(original ^ dummy ^ dummyPlain[i]);
        }

        return new string(chars);
    }

    /// <summary>
    /// Whether the recovered text is really a module definition. This is the safety net for the whole
    /// feature: if any assumption above is wrong the XOR still returns a string, and a string of noise
    /// entering the schema cache or a comparison export is the one failure mode nobody would catch by
    /// reading it. A definition must open with CREATE or ALTER and mention the object it claims to be.
    /// </summary>
    public static bool LooksLikeModuleDefinition(string text, string objectName)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // A leading comment block is normal in hand-written modules, so skip past whitespace and comments
        // before looking for the keyword.
        int i = SkipLeadingTrivia(text);
        if (i >= text.Length) return false;

        bool opensCorrectly = StartsWithKeyword(text, i, "CREATE") || StartsWithKeyword(text, i, "ALTER");
        if (!opensCorrectly) return false;

        return string.IsNullOrEmpty(objectName)
            || text.IndexOf(objectName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int SkipLeadingTrivia(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == '﻿') { i++; continue; }

            if (text[i] == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                int end = text.IndexOf('\n', i);
                if (end < 0) return text.Length;
                i = end + 1;
                continue;
            }

            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return text.Length;
                i = end + 2;
                continue;
            }

            break;
        }
        return i;
    }

    private static bool StartsWithKeyword(string text, int start, string keyword)
    {
        if (start + keyword.Length > text.Length) return false;
        if (string.Compare(text, start, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;

        int after = start + keyword.Length;
        return after >= text.Length || !char.IsLetterOrDigit(text[after]) && text[after] != '_';
    }

    private static readonly Regex LeadingAlter = new(@"^(\s*)ALTER\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Rewrites a leading ALTER to CREATE. What comes back from the server is whatever text was last
    /// submitted, so a module that was last changed with ALTER decrypts to an ALTER — and in a folder
    /// export that would read as a difference against the same definition created with CREATE on the other
    /// server. SMO normalises this for every object it scripts itself; encrypted objects bypass SMO, so
    /// they are normalised here instead.
    ///
    /// A definition whose ALTER hides behind a leading comment is left alone: the export is a comparison
    /// artefact, and rewriting text this cannot parse confidently is a worse trade than one word of noise.
    /// </summary>
    public static string NormalizeToCreate(string definition)
        => string.IsNullOrEmpty(definition) ? definition : LeadingAlter.Replace(definition, "$1CREATE", 1);

    /// <summary>
    /// Forces CRLF, matching what the folder export does with every other script it writes.
    /// </summary>
    public static string NormalizeLineEndings(string text)
        => text == null ? null : text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

    /// <summary>
    /// Concatenates the <c>imageval</c> chunks of one object in <c>valnum</c> order. In current releases
    /// there is a single row, but the column is an <c>image</c> and the layout predates that guarantee, so
    /// the rows are joined rather than assumed to be one.
    /// </summary>
    public static byte[] Concat(System.Collections.Generic.IReadOnlyList<byte[]> chunks)
    {
        if (chunks == null || chunks.Count == 0) return null;
        if (chunks.Count == 1) return chunks[0];

        int total = 0;
        foreach (var chunk in chunks) total += chunk?.Length ?? 0;

        var result = new byte[total];
        int offset = 0;
        foreach (var chunk in chunks)
        {
            if (chunk == null || chunk.Length == 0) continue;
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    /// <summary>UTF-16LE bytes of a string — the encoding module text is stored in.</summary>
    public static byte[] ToBytes(string text) => Encoding.Unicode.GetBytes(text ?? "");
}
