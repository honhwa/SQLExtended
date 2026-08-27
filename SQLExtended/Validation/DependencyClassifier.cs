using System;
using System.Collections.Generic;
using SQLExtended.Validation.Models;

namespace SQLExtended.Validation;

/// <summary>
/// Pure classification of a single <see cref="RawDependency"/> into a <see cref="ValidationIssue"/>.
/// Has no database or Visual Studio dependencies so it can be unit tested directly: the caller
/// supplies the resolved lookup sets (registered linked servers, existing databases, and a
/// per-database object lookup).
/// </summary>
internal static class DependencyClassifier
{
    /// <summary>
    /// XML / CLR data-type methods. SQL Server records calls like <c>xmlCol.value('…')</c> in
    /// sys.sql_expression_dependencies as bogus multi-part names — e.g. "alias.column.value" —
    /// where the "database" is actually a table alias. These are never real object references.
    /// </summary>
    private static readonly HashSet<string> TypeMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "value", "exist", "query", "nodes", "modify"
    };

    /// <summary>
    /// Virtual tables that exist only inside a trigger body. They are referenced unqualified and never
    /// bind to a real object, so they surface here as broken references — but are always valid.
    /// </summary>
    private static readonly HashSet<string> TriggerPseudoTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "inserted", "deleted"
    };

    /// <summary>
    /// Schemas that only ever hold system metadata (catalog views, DMVs). References into them are
    /// never broken user references — many live outside sys.objects (so can't be resolved that way)
    /// or are edition-specific (e.g. sys.dm_db_resource_stats is Azure-only and referenced
    /// conditionally by diagnostic procedures). Always skipped.
    /// </summary>
    private static bool IsSystemSchema(string schema) =>
        string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Decides whether a local reference resolves against one database's "schema.name" object set.
    /// Schema-qualified references must match "schema.entity" exactly. Unqualified references resolve
    /// if the bare name exists under <em>any</em> schema, since the runtime binds them against the
    /// module owner's default schema (not necessarily dbo).
    /// </summary>
    private static bool ResolvesIn(ISet<string> objects, string schema, string entity, bool schemaGiven)
    {
        if (objects == null || string.IsNullOrEmpty(entity))
            return false;

        if (schemaGiven)
            return objects.Contains(schema + "." + entity);

        foreach (string key in objects)
        {
            int dot = key.IndexOf('.');
            if (dot >= 0 && string.Equals(key.Substring(dot + 1), entity, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Classifies one dependency. Returns the finding to report, or <c>null</c> when the reference
    /// resolves cleanly and is not worth listing (e.g. a normal local reference).
    /// </summary>
    /// <param name="dep">The raw dependency row.</param>
    /// <param name="currentDatabase">The database that holds the referencing module.</param>
    /// <param name="linkedServers">Case-insensitive set of linked server names registered in sys.servers.</param>
    /// <param name="existingDatabases">Case-insensitive set of databases that exist on the server.</param>
    /// <param name="objectsInDatabase">
    /// Returns the case-insensitive set of "schema.name" object identifiers for a database, or
    /// <c>null</c> when that database could not be enumerated (offline / no permission).
    /// </param>
    /// <param name="infrastructureDatabases">
    /// Databases (e.g. <c>master</c>) whose objects are treated as always-available infrastructure:
    /// cross-database references to them are never reported, and local references that don't resolve
    /// in the current database but DO exist in one of these are suppressed. This is what stops
    /// utility objects installed in master (Ola Hallengren's CommandExecute/CommandLog, sp_WhoIsActive,
    /// etc.) from being flagged as missing in every other database.
    /// </param>
    /// <param name="referencingModuleLocalNames">
    /// Lazily returns the set of names defined locally in the referencing module (CTEs, derived-table
    /// and table aliases, table variables, temp tables). Only consulted for unqualified references, so
    /// the (relatively expensive) definition fetch + parse happens only when it could change the verdict.
    /// </param>
    public static ValidationIssue Classify(
        RawDependency dep,
        string currentDatabase,
        ISet<string> linkedServers,
        ISet<string> existingDatabases,
        Func<string, ISet<string>> objectsInDatabase,
        ISet<string> infrastructureDatabases = null,
        Func<ISet<string>> referencingModuleLocalNames = null)
    {
        // Filter out XML / CLR type-method artifacts (e.g. "alias.column.value" from xmlCol.value('…')).
        // These only ever appear with an outer (database/server) part that is actually a table alias.
        bool hasOuterPart = !string.IsNullOrEmpty(dep.ReferencedServer) || !string.IsNullOrEmpty(dep.ReferencedDatabase);
        if (hasOuterPart && !string.IsNullOrEmpty(dep.ReferencedEntity) && TypeMethodNames.Contains(dep.ReferencedEntity))
            return null;

        // Skip system metadata references (sys.*, INFORMATION_SCHEMA.*) — never broken user objects.
        if (IsSystemSchema(dep.ReferencedSchema))
            return null;

        var issue = new ValidationIssue
        {
            DatabaseName = currentDatabase,
            ReferencingType = dep.ReferencingType,
            ReferencingSchema = dep.ReferencingSchema,
            ReferencingName = dep.ReferencingName,
            ReferencedServer = dep.ReferencedServer,
            ReferencedDatabase = dep.ReferencedDatabase,
            ReferencedSchema = dep.ReferencedSchema,
            ReferencedEntity = dep.ReferencedEntity
        };

        // --- Linked server (4-part) reference ---
        if (!string.IsNullOrEmpty(dep.ReferencedServer))
        {
            issue.Kind = ReferenceKind.LinkedServer;
            if (linkedServers != null && linkedServers.Contains(dep.ReferencedServer))
            {
                issue.Severity = IssueSeverity.Info;
                issue.Issue = $"Linked server '{dep.ReferencedServer}' is registered — remote object not verified.";
            }
            else
            {
                issue.Severity = IssueSeverity.Error;
                issue.Issue = $"Linked server '{dep.ReferencedServer}' is not registered on this server.";
            }
            return issue;
        }

        // --- Cross-database (3-part) reference, to a database other than the current one ---
        bool isCrossDb = !string.IsNullOrEmpty(dep.ReferencedDatabase)
            && !string.Equals(dep.ReferencedDatabase, currentDatabase, StringComparison.OrdinalIgnoreCase);

        if (isCrossDb)
        {
            // Suppress references to infrastructure databases (e.g. master) entirely.
            if (infrastructureDatabases != null && infrastructureDatabases.Contains(dep.ReferencedDatabase))
                return null;

            issue.Kind = ReferenceKind.CrossDatabase;

            if (existingDatabases == null || !existingDatabases.Contains(dep.ReferencedDatabase))
            {
                issue.Severity = IssueSeverity.Error;
                issue.Issue = $"Database '{dep.ReferencedDatabase}' does not exist on this server.";
                return issue;
            }

            var objects = objectsInDatabase?.Invoke(dep.ReferencedDatabase);
            if (objects == null)
            {
                // Database exists but could not be enumerated (offline / no permission).
                issue.Severity = IssueSeverity.Info;
                issue.Issue = $"Cross-database reference to '{dep.ReferencedDatabase}' — contents could not be verified.";
                return issue;
            }

            string schema = string.IsNullOrEmpty(dep.ReferencedSchema) ? "dbo" : dep.ReferencedSchema;
            string key = schema + "." + dep.ReferencedEntity;
            if (objects.Contains(key))
            {
                issue.Severity = IssueSeverity.Info;
                issue.Issue = $"Cross-database reference resolves in '{dep.ReferencedDatabase}'.";
            }
            else
            {
                issue.Severity = IssueSeverity.Error;
                issue.Issue = $"Object '{schema}.{dep.ReferencedEntity}' not found in database '{dep.ReferencedDatabase}'.";
            }
            return issue;
        }

        // --- Local reference ---
        // referenced_id is NULL when SQL Server could not bind the name. Caller-dependent
        // (temp tables, dynamic SQL) and ambiguous references legitimately don't bind, so skip them.
        if (dep.ReferencedIdIsNull && !dep.IsCallerDependent && !dep.IsAmbiguous)
        {
            bool schemaGiven = !string.IsNullOrEmpty(dep.ReferencedSchema);
            string schema = schemaGiven ? dep.ReferencedSchema : "dbo";

            // Cross-check the actual catalog rather than trusting referenced_id: SQL Server leaves
            // it NULL for many references that DO exist (deferred resolution, recompiled callers,
            // inter-proc calls). Only a name genuinely absent from the catalog is broken.
            //
            // When the name is schema-qualified we resolve "schema.name" exactly. When it is
            // unqualified the runtime binds it against the module owner's default schema — which is
            // not necessarily dbo — so we treat the name as resolved if it exists under ANY schema.
            // CTEs, derived-table aliases and table variables are not schema-scoped and are never
            // recorded here, so an unqualified name absent from every schema is a genuine break.
            var localObjects = objectsInDatabase?.Invoke(currentDatabase);
            if (ResolvesIn(localObjects, schema, dep.ReferencedEntity, schemaGiven))
                return null;

            // Also resolve against infrastructure databases (master): utility objects installed there
            // (Ola Hallengren, sp_WhoIsActive, etc.) are treated as available everywhere.
            if (infrastructureDatabases != null)
            {
                foreach (string infraDb in infrastructureDatabases)
                {
                    var infraObjects = objectsInDatabase?.Invoke(infraDb);
                    if (ResolvesIn(infraObjects, schema, dep.ReferencedEntity, schemaGiven))
                        return null;
                }
            }

            // Unqualified names that aren't in any catalog are usually not broken objects but
            // constructs scoped to the module itself: the inserted/deleted trigger pseudo-tables,
            // or CTEs / derived-table & table aliases / table variables. Suppress those; only a name
            // that is genuinely none of them (e.g. FROM badtable) is a real broken reference.
            if (!schemaGiven)
            {
                if (string.Equals(dep.ReferencingType?.Trim(), "TR", StringComparison.OrdinalIgnoreCase) &&
                    TriggerPseudoTables.Contains(dep.ReferencedEntity))
                    return null;

                var localNames = referencingModuleLocalNames?.Invoke();
                if (localNames != null && localNames.Contains(dep.ReferencedEntity))
                    return null;
            }

            issue.Kind = ReferenceKind.Local;
            issue.Severity = IssueSeverity.Error;
            issue.Issue = schemaGiven
                ? $"Unresolved reference — '{schema}.{dep.ReferencedEntity}' does not exist in this database."
                : $"Unresolved reference — '{dep.ReferencedEntity}' does not exist in this database.";
            return issue;
        }

        // Local reference that resolves — not worth listing.
        return null;
    }
}
