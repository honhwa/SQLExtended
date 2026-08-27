using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SQLExtended.Export;

/// <summary>
/// Folder and file naming for the schema folder export. Pure string handling — no SMO, no I/O — so it
/// can be unit tested, which matters more here than it looks: two objects that collapse onto one file
/// name silently drop one of them, and in a folder compare that reads as "this object doesn't exist on
/// the other server". That is the one failure mode of this feature nobody would ever notice.
/// </summary>
internal static class ExportFileNaming
{
    /// <summary>
    /// The subfolder names the export owns, one per object type. Doubles as the whitelist of folders a
    /// re-export is allowed to clean, so it must stay in step with the exporter's groups — a folder
    /// missing from here is a folder whose stale scripts survive a re-export and lie in the next diff.
    /// </summary>
    public static readonly string[] TypeFolders =
    {
        "Tables",
        "Views",
        "Stored Procedures",
        "Functions",
        "Table Types",
        "User-Defined Types",
        "Sequences",
        "Synonyms",
        "Schemas",
        "Database Triggers",
    };

    /// <summary>Longest base name (before ".sql") we will write, to keep full paths inside MAX_PATH.</summary>
    private const int MaxBaseLength = 120;

    public static bool IsTypeFolder(string folderName)
        => !string.IsNullOrEmpty(folderName) && TypeFolders.Any(f => string.Equals(f, folderName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Makes a single path segment safe for Windows: invalid characters become '_', and trailing dots and
    /// spaces are dropped (Windows silently strips them, so "Foo." and "Foo" would be the same file).
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        string cleaned = new string(chars).TrimEnd('.', ' ');

        return cleaned.Length == 0 ? "_" : cleaned;
    }

    /// <summary>
    /// Returns the file name to write an object to, as "schema.object.sql", guaranteed unique within
    /// <paramref name="used"/>. Pass an <see cref="StringComparer.OrdinalIgnoreCase"/> set: SQL Server
    /// can hold both [dbo].[Foo] and [dbo].[foo] under a case-sensitive collation, but Windows cannot,
    /// so the second one has to be given a distinct name rather than overwrite the first.
    /// </summary>
    public static string UniqueFileName(HashSet<string> used, string schema, string objectName)
    {
        string baseName = string.IsNullOrEmpty(schema) ? objectName : $"{schema}.{objectName}";
        baseName = SanitizeFileName(baseName);
        if (baseName.Length > MaxBaseLength)
            baseName = baseName.Substring(0, MaxBaseLength).TrimEnd('.', ' ');
        if (baseName.Length == 0) baseName = "_";

        string candidate = baseName;
        for (int n = 2; !used.Add(candidate); n++)
            candidate = $"{baseName}~{n}";

        return candidate + ".sql";
    }
}
