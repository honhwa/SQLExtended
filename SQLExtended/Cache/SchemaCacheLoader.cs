using Microsoft.Data.SqlClient;
using SQLExtended.Cache.Models;
using SQLExtended.Decryption;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SQLExtended.Cache;

/// <summary>
/// Executes bulk queries against sys.* views to populate the schema cache.
/// All queries run in parallel for fast initial load.
/// </summary>
internal static class SchemaCacheLoader
{
    /// <summary>
    /// Loads all schema metadata for a database using a single connection
    /// to avoid exhausting the connection pool.
    /// </summary>
    public static async Task<DatabaseCacheData> LoadFullAsync(
        string connectionString, string database, CancellationToken ct = default)
    {
        var result = new DatabaseCacheData();

        await Task.Run(() =>
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();

            result.Objects = LoadObjects(conn, ct);
            result.Columns = LoadColumns(conn, ct);
            result.Indexes = LoadIndexes(conn, ct);
            result.ForeignKeys = LoadForeignKeys(conn, ct);
            result.Parameters = LoadParameters(conn, ct);

            // Merge definitions into objects
            var definitions = LoadDefinitions(conn, ct);
            foreach (var obj in result.Objects)
            {
                string key = $"{obj.SchemaName}.{obj.ObjectName}";
                if (definitions.TryGetValue(key, out string def))
                    obj.Definition = def;
            }

            ApplyEncryptedModules(conn, connectionString, database, result, ct);
        }, ct);

        return result;
    }

    /// <summary>
    /// Returns only objects modified since the given date.
    /// </summary>
    public static List<CachedObject> LoadModifiedSince(string connectionString, DateTime since, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name, o.type, o.create_date, o.modify_date,
                   p.rows AS row_count
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            LEFT JOIN (
                SELECT object_id, SUM(rows) rows
                FROM sys.partitions WHERE index_id IN (0,1)
                GROUP BY object_id
            ) p ON o.object_id = p.object_id
            WHERE o.type IN ('U','V','P','FN','IF','TF','SN','TT')
              AND o.is_ms_shipped = 0
              AND o.modify_date > @since";

        var objects = new List<CachedObject>();
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@since", since);
        cmd.CommandTimeout = 30;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            objects.Add(ReadObject(reader));
        }
        return objects;
    }

    /// <summary>
    /// Overload that reuses an existing open connection.
    /// </summary>
    public static List<CachedObject> LoadModifiedSince(SqlConnection conn, DateTime since, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name, o.type, o.create_date, o.modify_date,
                   p.rows AS row_count
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            LEFT JOIN (
                SELECT object_id, SUM(rows) rows
                FROM sys.partitions WHERE index_id IN (0,1)
                GROUP BY object_id
            ) p ON o.object_id = p.object_id
            WHERE o.type IN ('U','V','P','FN','IF','TF','SN','TT')
              AND o.is_ms_shipped = 0
              AND o.modify_date > @since";

        var objects = new List<CachedObject>();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@since", since);
        cmd.CommandTimeout = 30;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            objects.Add(ReadObject(reader));
        }
        return objects;
    }

    private static List<CachedObject> LoadObjects(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name, o.type, o.create_date, o.modify_date,
                   p.rows AS row_count
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            LEFT JOIN (
                SELECT object_id, SUM(rows) rows
                FROM sys.partitions WHERE index_id IN (0,1)
                GROUP BY object_id
            ) p ON o.object_id = p.object_id
            WHERE o.type IN ('U','V','P','FN','IF','TF','SN','TT') AND o.is_ms_shipped = 0";

        var objects = new List<CachedObject>();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            objects.Add(ReadObject(reader));
        }
        return objects;
    }

    private static CachedObject ReadObject(SqlDataReader reader)
    {
        return new CachedObject
        {
            SchemaName = reader.GetString(0),
            ObjectName = reader.GetString(1),
            ObjectType = reader.GetString(2).Trim(),
            CreateDate = reader.IsDBNull(3) ? null : (DateTime?)reader.GetDateTime(3),
            ModifyDate = reader.IsDBNull(4) ? null : (DateTime?)reader.GetDateTime(4),
            RowCount = reader.IsDBNull(5) ? 0 : Convert.ToInt64(reader.GetValue(5))
        };
    }

    private static List<CachedColumn> LoadColumns(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name AS table_name, c.name AS column_name,
                   c.column_id, t.name AS type_name,
                   c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity, c.is_computed,
                   cc.definition AS computed_def,
                   dc.definition AS default_def,
                   ep.value AS description
            FROM sys.columns c
            JOIN sys.objects o ON c.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.types t ON c.user_type_id = t.user_type_id
            LEFT JOIN sys.computed_columns cc ON c.object_id = cc.object_id AND c.column_id = cc.column_id
            LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
            LEFT JOIN sys.extended_properties ep ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
            WHERE o.type IN ('U','V') AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name, c.column_id";

        var columns = new List<CachedColumn>();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            columns.Add(new CachedColumn
            {
                SchemaName = reader.GetString(0),
                TableName = reader.GetString(1),
                ColumnName = reader.GetString(2),
                Ordinal = reader.GetInt32(3),
                DataType = FormatDataType(
                    reader.GetString(4),
                    reader.GetInt16(5),
                    reader.GetByte(6),
                    reader.GetByte(7)),
                IsNullable = reader.GetBoolean(8),
                IsIdentity = reader.GetBoolean(9),
                IsComputed = reader.GetBoolean(10),
                ComputedDefinition = reader.IsDBNull(11) ? null : reader.GetString(11),
                DefaultDefinition = reader.IsDBNull(12) ? null : reader.GetString(12),
                Description = reader.IsDBNull(13) ? null : reader.GetValue(13)?.ToString()
            });
        }
        return columns;
    }

    private static List<CachedIndex> LoadIndexes(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name AS table_name,
                   ix.name AS index_name, ix.type_desc, ix.is_unique, ix.is_primary_key,
                   STUFF((
                       SELECT ', ' + col2.name + CASE WHEN ic2.is_descending_key = 1 THEN ' DESC' ELSE '' END
                       FROM sys.index_columns ic2
                       JOIN sys.columns col2 ON ic2.object_id = col2.object_id AND ic2.column_id = col2.column_id
                       WHERE ic2.object_id = ix.object_id AND ic2.index_id = ix.index_id AND ic2.is_included_column = 0
                       ORDER BY ic2.key_ordinal
                       FOR XML PATH(''), TYPE
                   ).value('.', 'nvarchar(max)'), 1, 2, '') AS key_columns,
                   STUFF((
                       SELECT ', ' + col3.name
                       FROM sys.index_columns ic3
                       JOIN sys.columns col3 ON ic3.object_id = col3.object_id AND ic3.column_id = col3.column_id
                       WHERE ic3.object_id = ix.object_id AND ic3.index_id = ix.index_id AND ic3.is_included_column = 1
                       ORDER BY ic3.key_ordinal
                       FOR XML PATH(''), TYPE
                   ).value('.', 'nvarchar(max)'), 1, 2, '') AS included_columns,
                   ix.filter_definition
            FROM sys.indexes ix
            JOIN sys.objects o ON ix.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.type IN ('U') AND o.is_ms_shipped = 0 AND ix.type > 0
            ORDER BY s.name, o.name, ix.name";

        var indexes = new List<CachedIndex>();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            indexes.Add(new CachedIndex
            {
                SchemaName = reader.GetString(0),
                TableName = reader.GetString(1),
                IndexName = reader.IsDBNull(2) ? null : reader.GetString(2),
                IndexType = reader.GetString(3),
                IsUnique = reader.GetBoolean(4),
                IsPrimaryKey = reader.GetBoolean(5),
                KeyColumns = reader.IsDBNull(6) ? "" : reader.GetString(6),
                IncludedColumns = reader.IsDBNull(7) ? null : reader.GetString(7),
                FilterDefinition = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return indexes;
    }

    private static List<CachedForeignKey> LoadForeignKeys(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name AS table_name,
                   fk.name AS fk_name,
                   STUFF((
                       SELECT ', ' + col2.name
                       FROM sys.foreign_key_columns fkc2
                       JOIN sys.columns col2 ON fkc2.parent_object_id = col2.object_id AND fkc2.parent_column_id = col2.column_id
                       WHERE fkc2.constraint_object_id = fk.object_id
                       ORDER BY fkc2.constraint_column_id
                       FOR XML PATH(''), TYPE
                   ).value('.', 'nvarchar(max)'), 1, 2, '') AS fk_columns,
                   rs.name AS ref_schema, rt.name AS ref_table,
                   STUFF((
                       SELECT ', ' + rcol2.name
                       FROM sys.foreign_key_columns fkc3
                       JOIN sys.columns rcol2 ON fkc3.referenced_object_id = rcol2.object_id AND fkc3.referenced_column_id = rcol2.column_id
                       WHERE fkc3.constraint_object_id = fk.object_id
                       ORDER BY fkc3.constraint_column_id
                       FOR XML PATH(''), TYPE
                   ).value('.', 'nvarchar(max)'), 1, 2, '') AS ref_columns,
                   fk.delete_referential_action_desc, fk.update_referential_action_desc
            FROM sys.foreign_keys fk
            JOIN sys.objects o ON fk.parent_object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.objects rt ON fk.referenced_object_id = rt.object_id
            JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
            WHERE o.is_ms_shipped = 0
            ORDER BY s.name, o.name, fk.name";

        var keys = new List<CachedForeignKey>();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            keys.Add(new CachedForeignKey
            {
                SchemaName = reader.GetString(0),
                TableName = reader.GetString(1),
                ForeignKeyName = reader.GetString(2),
                Columns = reader.GetString(3),
                ReferencedSchema = reader.GetString(4),
                ReferencedTable = reader.GetString(5),
                ReferencedColumns = reader.GetString(6),
                DeleteAction = reader.GetString(7),
                UpdateAction = reader.GetString(8)
            });
        }
        return keys;
    }

    private static List<CachedParameter> LoadParameters(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name AS object_name,
                   p.name AS param_name, p.parameter_id,
                   t.name AS type_name, p.max_length, p.is_output, p.has_default_value
            FROM sys.parameters p
            JOIN sys.objects o ON p.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.types t ON p.user_type_id = t.user_type_id
            WHERE o.type IN ('P','FN','IF','TF') AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name, p.parameter_id";

        var parameters = new List<CachedParameter>();
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            parameters.Add(new CachedParameter
            {
                SchemaName = reader.GetString(0),
                ObjectName = reader.GetString(1),
                ParameterName = reader.GetString(2),
                Ordinal = reader.GetInt32(3),
                DataType = reader.GetString(4),
                MaxLength = reader.GetInt16(5),
                IsOutput = reader.GetBoolean(6),
                HasDefault = reader.GetBoolean(7)
            });
        }
        return parameters;
    }

    /// <summary>
    /// Returns a dictionary of "schema.name" → definition text for procs, functions, and views.
    /// </summary>
    private static Dictionary<string, string> LoadDefinitions(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT s.name AS schema_name, o.name, m.definition
            FROM sys.sql_modules m
            JOIN sys.objects o ON m.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.is_ms_shipped = 0";

        var defs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 };

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            string key = $"{reader.GetString(0)}.{reader.GetString(1)}";
            string def = reader.IsDBNull(2) ? null : reader.GetString(2);
            defs[key] = def;
        }
        return defs;
    }

    /// <summary>
    /// Flags the modules that came back with a NULL definition — they are encrypted — and, when the user has
    /// turned decryption on, fills their definitions in.
    ///
    /// The flag is set whether or not decryption runs: everything downstream (the schema viewer, the export,
    /// quick info) needs to tell "this object has no definition because it is encrypted" apart from "this
    /// object has no definition because it is a table". Marking it is free; recovering the text is not, which
    /// is why only the second half is behind a setting. See <see cref="ModuleDecryptionService"/> for what
    /// recovering it costs.
    /// </summary>
    private static void ApplyEncryptedModules(SqlConnection conn, string connectionString, string database, DatabaseCacheData result, CancellationToken ct)
    {
        List<EncryptedModule> encrypted;
        try
        {
            encrypted = ModuleDecryptionService.ListEncryptedModules(conn, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Losing the encrypted-module list costs a label on a handful of objects, never the load.
            Diagnostics.SQLExtendedLog.Warning("SchemaCache", $"Could not list encrypted modules in {database}", ex);
            return;
        }

        if (encrypted.Count == 0) return;

        var byKey = new Dictionary<string, CachedObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in result.Objects)
            byKey[$"{obj.SchemaName}.{obj.ObjectName}"] = obj;

        // Only the modules the cache actually holds are decrypted. LoadObjects does not cache triggers, and
        // decrypting one would mean ALTERing it — Sch-M lock, recompile — for a definition that is then
        // thrown away. Triggers are still decrypted on demand by the schema viewer, which does keep the
        // answer.
        var wanted = new List<EncryptedModule>();
        foreach (var module in encrypted)
        {
            if (!byKey.TryGetValue(module.Key, out var obj)) continue;
            obj.IsEncrypted = true;
            wanted.Add(module);
        }

        if (wanted.Count == 0 || !ModuleDecryptionService.Enabled) return;

        // Whatever happens in here costs the definitions and nothing else. Letting it throw would fail the
        // whole cache load — every table, column and index — because some module could not be decrypted,
        // which is wildly out of proportion. The reason is recorded on ModuleDecryptionService.LastRun and
        // shown by the Schema Cache window; it is not dropped.
        try
        {
            var decrypted = ModuleDecryptionService.Decrypt(connectionString, database ?? conn.Database, wanted, progress: null, ct: ct);
            foreach (var pair in decrypted.Definitions)
            {
                if (byKey.TryGetValue(pair.Key, out var obj))
                    obj.Definition = pair.Value;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Also recorded on ModuleDecryptionService.LastRun, which the Schema Cache window shows - but that
            // is one slot, overwritten by the next database to load.
            Diagnostics.SQLExtendedLog.Warning("Decryption", $"Module decryption failed in {database}", ex);
        }
    }

    /// <summary>
    /// Formats a SQL Server data type with its length/precision/scale for display.
    /// </summary>
    internal static string FormatDataType(string typeName, short maxLength, byte precision, byte scale)
    {
        switch (typeName.ToLowerInvariant())
        {
            case "varchar":
            case "nvarchar":
            case "char":
            case "nchar":
            case "binary":
            case "varbinary":
                string len = maxLength == -1 ? "MAX" :
                    (typeName.StartsWith("n", StringComparison.OrdinalIgnoreCase) ? (maxLength / 2).ToString() : maxLength.ToString());
                return $"{typeName}({len})";

            case "decimal":
            case "numeric":
                return $"{typeName}({precision},{scale})";

            case "float":
                return precision <= 24 ? "real" : "float";

            case "datetime2":
            case "datetimeoffset":
            case "time":
                return scale > 0 ? $"{typeName}({scale})" : typeName;

            default:
                return typeName;
        }
    }
}

/// <summary>
/// Container for all schema data loaded for a single database.
/// </summary>
internal sealed class DatabaseCacheData
{
    public List<CachedObject> Objects { get; set; } = new();
    public List<CachedColumn> Columns { get; set; } = new();
    public List<CachedIndex> Indexes { get; set; } = new();
    public List<CachedForeignKey> ForeignKeys { get; set; } = new();
    public List<CachedParameter> Parameters { get; set; } = new();
}
