using System;
using System.Collections.Generic;
using System.Threading;
using SQLExtended.Validation.Models;
using Microsoft.Data.SqlClient;

namespace SQLExtended.Validation;

/// <summary>
/// Validates schema references for one or more databases on a server.
///
/// Reads every reference made by views/procs/functions/triggers from
/// <c>sys.sql_expression_dependencies</c>, then verifies each target exists:
/// local objects must bind, cross-database targets must exist in their database,
/// and 4-part (linked server) targets must be registered in <c>sys.servers</c>.
/// Classification is delegated to the pure <see cref="DependencyClassifier"/>.
/// </summary>
internal static class SchemaValidationService
{
    /// <summary>
    /// Runs validation synchronously — call from a background thread.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(
        string connectionString,
        string connectionKey,
        IEnumerable<string> databases,
        CancellationToken token,
        IProgress<ValidationProgress> progress = null)
    {
        var results = new List<ValidationIssue>();
        var databaseList = databases as IReadOnlyList<string> ?? new List<string>(databases);

        progress?.Report(new ValidationProgress(0, databaseList.Count, "Resolving server metadata…"));

        string masterConn = ConnectionHelper.GetConnectionStringForDatabase(connectionString, "master");

        // Resolve the server-wide lookups once.
        var linkedServers = QueryStringSet(masterConn,
            "SELECT name FROM sys.servers");
        var existingDatabases = QueryStringSet(masterConn,
            "SELECT name FROM sys.databases");

        // System databases hold infrastructure objects referenced from everywhere: master (utility
        // procs like Ola Hallengren's CommandExecute, sp_WhoIsActive) and msdb (backup/restore
        // history, SQL Agent, Database Mail — read by sp_Blitz/sp_AllNightLog). Treat them as
        // infrastructure: cross-database refs to them are ignored, and local refs that resolve there
        // are suppressed.
        var infrastructureDatabases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "master", "msdb", "model", "tempdb"
        };

        // Per-database object lookups, populated lazily and cached. A null value means the
        // database could not be enumerated (offline / no permission).
        var objectCache = new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase);

        ISet<string> ObjectsInDatabase(string db)
        {
            if (objectCache.TryGetValue(db, out var cached))
                return cached;

            ISet<string> objects = null;
            try
            {
                string dbConn = ConnectionHelper.GetConnectionStringForDatabase(connectionString, db);
                objects = QueryStringSet(dbConn,
                    "SELECT SCHEMA_NAME(schema_id) + '.' + name FROM sys.objects WHERE type IN ('U','V','P','FN','IF','TF','TR','SN','TT','SO')");
            }
            catch
            {
                objects = null;
            }

            objectCache[db] = objects;
            return objects;
        }

        for (int i = 0; i < databaseList.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            string database = databaseList[i];
            progress?.Report(new ValidationProgress(i, databaseList.Count, $"Checking {database}…"));

            string dbConn = ConnectionHelper.GetConnectionStringForDatabase(connectionString, database);

            // Locally-defined names (CTEs / aliases / table variables / temp tables) per referencing
            // module, parsed from its definition on first need. object_ids are unique within a database,
            // so this cache is scoped to the current database.
            var moduleLocalNames = new Dictionary<int, ISet<string>>();
            ISet<string> LocalNamesFor(int moduleId)
            {
                if (moduleLocalNames.TryGetValue(moduleId, out var cached))
                    return cached;
                ISet<string> names;
                try { names = ModuleLocalNameScanner.Scan(QueryModuleDefinition(dbConn, moduleId)); }
                catch { names = ModuleLocalNameScanner.Scan(null); }
                moduleLocalNames[moduleId] = names;
                return names;
            }

            foreach (var dep in ReadDependencies(dbConn, token))
            {
                var current = dep;
                var issue = DependencyClassifier.Classify(
                    current, database, linkedServers, existingDatabases, ObjectsInDatabase, infrastructureDatabases,
                    () => LocalNamesFor(current.ReferencingId));

                if (issue != null)
                {
                    issue.ConnectionString = connectionString;
                    issue.ConnectionKey = connectionKey;
                    results.Add(issue);
                }
            }
        }

        progress?.Report(new ValidationProgress(databaseList.Count, databaseList.Count, "Done"));
        return results;
    }

    private static IEnumerable<RawDependency> ReadDependencies(string connectionString, CancellationToken token)
    {
        const string sql = @"
            SELECT o.type AS RefType,
                   SCHEMA_NAME(o.schema_id) AS RefSchema,
                   o.name AS RefName,
                   d.referenced_server_name,
                   d.referenced_database_name,
                   d.referenced_schema_name,
                   d.referenced_entity_name,
                   d.referenced_id,
                   d.referenced_minor_id,
                   d.is_caller_dependent,
                   d.is_ambiguous,
                   d.referencing_id
            FROM sys.sql_expression_dependencies d
            JOIN sys.objects o ON o.object_id = d.referencing_id
            WHERE o.type IN ('V','P','FN','IF','TF','TR')
              AND d.referenced_entity_name IS NOT NULL";

        var rows = new List<RawDependency>();
        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.CommandTimeout = 60;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        rows.Add(new RawDependency
                        {
                            ReferencingType = GetString(reader, 0),
                            ReferencingSchema = GetString(reader, 1),
                            ReferencingName = GetString(reader, 2),
                            ReferencedServer = GetString(reader, 3),
                            ReferencedDatabase = GetString(reader, 4),
                            ReferencedSchema = GetString(reader, 5),
                            ReferencedEntity = GetString(reader, 6),
                            ReferencedIdIsNull = reader.IsDBNull(7),
                            IsCallerDependent = !reader.IsDBNull(9) && reader.GetBoolean(9),
                            IsAmbiguous = !reader.IsDBNull(10) && reader.GetBoolean(10),
                            ReferencingId = reader.GetInt32(11)
                        });
                    }
                }
            }
        }
        return rows;
    }

    private static ISet<string> QueryStringSet(string connectionString, string sql)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.CommandTimeout = 30;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                            set.Add(reader.GetString(0));
                    }
                }
            }
        }
        return set;
    }

    private static string GetString(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Returns the T-SQL definition of a module by object_id, or null if unavailable.</summary>
    private static string QueryModuleDefinition(string connectionString, int objectId)
    {
        using (var conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT OBJECT_DEFINITION(@id)";
                cmd.CommandTimeout = 30;
                cmd.Parameters.AddWithValue("@id", objectId);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : (string)result;
            }
        }
    }
}

/// <summary>Progress update reported while validation runs: how many databases are done, the total, and a status line.</summary>
internal readonly struct ValidationProgress
{
    public ValidationProgress(int completed, int total, string message)
    {
        Completed = completed;
        Total = total;
        Message = message;
    }

    public int Completed { get; }
    public int Total { get; }
    public string Message { get; }
}
