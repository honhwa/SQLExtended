using System;
using System.Collections.Generic;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Describes a DBCC (Database Console Command) for IntelliSense completion: its name,
/// category, a concise syntax hint, and a one-line description.
/// </summary>
internal sealed class DbccCommand
{
    public DbccCommand(string name, string category, string syntax, string description)
    {
        Name = name;
        Category = category;
        Syntax = syntax;
        Description = description;
    }

    public string Name { get; }
    public string Category { get; }
    public string Syntax { get; }
    public string Description { get; }
}

/// <summary>
/// Catalog of DBCC commands, offered after the user types "DBCC ". Covers the
/// documented validation, maintenance, informational, and miscellaneous commands,
/// plus a few widely-used undocumented ones (PAGE, IND, LOG, …).
/// </summary>
internal static class SqlDbccCommands
{
    private static readonly List<DbccCommand> _all = Build();

    private static readonly Dictionary<string, DbccCommand> _byName = BuildIndex(_all);

    public static IReadOnlyList<DbccCommand> All => _all;

    /// <summary>Looks up a DBCC command by name, case-insensitively. Null if unknown.</summary>
    public static DbccCommand Find(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return _byName.TryGetValue(name, out var cmd) ? cmd : null;
    }

    private static Dictionary<string, DbccCommand> BuildIndex(List<DbccCommand> all)
    {
        var dict = new Dictionary<string, DbccCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in all)
            dict[c.Name] = c;
        return dict;
    }

    private static DbccCommand C(string name, string category, string syntax, string description)
        => new DbccCommand(name, category, syntax, description);

    private static List<DbccCommand> Build()
    {
        const string Validation = "Validation";
        const string Maintenance = "Maintenance";
        const string Informational = "Informational";
        const string Misc = "Miscellaneous";
        const string Undocumented = "Undocumented";

        return new List<DbccCommand>
        {
            // --- Validation ---
            C("CHECKALLOC", Validation, "( database [, NOINDEX | REPAIR] ) [WITH options]", "Checks the consistency of disk space allocation structures for a database."),
            C("CHECKCATALOG", Validation, "( database ) [WITH NO_INFOMSGS]", "Checks catalog consistency within the specified database."),
            C("CHECKCONSTRAINTS", Validation, "( table | constraint ) [WITH options]", "Checks the integrity of one or more constraints on a table."),
            C("CHECKDB", Validation, "( database [, REPAIR option] ) [WITH options]", "Checks the logical and physical integrity of all objects in a database."),
            C("CHECKFILEGROUP", Validation, "( [ filegroup [, NOINDEX] ] ) [WITH options]", "Checks the allocation and structural integrity of tables in a filegroup."),
            C("CHECKIDENT", Validation, "( table [, { NORESEED | RESEED [, new_value] }] )", "Checks and optionally corrects the current identity value of a table."),
            C("CHECKTABLE", Validation, "( table | view [, REPAIR option] ) [WITH options]", "Checks the integrity of all the pages and structures of a table or indexed view."),

            // --- Maintenance ---
            C("CLEANTABLE", Maintenance, "( database, 'table | view' [, batch_size] )", "Reclaims space after dropping variable-length or text columns."),
            C("DROPCLEANBUFFERS", Maintenance, "[WITH NO_INFOMSGS]", "Removes all clean buffers from the buffer pool (cold cache for testing)."),
            C("FREEPROCCACHE", Maintenance, "[ ( plan_handle | sql_handle | pool_name ) ] [WITH NO_INFOMSGS]", "Removes plan cache entries (all, or for a specific plan/handle/pool)."),
            C("FREESYSTEMCACHE", Maintenance, "( 'ALL' | 'pool_name' ) [WITH options]", "Releases unused cache entries from all or a specific resource pool's caches."),
            C("FREESESSIONCACHE", Maintenance, "[WITH NO_INFOMSGS]", "Flushes the distributed query connection cache for the session."),
            C("SHRINKDATABASE", Maintenance, "( database [, target_percent] [, { NOTRUNCATE | TRUNCATEONLY }] )", "Shrinks the size of the data and log files in the specified database."),
            C("SHRINKFILE", Maintenance, "( { file_name | file_id } [, target_size] [, option] )", "Shrinks the size of a specified data or log file for the current database."),
            C("UPDATEUSAGE", Maintenance, "( database [, table [, index] ] ) [WITH options]", "Reports and corrects inaccurate page and row-count catalog data."),
            C("DBREINDEX", Maintenance, "( table [, index [, fillfactor] ] ) [WITH NO_INFOMSGS]", "Rebuilds one or more indexes for a table (deprecated — use ALTER INDEX REBUILD)."),
            C("SHOWCONTIG", Maintenance, "( [ table [, index] ] ) [WITH options]", "Displays fragmentation information (deprecated — use sys.dm_db_index_physical_stats)."),

            // --- Informational ---
            C("INPUTBUFFER", Informational, "( session_id [, request_id] )", "Displays the last statement sent from a client to the instance."),
            C("OUTPUTBUFFER", Informational, "( session_id [, request_id] )", "Displays the current output buffer in hexadecimal and ASCII for a session."),
            C("OPENTRAN", Informational, "[ ( database ) ] [WITH options]", "Displays information about the oldest active transaction in a database."),
            C("SHOW_STATISTICS", Informational, "( 'table | view' [, target] ) [WITH options]", "Displays current query optimization statistics for a target on a table/view."),
            C("SQLPERF", Informational, "( LOGSPACE | 'sys.dm_os_latch_stats', CLEAR )", "Provides transaction-log space statistics (or resets latch statistics)."),
            C("TRACESTATUS", Informational, "[ ( trace# [, ...n] ) ] [WITH NO_INFOMSGS]", "Displays the status of trace flags (which are enabled, and how)."),
            C("USEROPTIONS", Informational, "", "Returns the SET options active for the current connection."),
            C("PROCCACHE", Informational, "", "Displays information about the procedure (plan) cache."),

            // --- Miscellaneous ---
            C("CLONEDATABASE", Misc, "( source_database, target_database ) [WITH options]", "Creates a schema-only, read-only clone of a database for diagnostics."),
            C("FLUSHAUTHCACHE", Misc, "[WITH NO_INFOMSGS]", "Empties the database authentication cache for logins and firewall rules."),
            C("HELP", Misc, "( 'command' | '?' )", "Returns syntax information for the specified DBCC command."),
            C("TRACEON", Misc, "( trace# [, ...n] [, -1] ) [WITH NO_INFOMSGS]", "Enables the specified trace flags (session or, with -1, global)."),
            C("TRACEOFF", Misc, "( trace# [, ...n] [, -1] ) [WITH NO_INFOMSGS]", "Disables the specified trace flags."),

            // --- Undocumented but commonly used in diagnostics ---
            C("PAGE", Undocumented, "( { database | dbid }, file#, page#, print_option )", "Dumps the contents of a data/index page (undocumented diagnostic)."),
            C("IND", Undocumented, "( { database | dbid }, table, index_id )", "Lists the pages belonging to a table or index (undocumented diagnostic)."),
            C("LOG", Undocumented, "( database [, output_level] )", "Displays the active transaction log records (undocumented diagnostic)."),
            C("LOGINFO", Undocumented, "[ ( database ) ]", "Displays information about the virtual log files (VLFs) of the log."),
            C("WRITEPAGE", Undocumented, "( { database | dbid }, file#, page#, offset, length, data )", "Writes directly to a page — dangerous, diagnostic/repair use only."),
        };
    }
}
