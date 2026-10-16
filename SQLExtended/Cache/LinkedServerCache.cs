using Microsoft.Data.SqlClient;
using SQLExtended.Cache.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SQLExtended.Cache;

/// <summary>
/// What IntelliSense knows about the local server's linked servers, in three layers that cost progressively more:
/// <list type="number">
/// <item>the linked server names — one query against the local <c>sys.servers</c>;</item>
/// <item>each linked server's databases — <c>sp_catalogs</c>, which reaches the remote server;</item>
/// <item>a remote database's tables and views (<c>sp_tables_ex</c>), and a remote table's columns
/// (<c>sp_columns_ex</c>, read one table at a time, as each is used).</item>
/// </list>
/// The remote reads go through the catalog procedures rather than <c>OPENQUERY</c> on the remote <c>sys.</c>
/// views because they work through any provider (Oracle, ODBC, another SQL Server alike) and need neither RPC Out
/// nor a SQL Server on the far end. They do not list procedures — there is no <c>sp_procedures_ex</c> — so
/// <c>EXEC LinkedSrv.db.</c> stops at the database.
///
/// <para>Keyed by the <i>local</i> server's connection key, like <see cref="SystemCatalogCache"/>, and for the same
/// reasons nothing here is persisted and every load is memoised both while it runs and after it fails: completion
/// asks on every keystroke, and a linked server that is down costs a full provider timeout per ask. A failure stays
/// until <see cref="Refresh"/> (the Schema Cache window) or <see cref="Clear"/> (Clear All Cache).</para>
/// </summary>
internal sealed class LinkedServerCache
{
    private static readonly Lazy<LinkedServerCache> _instance = new(() => new LinkedServerCache());
    public static LinkedServerCache Instance => _instance.Value;

    // One slot per thing loaded: the server list, a server's databases, a database's objects, a table's columns.
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);

    // The connection string each local server was last reached through, so the Schema Cache window can start a
    // load from a context menu, where there is no editor connection to hand.
    private readonly ConcurrentDictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised on a worker thread whenever a load finishes, fails, or is discarded.</summary>
    public event EventHandler Changed;

    private LinkedServerCache() { }

    // --- Level 1: names ---

    /// <summary>True once the local server's linked server list is in memory; otherwise starts loading it. Never blocks.</summary>
    public bool EnsureServers(string connectionString, string connectionKey) =>
        Ensure(Key("s", connectionKey), connectionKey, connectionString, LoadServers);

    public IReadOnlyList<LinkedServerInfo> GetServers(string connectionKey) =>
        Get<List<LinkedServerInfo>>(Key("s", connectionKey)) ?? (IReadOnlyList<LinkedServerInfo>)Array.Empty<LinkedServerInfo>();

    /// <summary>The linked server of that name, or null — also null while the list is still loading.</summary>
    public LinkedServerInfo FindServer(string connectionKey, string name) =>
        string.IsNullOrEmpty(name) ? null : GetServers(connectionKey).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    // --- Level 2: databases ---

    public bool EnsureDatabases(string connectionString, string connectionKey, string server) =>
        Ensure(Key("d", connectionKey, server), connectionKey, connectionString, cs => LoadDatabases(cs, connectionKey, server));

    public IReadOnlyList<string> GetDatabases(string connectionKey, string server) =>
        Get<List<string>>(Key("d", connectionKey, server)) ?? (IReadOnlyList<string>)Array.Empty<string>();

    // --- Level 3: tables, views and columns ---

    public bool EnsureObjects(string connectionString, string connectionKey, string server, string database) =>
        Ensure(Key("o", connectionKey, server, database), connectionKey, connectionString, cs => LoadObjects(cs, server, database));

    public IReadOnlyList<LinkedObject> GetObjects(string connectionKey, string server, string database) =>
        Get<List<LinkedObject>>(Key("o", connectionKey, server, database)) ?? (IReadOnlyList<LinkedObject>)Array.Empty<LinkedObject>();

    public bool EnsureColumns(string connectionString, string connectionKey, string server, string database, string schema, string table) =>
        Ensure(Key("c", connectionKey, server, database, schema, table), connectionKey, connectionString, cs => LoadColumns(cs, server, database, schema, table));

    public IReadOnlyList<CachedColumn> GetColumns(string connectionKey, string server, string database, string schema, string table) =>
        Get<List<CachedColumn>>(Key("c", connectionKey, server, database, schema, table)) ?? (IReadOnlyList<CachedColumn>)Array.Empty<CachedColumn>();

    // --- Schema Cache window ---

    /// <summary>Every local server whose linked server list has been asked for.</summary>
    public IReadOnlyList<string> ConnectionKeys =>
        _slots.Keys.Select(Split).Where(p => p[0] == "s").Select(p => p[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public string GetConnectionString(string connectionKey) =>
        connectionKey != null && _connectionStrings.TryGetValue(connectionKey, out string cs) ? cs : null;

    /// <summary>The state of the local server's linked server list, and its error if it failed.</summary>
    public LinkedLoadState GetServersState(string connectionKey, out string error) => StateOf(Key("s", connectionKey), out error, out _);

    /// <summary>Each linked server with whatever has been loaded beneath it.</summary>
    public IReadOnlyList<LinkedServerSnapshot> GetSnapshot(string connectionKey)
    {
        var result = new List<LinkedServerSnapshot>();
        var keys = _slots.Keys.Select(Split).ToList();

        foreach (var server in GetServers(connectionKey))
        {
            var snapshot = new LinkedServerSnapshot { Server = server };
            snapshot.DatabasesState = StateOf(Key("d", connectionKey, server.Name), out string dbError, out var loadedUtc);
            snapshot.DatabasesError = dbError;
            snapshot.DatabasesLoadedUtc = loadedUtc;

            // The databases listed, plus any whose objects were loaded without the list (a typed "LS.db." while the
            // list itself failed — sp_catalogs is the one provider call some drivers do not implement).
            var names = new List<string>(GetDatabases(connectionKey, server.Name));
            foreach (var p in keys)
            {
                if (p[0] == "o" && Same(p[1], connectionKey) && Same(p[2], server.Name) && !names.Contains(p[3], StringComparer.OrdinalIgnoreCase))
                    names.Add(p[3]);
            }

            snapshot.Databases = names
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(db =>
                {
                    var d = new LinkedDatabaseSnapshot { Name = db };
                    d.ObjectsState = StateOf(Key("o", connectionKey, server.Name, db), out string objError, out _);
                    d.ObjectsError = objError;
                    foreach (var o in GetObjects(connectionKey, server.Name, db))
                    {
                        if (string.Equals(o.Type, "TABLE", StringComparison.OrdinalIgnoreCase)) d.TableCount++;
                        else if (string.Equals(o.Type, "VIEW", StringComparison.OrdinalIgnoreCase)) d.ViewCount++;
                        else d.OtherCount++;
                    }
                    d.TablesWithColumns = keys.Count(p => p[0] == "c" && Same(p[1], connectionKey) && Same(p[2], server.Name) && Same(p[3], db) &&
                                                          StateOf(Key(p), out _, out _) == LinkedLoadState.Ready);
                    return d;
                })
                .ToList();

            result.Add(snapshot);
        }

        return result;
    }

    /// <summary>
    /// Forgets everything loaded for one linked server — its databases, objects and columns, failures included —
    /// and, with <paramref name="reloadDatabases"/>, starts reading its database list again. A null server re-reads
    /// the local list of linked servers instead.
    /// </summary>
    public void Refresh(string connectionKey, string server, bool reloadDatabases, string connectionString = null)
    {
        RememberConnection(connectionKey, connectionString);
        foreach (var key in _slots.Keys.ToList())
        {
            var p = Split(key);
            bool match = server == null
                ? p[0] == "s" && Same(p[1], connectionKey)
                : p[0] != "s" && Same(p[1], connectionKey) && Same(p[2], server);
            if (match)
                _slots.TryRemove(key, out _);
        }

        string cs = GetConnectionString(connectionKey);
        if (server == null)
            EnsureServers(cs, connectionKey);
        else if (reloadDatabases)
            EnsureDatabases(cs, connectionKey, server);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets one remote database's objects and columns, then reads its objects again.</summary>
    public void RefreshDatabase(string connectionKey, string server, string database, string connectionString = null)
    {
        RememberConnection(connectionKey, connectionString);
        foreach (var key in _slots.Keys.ToList())
        {
            var p = Split(key);
            if ((p[0] == "o" || p[0] == "c") && Same(p[1], connectionKey) && Same(p[2], server) && Same(p[3], database))
                _slots.TryRemove(key, out _);
        }

        EnsureObjects(GetConnectionString(connectionKey), connectionKey, server, database);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clear All Cache. Loads still in flight finish into slots nothing points at any more.</summary>
    public void Clear()
    {
        _slots.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --- Slots ---

    private sealed class Slot
    {
        public LinkedLoadState State = LinkedLoadState.NotLoaded;
        public object Value;
        public string Error;
        public DateTime? LoadedUtc;
    }

    private bool Ensure<T>(string key, string connectionKey, string connectionString, Func<string, T> load) where T : class
    {
        if (string.IsNullOrEmpty(connectionKey))
            return false;
        RememberConnection(connectionKey, connectionString);

        var slot = _slots.GetOrAdd(key, _ => new Slot());
        lock (slot)
        {
            if (slot.State == LinkedLoadState.Ready)
                return true;
            if (slot.State != LinkedLoadState.NotLoaded || string.IsNullOrEmpty(connectionString))
                return false;
            slot.State = LinkedLoadState.Loading;
        }

        _ = Task.Run(() =>
        {
            T value = null;
            string error = null;
            try
            {
                value = load(connectionString);
            }
            catch (Exception ex)
            {
                // A linked server that is down, a login with no mapping, a provider without catalog support: all
                // ordinary, all memoised below, and the Schema Cache window shows the message. Logged so a list that
                // went quiet can still be traced from the session log.
                error = ex.Message;
                Diagnostics.SQLExtendedLog.Info("LinkedServers", $"{Describe(key)} failed: {ex.Message}");
            }

            lock (slot)
            {
                if (error == null) { slot.Value = value; slot.LoadedUtc = DateTime.UtcNow; slot.State = LinkedLoadState.Ready; }
                else { slot.Error = error; slot.State = LinkedLoadState.Failed; }
            }
            Changed?.Invoke(this, EventArgs.Empty);
        });

        return false;
    }

    private void RememberConnection(string connectionKey, string connectionString)
    {
        if (!string.IsNullOrEmpty(connectionKey) && !string.IsNullOrEmpty(connectionString))
            _connectionStrings[connectionKey] = connectionString;
    }

    private T Get<T>(string key) where T : class
    {
        if (!_slots.TryGetValue(key, out var slot))
            return null;
        lock (slot)
            return slot.State == LinkedLoadState.Ready ? slot.Value as T : null;
    }

    private LinkedLoadState StateOf(string key, out string error, out DateTime? loadedUtc)
    {
        error = null;
        loadedUtc = null;
        if (!_slots.TryGetValue(key, out var slot))
            return LinkedLoadState.NotLoaded;
        lock (slot)
        {
            error = slot.Error;
            loadedUtc = slot.LoadedUtc;
            return slot.State;
        }
    }

    // A unit separator cannot appear in a server, database or object name, so it splits the key back unambiguously.
    private const char Sep = '\u001F';
    private static string Key(params string[] parts) => string.Join(Sep.ToString(), parts.Select(p => p ?? ""));
    private static string[] Split(string key) => key.Split(Sep);
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Describe(string key)
    {
        var p = Split(key);
        return p[0] switch
        {
            "s" => $"Linked server list on {p[1]}",
            "d" => $"Databases of linked server [{p[2]}] on {p[1]}",
            "o" => $"Objects of [{p[2]}].[{p[3]}] on {p[1]}",
            _ => $"Columns of [{p[2]}].[{p[3]}].[{p[4]}].[{p[5]}] on {p[1]}"
        };
    }

    // --- Loading ---

    private static List<LinkedServerInfo> LoadServers(string connectionString)
    {
        var servers = new List<LinkedServerInfo>();
        using var conn = SqlConnectionFactory.Create(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(LinkedServerSql.Servers, conn) { CommandTimeout = 15 };
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            servers.Add(new LinkedServerInfo
            {
                Name = reader.GetString(0),
                Product = reader.IsDBNull(1) ? null : reader.GetString(1),
                Provider = reader.IsDBNull(2) ? null : reader.GetString(2),
                DataSource = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsDataAccessEnabled = !reader.IsDBNull(4) && reader.GetBoolean(4),
                IsRpcOutEnabled = !reader.IsDBNull(5) && reader.GetBoolean(5)
            });
        }
        return servers;
    }

    private List<string> LoadDatabases(string connectionString, string connectionKey, string server)
    {
        // Without data access every catalog procedure fails with the same error after a round trip; saying so
        // directly is both quicker and a clearer message in the Schema Cache window.
        var info = FindServer(connectionKey, server);
        if (info != null && !info.IsDataAccessEnabled)
            throw new InvalidOperationException("Data access is not enabled for this linked server (sp_serveroption 'data access').");

        var names = new List<string>();
        using var conn = SqlConnectionFactory.Create(connectionString);
        conn.Open();
        using var cmd = new SqlCommand("sys.sp_catalogs", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = LinkedServerSql.RemoteTimeoutSeconds };
        cmd.Parameters.AddWithValue("@server_name", server);
        using var reader = cmd.ExecuteReader();
        int name = reader.GetOrdinal("CATALOG_NAME");
        while (reader.Read())
            if (!reader.IsDBNull(name))
                names.Add(reader.GetString(name));
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static List<LinkedObject> LoadObjects(string connectionString, string server, string database)
    {
        var objects = new List<LinkedObject>();
        using var conn = SqlConnectionFactory.Create(connectionString);
        conn.Open();
        using var cmd = new SqlCommand("sys.sp_tables_ex", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = LinkedServerSql.RemoteTimeoutSeconds };
        cmd.Parameters.AddWithValue("@table_server", server);
        cmd.Parameters.AddWithValue("@table_catalog", database);
        using var reader = cmd.ExecuteReader();
        int schema = reader.GetOrdinal("TABLE_SCHEM"), name = reader.GetOrdinal("TABLE_NAME"), type = reader.GetOrdinal("TABLE_TYPE");
        while (reader.Read())
        {
            string s = reader.IsDBNull(schema) ? null : reader.GetString(schema);
            string t = reader.IsDBNull(type) ? null : reader.GetString(type);
            if (reader.IsDBNull(name) || !LinkedServerSql.IsUserObject(s, t))
                continue;
            objects.Add(new LinkedObject { Schema = s, Name = reader.GetString(name), Type = t.ToUpperInvariant() });
        }
        return objects;
    }

    private static List<CachedColumn> LoadColumns(string connectionString, string server, string database, string schema, string table)
    {
        var columns = new List<CachedColumn>();
        using var conn = SqlConnectionFactory.Create(connectionString);
        conn.Open();
        using var cmd = new SqlCommand("sys.sp_columns_ex", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = LinkedServerSql.RemoteTimeoutSeconds };
        cmd.Parameters.AddWithValue("@table_server", server);
        cmd.Parameters.AddWithValue("@table_catalog", (object)database ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@table_schema", (object)schema ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@table_name", table);
        using var reader = cmd.ExecuteReader();
        int tSchema = reader.GetOrdinal("TABLE_SCHEM"), tName = reader.GetOrdinal("TABLE_NAME"), cName = reader.GetOrdinal("COLUMN_NAME");
        int typeName = reader.GetOrdinal("TYPE_NAME"), size = reader.GetOrdinal("COLUMN_SIZE"), digits = reader.GetOrdinal("DECIMAL_DIGITS");
        int nullable = reader.GetOrdinal("IS_NULLABLE"), ordinal = reader.GetOrdinal("ORDINAL_POSITION");

        while (reader.Read())
        {
            // The name arguments are LIKE patterns, so "Order_Lines" also matches "OrderXLines": keep exact matches only.
            if (!string.Equals(reader.IsDBNull(tName) ? null : reader.GetString(tName), table, StringComparison.OrdinalIgnoreCase))
                continue;
            if (schema != null && !string.Equals(reader.IsDBNull(tSchema) ? null : reader.GetString(tSchema), schema, StringComparison.OrdinalIgnoreCase))
                continue;

            columns.Add(new CachedColumn
            {
                SchemaName = schema,
                TableName = table,
                ColumnName = reader.GetString(cName),
                Ordinal = IntOrNull(reader, ordinal) ?? columns.Count + 1,
                DataType = LinkedServerSql.FormatType(reader.IsDBNull(typeName) ? null : reader.GetString(typeName), IntOrNull(reader, size), IntOrNull(reader, digits)),
                IsNullable = !reader.IsDBNull(nullable) && string.Equals(Convert.ToString(reader.GetValue(nullable)).Trim(), "YES", StringComparison.OrdinalIgnoreCase)
            });
        }

        columns.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
        return columns;
    }

    private static int? IntOrNull(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
}
