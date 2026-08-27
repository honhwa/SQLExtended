/*
    schema-cache-access-probe.sql
    ------------------------------------------------------------------------------------------------
    Runs the exact reads SQLExtended's IntelliSense schema cache performs, one stage at a time, and
    prints a verdict for each. Reach for this before debugging the C# -- it separates "this login
    cannot see the catalog" from "the plumbing is wrong", the same split decrypt-module-probe.sql
    exists for.

    Written for Azure SQL Database, where every read has to happen inside the target database
    (no USE, no cross-database queries) and where reaching master is a separate matter entirely.
    It runs unchanged on a box instance.

    Source of truth for the statements: SQLExtended/Cache/SchemaCacheLoader.cs,
    SQLExtended/Cache/SystemCatalogSql.cs, and the database-list read in
    SQLExtended/IntelliSense/SqlCompletionSource.cs (GetDatabaseNames).

    Stages 1-8 must be run WITH THE QUERY WINDOW CONNECTED TO THE USER DATABASE you expect
    completion to work in. Stage 0b is the only one that wants master.
    ------------------------------------------------------------------------------------------------
*/

SET NOCOUNT ON;

DECLARE @rows int;

------------------------------------------------------------------------------------------------
-- Stage 0a: who and where. Engine edition 5 = Azure SQL Database, 8 = Managed Instance,
--           3 = box Enterprise. Anything but 5 and the "no cross-database" rules below relax.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 0a: context ===';
SELECT  DB_NAME()                                        AS current_database,
        SUSER_SNAME()                                    AS login_name,
        USER_NAME()                                      AS database_user,
        CONVERT(int, SERVERPROPERTY('EngineEdition'))    AS engine_edition,
        SERVERPROPERTY('ServerName')                     AS server_name,
        CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS product_version,
        IS_ROLEMEMBER('db_datareader')                   AS is_db_datareader,
        IS_MEMBER('db_owner')                            AS is_db_owner,
        HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW DEFINITION') AS can_view_definition_db_wide;

/*
    VIEW DEFINITION is the permission that decides whether the cache is useful. Without it the
    sys.* catalog views are metadata-visibility filtered: every query below succeeds, returns
    zero rows, and completion offers nothing -- which on screen is indistinguishable from the
    cache not having loaded. db_datareader alone does NOT grant it.

        GRANT VIEW DEFINITION TO [<user>];        -- database-wide

    On Azure SQL Database the server-level ##MS_DefinitionReader## role does not apply; use the
    database grant, or add the user to db_owner.
*/

------------------------------------------------------------------------------------------------
-- Stage 0b: the database list. RUN THIS AGAINST master (the extension opens its own connection
--           with InitialCatalog=master and ConnectTimeout=5 to do it).
--           On Azure SQL Database this only works for a login that exists in master; a
--           contained database user cannot connect there at all, and the extension then falls
--           back to whatever databases it already has cached.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 0b: database list (run against master) ===';
BEGIN TRY
    SELECT name FROM sys.databases
    WHERE state_desc = 'ONLINE'
      AND HAS_DBACCESS(name) = 1
    ORDER BY name;

    PRINT '  OK: database list readable from this connection.';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
    PRINT '  -> If this is a contained user on Azure SQL Database, this is expected. Completion';
    PRINT '     will only offer databases the cache already holds.';
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 1: objects. The spine of the cache -- tables, views, procs, functions, synonyms and
--          table types. Everything else joins to these.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 1: objects ===';
BEGIN TRY
    SELECT s.name AS schema_name, o.name, o.type, o.create_date, o.modify_date,
           p.rows AS row_count
    FROM sys.objects o
    JOIN sys.schemas s ON o.schema_id = s.schema_id
    LEFT JOIN (
        SELECT object_id, SUM(rows) rows
        FROM sys.partitions WHERE index_id IN (0,1)
        GROUP BY object_id
    ) p ON o.object_id = p.object_id
    WHERE o.type IN ('U','V','P','FN','IF','TF','SN','TT') AND o.is_ms_shipped = 0;

    SET @rows = @@ROWCOUNT;
    PRINT '  rows: ' + CONVERT(varchar(20), @rows);
    IF @rows = 0
        PRINT '  ZERO ROWS: either an empty database, or no VIEW DEFINITION (see stage 0a).';
    ELSE
        PRINT '  OK';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 2: columns. This is what completion offers after "alias." -- if stage 1 works and this
--          does not, object names complete and columns never do.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 2: columns ===';
BEGIN TRY
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
    ORDER BY s.name, o.name, c.column_id;

    SET @rows = @@ROWCOUNT;
    PRINT '  rows: ' + CONVERT(varchar(20), @rows);
    IF @rows = 0 PRINT '  ZERO ROWS: no columns will be offered by completion.' ELSE PRINT '  OK';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 3: indexes. Uses FOR XML PATH / .value(), which is supported on Azure SQL Database but
--          is the one construct here that a downlevel or hardened target could reject.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 3: indexes ===';
BEGIN TRY
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
    ORDER BY s.name, o.name, ix.name;

    PRINT '  rows: ' + CONVERT(varchar(20), @@ROWCOUNT) + '  OK';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 4: foreign keys. Also feeds the JOIN-predicate completion, so losing this loses that.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 4: foreign keys ===';
BEGIN TRY
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
    ORDER BY s.name, o.name, fk.name;

    PRINT '  rows: ' + CONVERT(varchar(20), @@ROWCOUNT) + '  OK';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 5: parameters. Signature help for procs and functions.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 5: parameters ===';
BEGIN TRY
    SELECT s.name AS schema_name, o.name AS object_name,
           p.name AS param_name, p.parameter_id,
           t.name AS type_name, p.max_length, p.is_output, p.has_default_value
    FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.schemas s ON o.schema_id = s.schema_id
    JOIN sys.types t ON p.user_type_id = t.user_type_id
    WHERE o.type IN ('P','FN','IF','TF') AND o.is_ms_shipped = 0
    ORDER BY s.name, o.name, p.parameter_id;

    PRINT '  rows: ' + CONVERT(varchar(20), @@ROWCOUNT) + '  OK';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 6: module definitions. Feeds the schema viewer and "search in definitions", NOT
--          completion. A NULL definition means the module is encrypted; a login without
--          VIEW DEFINITION gets NULL for everything and looks exactly the same.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 6: definitions ===';
BEGIN TRY
    SELECT s.name AS schema_name, o.name, m.definition
    FROM sys.sql_modules m
    JOIN sys.objects o ON m.object_id = o.object_id
    JOIN sys.schemas s ON o.schema_id = s.schema_id
    WHERE o.is_ms_shipped = 0;

    SET @rows = @@ROWCOUNT;

    SELECT COUNT(*)                                              AS module_count,
           SUM(CASE WHEN m.definition IS NULL THEN 1 ELSE 0 END) AS null_definitions
    FROM sys.sql_modules m
    JOIN sys.objects o ON m.object_id = o.object_id
    WHERE o.is_ms_shipped = 0;

    PRINT '  rows: ' + CONVERT(varchar(20), @rows);
    PRINT '  -> If null_definitions equals module_count and nothing here is encrypted, this login';
    PRINT '     is missing VIEW DEFINITION.';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 7: the system catalog surface behind "sys." completion (SystemCatalogSql).
--          Read once per server, from whichever database the first completion happened in --
--          on Azure SQL Database that means this database's surface answers for the whole
--          session.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 7: system catalog (sys. completion) ===';
BEGIN TRY
    SELECT s.name AS schema_name, o.name AS object_name, o.type
    FROM sys.all_objects o
    JOIN sys.schemas s ON o.schema_id = s.schema_id
    WHERE o.is_ms_shipped = 1
      AND s.name IN ('sys', 'INFORMATION_SCHEMA')
      AND o.type IN ('V', 'U', 'IF', 'TF', 'FN')
    ORDER BY s.name, o.name;

    PRINT '  objects: ' + CONVERT(varchar(20), @@ROWCOUNT);

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
    ORDER BY s.name, o.name, c.column_id;

    PRINT '  columns: ' + CONVERT(varchar(20), @@ROWCOUNT) + '  OK';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

------------------------------------------------------------------------------------------------
-- Stage 8: incremental refresh. The periodic refresh asks only for what changed.
------------------------------------------------------------------------------------------------
PRINT '=== Stage 8: incremental refresh ===';
BEGIN TRY
    DECLARE @since datetime = DATEADD(day, -1, GETDATE());

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
      AND o.modify_date > @since;

    PRINT '  rows: ' + CONVERT(varchar(20), @@ROWCOUNT) + '  OK';
END TRY
BEGIN CATCH
    PRINT '  FAILED: ' + ERROR_MESSAGE();
END CATCH;

PRINT '=== done ===';
