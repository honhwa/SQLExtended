using System;

namespace SQLExtended.Cache.Models;

internal sealed class CachedObject
{
    public string ConnectionKey { get; set; }
    public string DatabaseName { get; set; }
    public string SchemaName { get; set; }
    public string ObjectName { get; set; }

    /// <summary>
    /// Object type code from sys.objects: U (table), V (view), P (proc),
    /// FN (scalar function), IF (inline TVF), TF (table function), SN (synonym), TT (table type).
    /// </summary>
    public string ObjectType { get; set; }

    public long RowCount { get; set; }
    public DateTime? CreateDate { get; set; }
    public DateTime? ModifyDate { get; set; }

    /// <summary>
    /// Stored proc/function/view definition body (from sys.sql_modules).
    /// </summary>
    public string Definition { get; set; }

    /// <summary>
    /// The module was created WITH ENCRYPTION, so <see cref="Definition"/> came back NULL from
    /// sys.sql_modules. Not persisted: it is re-derived on every load, and after a successful decryption
    /// pass the definition is filled in while this stays true — the text is real, the object is still
    /// encrypted on the server, and callers that report where a definition came from need to know both.
    /// </summary>
    public bool IsEncrypted { get; set; }

    public string ObjectTypeDisplay => ObjectType?.Trim() switch
    {
        "U" => "Table",
        "V" => "View",
        "P" => "Stored Procedure",
        "FN" => "Scalar Function",
        "IF" => "Inline Table Function",
        "TF" => "Table Function",
        "SN" => "Synonym",
        "TT" => "Table Type",
        _ => "Object"
    };

    public string QualifiedName => $"[{SchemaName}].[{ObjectName}]";
}
