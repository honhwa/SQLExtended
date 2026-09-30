using System.Collections.Generic;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Completion data for hints: the query hints offered inside "OPTION (", the hint names offered inside
/// "USE HINT (", and the table hints offered inside a table reference's "WITH (". Azure Synapse / PDW-only hints (LABEL, FORCE EXTERNALPUSHDOWN, …) are left
/// out: they fail on every box and Azure SQL Database instance this runs against.
/// </summary>
internal static class SqlQueryHints
{
    /// <summary>Query hints that can appear in OPTION ( … ). Reuses <see cref="AlterClause"/> as a plain keyword + description.</summary>
    public static IReadOnlyList<AlterClause> Hints { get; } = new List<AlterClause>
    {
        new AlterClause("RECOMPILE", "Compile a fresh plan for this execution and discard it afterwards."),
        new AlterClause("MAXDOP", "MAXDOP n — cap the degree of parallelism for this query (0 = server setting)."),
        new AlterClause("OPTIMIZE FOR UNKNOWN", "Optimize using average density for every parameter instead of the sniffed values."),
        new AlterClause("OPTIMIZE FOR", "OPTIMIZE FOR (@p = value | UNKNOWN, …) — optimize for the given parameter values."),
        new AlterClause("USE HINT", "USE HINT ('name', …) — apply named optimizer behaviours without trace flags."),
        new AlterClause("MAXRECURSION", "MAXRECURSION n — maximum recursion depth of a recursive CTE (0 = no limit, default 100)."),
        new AlterClause("FAST", "FAST n — optimize for returning the first n rows quickly."),
        new AlterClause("FORCE ORDER", "Join tables in the order they are written in the query."),
        new AlterClause("LOOP JOIN", "Use nested loops for every join in the query."),
        new AlterClause("HASH JOIN", "Use hash joins for every join in the query."),
        new AlterClause("MERGE JOIN", "Use merge joins for every join in the query."),
        new AlterClause("HASH GROUP", "Perform GROUP BY / DISTINCT aggregations by hashing."),
        new AlterClause("ORDER GROUP", "Perform GROUP BY / DISTINCT aggregations by ordering (stream aggregate)."),
        new AlterClause("CONCAT UNION", "Perform UNION operations by concatenation."),
        new AlterClause("HASH UNION", "Perform UNION operations by hashing."),
        new AlterClause("MERGE UNION", "Perform UNION operations by merging."),
        new AlterClause("EXPAND VIEWS", "Expand indexed views into their definitions instead of matching them."),
        new AlterClause("KEEP PLAN", "Relax the recompile threshold for temporary tables."),
        new AlterClause("KEEPFIXED PLAN", "Never recompile because of statistics changes."),
        new AlterClause("ROBUST PLAN", "Choose a plan that works for the maximum potential row size, at some cost in performance."),
        new AlterClause("MAX_GRANT_PERCENT", "MAX_GRANT_PERCENT = n — cap the memory grant as a percentage of the configured limit."),
        new AlterClause("MIN_GRANT_PERCENT", "MIN_GRANT_PERCENT = n — guarantee a minimum memory grant as a percentage of the configured limit."),
        new AlterClause("NO_PERFORMANCE_SPOOL", "Prevent spool operators from being added to the plan."),
        new AlterClause("IGNORE_NONCLUSTERED_COLUMNSTORE_INDEX", "Do not use nonclustered columnstore indexes for this query."),
        new AlterClause("PARAMETERIZATION SIMPLE", "Use simple parameterization for this query (plan guides only)."),
        new AlterClause("PARAMETERIZATION FORCED", "Use forced parameterization for this query (plan guides only)."),
        new AlterClause("QUERYTRACEON", "QUERYTRACEON n — enable a plan-affecting trace flag for this query only."),
        new AlterClause("USE PLAN", "USE PLAN N'xml_plan' — force the query to use the given XML showplan."),
        new AlterClause("TABLE HINT", "TABLE HINT (object, hint, …) — apply a table hint to one table in the query."),
    };

    /// <summary>Table hints valid inside a table reference's WITH ( … ), and after the object in OPTION (TABLE HINT (object, …)).</summary>
    public static IReadOnlyList<AlterClause> TableHints { get; } = new List<AlterClause>
    {
        new AlterClause("NOLOCK", "Read without shared locks — dirty reads are possible (same as READUNCOMMITTED)."),
        new AlterClause("READUNCOMMITTED", "Read without shared locks — dirty reads are possible."),
        new AlterClause("READCOMMITTED", "Read committed isolation for this table (row versioning if READ_COMMITTED_SNAPSHOT is on)."),
        new AlterClause("READCOMMITTEDLOCK", "Read committed using locking, even when READ_COMMITTED_SNAPSHOT is on."),
        new AlterClause("REPEATABLEREAD", "Hold shared locks until the end of the transaction."),
        new AlterClause("SERIALIZABLE", "Hold range locks until the end of the transaction (same as HOLDLOCK)."),
        new AlterClause("HOLDLOCK", "Hold shared locks until the end of the transaction, with range locks (same as SERIALIZABLE)."),
        new AlterClause("SNAPSHOT", "Snapshot isolation for this table (memory-optimized tables)."),
        new AlterClause("UPDLOCK", "Take update locks instead of shared locks, held to the end of the transaction."),
        new AlterClause("XLOCK", "Take exclusive locks, held to the end of the transaction."),
        new AlterClause("ROWLOCK", "Take row locks where page or table locks would normally be taken."),
        new AlterClause("PAGLOCK", "Take page locks where row or table locks would normally be taken."),
        new AlterClause("TABLOCK", "Take a shared lock on the whole table (enables minimal logging for bulk inserts)."),
        new AlterClause("TABLOCKX", "Take an exclusive lock on the whole table."),
        new AlterClause("READPAST", "Skip rows locked by other transactions instead of waiting for them."),
        new AlterClause("NOWAIT", "Fail immediately with an error instead of waiting on a lock."),
        new AlterClause("INDEX", "INDEX (name | id, …) — force the optimizer to use the given index(es)."),
        new AlterClause("FORCESEEK", "Use only an index seek to access the table (optionally FORCESEEK (index (columns)))."),
        new AlterClause("FORCESCAN", "Use only a scan to access the table."),
        new AlterClause("NOEXPAND", "Use the indexed view's index instead of expanding the view."),
        new AlterClause("SPATIAL_WINDOW_MAX_CELLS", "SPATIAL_WINDOW_MAX_CELLS = n — the maximum cells to tessellate for a spatial index."),
        new AlterClause("KEEPIDENTITY", "INSERT ... SELECT FROM OPENROWSET(BULK): keep the file's identity values."),
        new AlterClause("KEEPDEFAULTS", "INSERT ... SELECT FROM OPENROWSET(BULK): insert defaults for empty columns instead of NULL."),
        new AlterClause("IGNORE_CONSTRAINTS", "INSERT ... SELECT FROM OPENROWSET(BULK): do not check CHECK and FOREIGN KEY constraints."),
        new AlterClause("IGNORE_TRIGGERS", "INSERT ... SELECT FROM OPENROWSET(BULK): do not fire insert triggers."),
    };

    /// <summary>Names valid inside USE HINT ( '…' ). Inserted as quoted string literals.</summary>
    public static IReadOnlyList<AlterClause> UseHintNames { get; } = new List<AlterClause>
    {
        new AlterClause("ENABLE_QUERY_OPTIMIZER_HOTFIXES", "Enable query optimizer hotfixes (trace flag 4199)."),
        new AlterClause("DISABLE_PARAMETER_SNIFFING", "Optimize with average data distribution, ignoring sniffed values (trace flag 4136)."),
        new AlterClause("DISABLE_OPTIMIZER_ROWGOAL", "Disable row goal adjustments for TOP, OPTION (FAST n), IN and EXISTS (trace flag 4138)."),
        new AlterClause("FORCE_LEGACY_CARDINALITY_ESTIMATION", "Use the SQL Server 2012 and earlier cardinality estimator (trace flag 9481)."),
        new AlterClause("FORCE_DEFAULT_CARDINALITY_ESTIMATION", "Use the cardinality estimator matching the database compatibility level (trace flag 2312)."),
        new AlterClause("ASSUME_MIN_SELECTIVITY_FOR_FILTER_ESTIMATES", "Assume minimum selectivity (full correlation) when estimating AND predicates (trace flag 4137 / 9471)."),
        new AlterClause("ASSUME_FULL_INDEPENDENCE_FOR_FILTER_ESTIMATES", "Assume full independence when estimating AND predicates."),
        new AlterClause("ASSUME_PARTIAL_CORRELATION_FOR_FILTER_ESTIMATES", "Assume partial correlation (exponential backoff) when estimating AND predicates."),
        new AlterClause("ASSUME_JOIN_PREDICATE_DEPENDS_ON_FILTERS", "Use simple containment instead of base containment for joins (trace flag 9476)."),
        new AlterClause("ENABLE_HIST_AMENDMENT_FOR_ASC_KEYS", "Enable automatic histogram amendment for ascending keys (trace flag 4139)."),
        new AlterClause("DISABLE_OPTIMIZED_NESTED_LOOP", "Do not sort rows before a nested loops join (trace flag 2340)."),
        new AlterClause("DISABLE_BATCH_MODE_ADAPTIVE_JOINS", "Disable batch mode adaptive joins."),
        new AlterClause("DISABLE_BATCH_MODE_MEMORY_GRANT_FEEDBACK", "Disable batch mode memory grant feedback."),
        new AlterClause("DISABLE_ROW_MODE_MEMORY_GRANT_FEEDBACK", "Disable row mode memory grant feedback."),
        new AlterClause("DISABLE_INTERLEAVED_EXECUTION_TVF", "Disable interleaved execution for multi-statement table-valued functions."),
        new AlterClause("DISABLE_DEFERRED_COMPILATION_TV", "Disable table variable deferred compilation."),
        new AlterClause("DISABLE_TSQL_SCALAR_UDF_INLINING", "Disable scalar UDF inlining."),
        new AlterClause("DISALLOW_BATCH_MODE", "Disable batch mode execution for this query."),
        new AlterClause("DISABLE_PARAMETER_SENSITIVE_PLAN_OPTIMIZATION", "Disable parameter sensitive plan optimization (SQL Server 2022+)."),
        new AlterClause("QUERY_PLAN_PROFILE", "Enable lightweight profiling for this query (for the query_plan_profile extended event)."),
        new AlterClause("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_100", "Optimize as under compatibility level 100 (SQL Server 2008)."),
        new AlterClause("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_110", "Optimize as under compatibility level 110 (SQL Server 2012)."),
        new AlterClause("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_120", "Optimize as under compatibility level 120 (SQL Server 2014)."),
        new AlterClause("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_130", "Optimize as under compatibility level 130 (SQL Server 2016)."),
        new AlterClause("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_140", "Optimize as under compatibility level 140 (SQL Server 2017)."),
        new AlterClause("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150", "Optimize as under compatibility level 150 (SQL Server 2019)."),
        new AlterClause("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_160", "Optimize as under compatibility level 160 (SQL Server 2022)."),
    };
}
