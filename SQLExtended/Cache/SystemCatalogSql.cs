namespace SQLExtended.Cache;

/// <summary>
/// The catalog read behind <see cref="SystemCatalogCache"/>, kept in its own file — free of
/// SqlClient — so the test project can link it and parse it with ScriptDom
/// (<c>SQLExtended.Tests/Cache/SystemCatalogSqlTests.cs</c>), the same split
/// <c>ExportFileNaming</c> and <c>MonitorCollection</c> are here for.
///
/// It is worth testing because its failure is silent: <see cref="SystemCatalogCache"/> swallows the
/// exception and memoises the server as failed, so a syntax error here is indistinguishable on
/// screen from a login that cannot read the catalog — in both cases "sys." simply offers nothing.
/// </summary>
internal static class SystemCatalogSql
{
    /// <summary>
    /// Two result sets: the objects, then their columns.
    ///
    /// <para><c>sys.all_objects</c> rather than <c>sys.objects</c>, and <c>sys.all_columns</c> rather
    /// than <c>sys.columns</c>: the "all_" views are the ones that include system objects at all. The
    /// user-object loader in <c>SchemaCacheLoader</c> is the exact complement of this — it filters
    /// <c>is_ms_shipped = 0</c>, this takes <c>= 1</c> — so nothing is loaded twice.</para>
    ///
    /// <para>Types are restricted deliberately. V covers the catalog views and the great majority of
    /// DMVs; IF/TF cover the table-valued DMVs (<c>sys.dm_exec_sql_text</c> and friends); FN covers
    /// the scalar system functions; U covers the few readable system base tables. Types S (internal
    /// base tables, reachable only over the DAC), P and X (system stored and extended procedures) are
    /// left out — S is not usefully queryable, and P/X belong to the EXEC completion path, which does
    /// not read this cache.</para>
    ///
    /// <para>The column query drops FN: a scalar function has no columns, and including it would cost
    /// a join for no rows.</para>
    /// </summary>
    public const string ObjectsAndColumns = @"
        SELECT s.name AS schema_name, o.name AS object_name, o.type
        FROM sys.all_objects o
        JOIN sys.schemas s ON o.schema_id = s.schema_id
        WHERE o.is_ms_shipped = 1
          AND s.name IN ('sys', 'INFORMATION_SCHEMA')
          AND o.type IN ('V', 'U', 'IF', 'TF', 'FN')
        ORDER BY s.name, o.name;

        SELECT s.name AS schema_name, o.name AS table_name, c.name AS column_name,
               c.column_id, t.name AS type_name,
               c.max_length, c.precision, c.scale, c.is_nullable
        FROM sys.all_columns c
        JOIN sys.all_objects o ON c.object_id = o.object_id
        JOIN sys.schemas s ON o.schema_id = s.schema_id
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE o.is_ms_shipped = 1
          AND s.name IN ('sys', 'INFORMATION_SCHEMA')
          AND o.type IN ('V', 'U', 'IF', 'TF')
        ORDER BY s.name, o.name, c.column_id;";
}
