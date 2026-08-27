using System.Collections.Generic;

namespace SQLExtended.IntelliSense;

/// <summary>
/// A single ALTER clause for completion — either an alterable object kind (the word(s)
/// that follow ALTER) or a sub-action of ALTER TABLE — with a one-line description.
/// </summary>
internal sealed class AlterClause
{
    public AlterClause(string keyword, string description)
    {
        Keyword = keyword;
        Description = description;
    }

    public string Keyword { get; }
    public string Description { get; }
}

/// <summary>
/// Completion data for the ALTER statement: the object kinds offered after "ALTER ",
/// and the sub-actions offered after "ALTER TABLE [schema.]name ".
/// </summary>
internal static class SqlAlterCommands
{
    /// <summary>Object kinds that can follow ALTER (ALTER TABLE, ALTER PROCEDURE, …).</summary>
    public static IReadOnlyList<AlterClause> Targets { get; } = new List<AlterClause>
    {
        new AlterClause("TABLE", "Modify a table — add/alter/drop columns, constraints, or storage options."),
        new AlterClause("VIEW", "Modify the definition of an existing view."),
        new AlterClause("PROCEDURE", "Modify the definition of an existing stored procedure."),
        new AlterClause("PROC", "Modify the definition of an existing stored procedure (short form)."),
        new AlterClause("FUNCTION", "Modify the definition of an existing user-defined function."),
        new AlterClause("TRIGGER", "Modify the definition of an existing trigger."),
        new AlterClause("INDEX", "Rebuild, reorganize, disable, or set options on an index."),
        new AlterClause("DATABASE", "Change database options, files, filegroups, collation, or compatibility level."),
        new AlterClause("DATABASE SCOPED CONFIGURATION", "Configure database-scoped settings such as MAXDOP or legacy cardinality estimation."),
        new AlterClause("SCHEMA", "Transfer a securable object into the specified schema."),
        new AlterClause("SEQUENCE", "Change the properties of an existing sequence object."),
        new AlterClause("LOGIN", "Modify a SQL Server login — password, default database, enable/disable."),
        new AlterClause("USER", "Modify a database user — default schema, login mapping, or name."),
        new AlterClause("ROLE", "Add or drop members of a database role, or rename it."),
        new AlterClause("SERVER ROLE", "Add or drop members of a server role, or rename it."),
        new AlterClause("APPLICATION ROLE", "Change the name, password, or default schema of an application role."),
        new AlterClause("AUTHORIZATION", "Change the ownership of a securable (ALTER AUTHORIZATION ON …)."),
        new AlterClause("FULLTEXT INDEX", "Modify a full-text index on a table or indexed view."),
        new AlterClause("FULLTEXT CATALOG", "Rebuild, reorganize, or set options on a full-text catalog."),
        new AlterClause("PARTITION FUNCTION", "Split or merge the boundary values of a partition function."),
        new AlterClause("PARTITION SCHEME", "Add a filegroup to (or mark next-used on) a partition scheme."),
        new AlterClause("ASSEMBLY", "Modify a CLR assembly — add files, change permissions, or visibility."),
        new AlterClause("QUEUE", "Change the properties of a Service Broker queue."),
        new AlterClause("SERVICE", "Change the contracts or queue of a Service Broker service."),
        new AlterClause("RESOURCE GOVERNOR", "Reconfigure, disable, or reset statistics for the Resource Governor."),
        new AlterClause("AVAILABILITY GROUP", "Modify an Always On availability group."),
        new AlterClause("SERVER CONFIGURATION", "Change instance-level settings such as process affinity or soft-NUMA."),
    };

    /// <summary>Sub-actions that can follow "ALTER TABLE [schema.]name ".</summary>
    public static IReadOnlyList<AlterClause> TableActions { get; } = new List<AlterClause>
    {
        new AlterClause("ADD", "Add a new column or table-level constraint."),
        new AlterClause("ALTER COLUMN", "Change the data type, nullability, or collation of an existing column."),
        new AlterClause("DROP COLUMN", "Remove one or more columns from the table."),
        new AlterClause("DROP CONSTRAINT", "Remove a named constraint from the table."),
        new AlterClause("ADD CONSTRAINT", "Add a named PRIMARY KEY, FOREIGN KEY, UNIQUE, CHECK, or DEFAULT constraint."),
        new AlterClause("WITH CHECK ADD", "Add a constraint and validate it against existing data."),
        new AlterClause("WITH NOCHECK ADD", "Add a constraint without validating existing data."),
        new AlterClause("CHECK CONSTRAINT", "Re-enable constraint checking (CHECK CONSTRAINT ALL or a named one)."),
        new AlterClause("NOCHECK CONSTRAINT", "Disable constraint checking (NOCHECK CONSTRAINT ALL or a named one)."),
        new AlterClause("ENABLE TRIGGER", "Enable one or all triggers on the table."),
        new AlterClause("DISABLE TRIGGER", "Disable one or all triggers on the table."),
        new AlterClause("REBUILD", "Rebuild the table or its partitions (e.g. to apply data compression)."),
        new AlterClause("SWITCH PARTITION", "Switch a partition or the whole table to/from another table."),
        new AlterClause("SET", "Set table options such as LOCK_ESCALATION, FILESTREAM_ON, or SYSTEM_VERSIONING."),
    };

    /// <summary>Sub-actions that can follow "ALTER INDEX {name | ALL} ON object ".</summary>
    public static IReadOnlyList<AlterClause> IndexActions { get; } = new List<AlterClause>
    {
        new AlterClause("REBUILD", "Rebuild the index from scratch, removing fragmentation and reclaiming space."),
        new AlterClause("REORGANIZE", "Defragment the leaf level of the index online, using minimal resources."),
        new AlterClause("DISABLE", "Disable the index, making it unavailable until it is rebuilt."),
        new AlterClause("SET", "Change index options such as ALLOW_ROW_LOCKS, IGNORE_DUP_KEY, or STATISTICS_NORECOMPUTE."),
        new AlterClause("RESUME", "Resume a paused resumable online index rebuild."),
        new AlterClause("PAUSE", "Pause an in-progress resumable online index rebuild."),
        new AlterClause("ABORT", "Abort an in-progress or paused resumable online index rebuild."),
    };

    /// <summary>Keyword(s) valid in the index-name slot of ALTER INDEX (i.e. ALL).</summary>
    public static IReadOnlyList<AlterClause> IndexNameHints { get; } = new List<AlterClause>
    {
        new AlterClause("ALL", "Apply the operation to all indexes defined on the table or view."),
    };
}
