using System;
using System.Collections.Generic;
using SQLExtended.Validation;
using SQLExtended.Validation.Models;
using Xunit;

namespace SQLExtended.Tests;

public class DependencyClassifierTests
{
    private const string CurrentDb = "AppDb";

    private static ISet<string> Set(params string[] items)
        => new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);

    private static ValidationIssue Classify(
        RawDependency dep,
        ISet<string> linkedServers = null,
        ISet<string> databases = null,
        Func<string, ISet<string>> objectsInDb = null,
        ISet<string> infrastructure = null,
        Func<ISet<string>> localNames = null)
        => DependencyClassifier.Classify(
            dep,
            CurrentDb,
            linkedServers ?? Set(),
            databases ?? Set("AppDb", "master"),
            objectsInDb ?? (_ => Set()),
            infrastructure ?? Set(),
            localNames);

    [Fact]
    public void ResolvedLocalReference_IsNotReported()
    {
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vSales",
            ReferencedSchema = "dbo", ReferencedEntity = "Orders",
            ReferencedIdIsNull = false
        };

        Assert.Null(Classify(dep));
    }

    [Fact]
    public void UnresolvedLocalReference_IsError()
    {
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "GetSales",
            ReferencedSchema = "dbo", ReferencedEntity = "GhostTable",
            ReferencedIdIsNull = true
        };

        var issue = Classify(dep);

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ReferenceKind.Local, issue.Kind);
    }

    [Fact]
    public void UnqualifiedUnresolvedLocalReference_MissingEverywhere_IsError()
    {
        // SELECT ... FROM GhostTable (no schema prefix) inside a saved module. SQL Server records
        // it with referenced_schema_name = NULL and referenced_id = NULL. It exists under no schema,
        // so it's a genuine broken reference and must be reported.
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vReport",
            ReferencedEntity = "GhostTable",
            ReferencedIdIsNull = true
        };

        var issue = Classify(dep, objectsInDb: _ => Set("dbo.SomethingElse"));

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ReferenceKind.Local, issue.Kind);
    }

    [Fact]
    public void UnqualifiedUnresolvedLocalReference_MatchingModuleLocalName_IsNotReported()
    {
        // FROM Orders o … o.Id — 'o' is a table alias defined in the module, recorded here as an
        // unbound reference. The module-local-name lookup must suppress it.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "GetSales",
            ReferencedEntity = "o",
            ReferencedIdIsNull = true
        };

        Assert.Null(Classify(dep, localNames: () => Set("o", "cteTotals")));
    }

    [Fact]
    public void UnqualifiedUnresolvedLocalReference_NotAModuleLocalName_IsError()
    {
        // 'badtable' is not an alias/CTE/variable in the module, so even with a local-name lookup
        // present it remains a genuine broken reference.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "GetSales",
            ReferencedEntity = "badtable",
            ReferencedIdIsNull = true
        };

        var issue = Classify(dep, localNames: () => Set("o", "cteTotals"));

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ReferenceKind.Local, issue.Kind);
    }

    [Fact]
    public void TriggerPseudoTables_AreNotReported()
    {
        foreach (var pseudo in new[] { "inserted", "deleted", "INSERTED", "Deleted" })
        {
            var dep = new RawDependency
            {
                ReferencingType = "TR", ReferencingSchema = "dbo", ReferencingName = "trg_Audit",
                ReferencedEntity = pseudo,
                ReferencedIdIsNull = true
            };

            Assert.Null(Classify(dep));
        }
    }

    [Fact]
    public void InsertedDeleted_OutsideTrigger_IsStillEvaluated()
    {
        // 'inserted' is only a pseudo-table inside a trigger. In a proc it's a normal name: if it's
        // a module-local alias it's suppressed, otherwise it's a genuine missing reference.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "DoThing",
            ReferencedEntity = "inserted",
            ReferencedIdIsNull = true
        };

        var issue = Classify(dep, localNames: () => Set());

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
    }

    [Fact]
    public void UnqualifiedUnresolvedLocalReference_ResolvingUnderNonDboSchema_IsNotReported()
    {
        // A module owned by a user whose default schema is 'sales' references `FROM Orders`
        // unqualified; the runtime binds it to sales.Orders. It exists under some schema → not broken.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "sales", ReferencingName = "GetOrders",
            ReferencedEntity = "Orders",
            ReferencedIdIsNull = true
        };

        Assert.Null(Classify(dep, objectsInDb: db => db == CurrentDb ? Set("sales.Orders") : Set()));
    }

    [Fact]
    public void SystemSchemaReference_IsNotReported()
    {
        // sys.* DMVs (e.g. the Azure-only sys.dm_db_resource_stats) are referenced conditionally and
        // live outside sys.objects — never a broken user reference.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "sp_BlitzFirst",
            ReferencedSchema = "sys", ReferencedEntity = "dm_db_resource_stats",
            ReferencedIdIsNull = true
        };

        Assert.Null(Classify(dep));
    }

    [Fact]
    public void CrossDatabaseReferenceToMsdb_IsNotReported()
    {
        // sp_Blitz / sp_AllNightLog read msdb backup & agent tables. msdb is infrastructure.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "sp_Blitz",
            ReferencedDatabase = "msdb", ReferencedSchema = "dbo", ReferencedEntity = "backupset"
        };

        var issue = Classify(dep,
            databases: Set("AppDb", "msdb"),
            infrastructure: Set("master", "msdb", "model", "tempdb"));

        Assert.Null(issue);
    }

    [Fact]
    public void XmlMethodReference_IsNotReported()
    {
        // xmlCol.value('…') surfaces as the bogus 3-part name "alias.column.value".
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "sp_BlitzQueryStore",
            ReferencedDatabase = "qp", ReferencedSchema = "query_plan", ReferencedEntity = "value"
        };

        Assert.Null(Classify(dep, databases: Set("AppDb")));
    }

    [Fact]
    public void UnresolvedLocalReference_ThatExistsInCatalog_IsNotReported()
    {
        // referenced_id is NULL but the object really exists (inter-proc call / deferred resolution).
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "sp_BlitzFirst",
            ReferencedSchema = "dbo", ReferencedEntity = "sp_BlitzWho",
            ReferencedIdIsNull = true
        };

        var issue = Classify(dep, objectsInDb: db => db == CurrentDb ? Set("dbo.sp_BlitzWho") : Set());

        Assert.Null(issue);
    }

    [Fact]
    public void CallerDependentLocalReference_IsNotReported()
    {
        // Temp tables / dynamic SQL legitimately don't bind.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "LoadStaging",
            ReferencedEntity = "#temp",
            ReferencedIdIsNull = true, IsCallerDependent = true
        };

        Assert.Null(Classify(dep));
    }

    [Fact]
    public void AmbiguousLocalReference_IsNotReported()
    {
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vThing",
            ReferencedEntity = "SomeName",
            ReferencedIdIsNull = true, IsAmbiguous = true
        };

        Assert.Null(Classify(dep));
    }

    [Fact]
    public void RegisteredLinkedServer_IsInfo()
    {
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "PullRemote",
            ReferencedServer = "REMOTESRV", ReferencedDatabase = "Sales",
            ReferencedSchema = "dbo", ReferencedEntity = "Customers"
        };

        var issue = Classify(dep, linkedServers: Set("REMOTESRV"));

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Info, issue.Severity);
        Assert.Equal(ReferenceKind.LinkedServer, issue.Kind);
    }

    [Fact]
    public void UnregisteredLinkedServer_IsError()
    {
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "PullRemote",
            ReferencedServer = "GONESRV", ReferencedDatabase = "Sales",
            ReferencedSchema = "dbo", ReferencedEntity = "Customers"
        };

        var issue = Classify(dep, linkedServers: Set("REMOTESRV"));

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ReferenceKind.LinkedServer, issue.Kind);
    }

    [Fact]
    public void CrossDatabase_MissingDatabase_IsError()
    {
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vOther",
            ReferencedDatabase = "GhostDb", ReferencedSchema = "dbo", ReferencedEntity = "Things"
        };

        var issue = Classify(dep, databases: Set("AppDb", "master"));

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ReferenceKind.CrossDatabase, issue.Kind);
        Assert.Contains("GhostDb", issue.Issue);
    }

    [Fact]
    public void CrossDatabase_MissingObject_IsError()
    {
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vOther",
            ReferencedDatabase = "Sales", ReferencedSchema = "dbo", ReferencedEntity = "GhostTable"
        };

        var issue = Classify(dep,
            databases: Set("AppDb", "Sales"),
            objectsInDb: db => db == "Sales" ? Set("dbo.Customers", "dbo.Orders") : Set());

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ReferenceKind.CrossDatabase, issue.Kind);
    }

    [Fact]
    public void CrossDatabase_ResolvingObject_IsInfo()
    {
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vOther",
            ReferencedDatabase = "Sales", ReferencedSchema = "dbo", ReferencedEntity = "Customers"
        };

        var issue = Classify(dep,
            databases: Set("AppDb", "Sales"),
            objectsInDb: db => db == "Sales" ? Set("dbo.Customers", "dbo.Orders") : Set());

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Info, issue.Severity);
        Assert.Equal(ReferenceKind.CrossDatabase, issue.Kind);
    }

    [Fact]
    public void CrossDatabase_UnenumerableDatabase_IsInfo()
    {
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vOther",
            ReferencedDatabase = "Sales", ReferencedSchema = "dbo", ReferencedEntity = "Customers"
        };

        var issue = Classify(dep,
            databases: Set("AppDb", "Sales"),
            objectsInDb: _ => null);

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Info, issue.Severity);
        Assert.Equal(ReferenceKind.CrossDatabase, issue.Kind);
    }

    [Fact]
    public void CrossDatabase_IgnoredDatabase_IsNotReported()
    {
        // References to master (system procs live outside sys.objects) are suppressed.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "DoThing",
            ReferencedDatabase = "master", ReferencedSchema = "dbo", ReferencedEntity = "sp_helptext"
        };

        var issue = Classify(dep,
            databases: Set("AppDb", "master"),
            objectsInDb: _ => Set(),
            infrastructure: Set("master"));

        Assert.Null(issue);
    }

    [Fact]
    public void LocalReference_ResolvingInMasterInfrastructure_IsNotReported()
    {
        // Ola Hallengren's procs live in master; a copy in another DB references dbo.CommandExecute,
        // which isn't in that DB but exists in master. Treated as infrastructure → not reported.
        var dep = new RawDependency
        {
            ReferencingType = "P", ReferencingSchema = "dbo", ReferencingName = "DatabaseBackup",
            ReferencedSchema = "dbo", ReferencedEntity = "CommandExecute",
            ReferencedIdIsNull = true
        };

        var issue = Classify(dep,
            databases: Set("AppDb", "master"),
            objectsInDb: db => db == "master" ? Set("dbo.CommandExecute", "dbo.CommandLog") : Set(),
            infrastructure: Set("master"));

        Assert.Null(issue);
    }

    [Fact]
    public void LocalReference_MissingEverywhere_IsStillError()
    {
        // A schema-qualified object that's absent from both the current DB and master is a real break.
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vReport",
            ReferencedSchema = "dbo", ReferencedEntity = "DroppedTable",
            ReferencedIdIsNull = true
        };

        var issue = Classify(dep,
            objectsInDb: _ => Set("dbo.SomethingElse"),
            infrastructure: Set("master"));

        Assert.NotNull(issue);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ReferenceKind.Local, issue.Kind);
    }

    [Fact]
    public void ThreePartReferenceToCurrentDatabase_TreatedAsLocal()
    {
        // db.schema.obj where db == current DB: not cross-database. Resolves → not reported.
        var dep = new RawDependency
        {
            ReferencingType = "V", ReferencingSchema = "dbo", ReferencingName = "vSelf",
            ReferencedDatabase = CurrentDb, ReferencedSchema = "dbo", ReferencedEntity = "Orders",
            ReferencedIdIsNull = false
        };

        Assert.Null(Classify(dep));
    }
}
