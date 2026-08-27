namespace SQLExtended.Validation.Models;

/// <summary>How severe a validation finding is.</summary>
internal enum IssueSeverity
{
    /// <summary>The reference is broken — the target does not exist / is not registered.</summary>
    Error,

    /// <summary>The reference resolves (or cannot be verified remotely). Listed for information only.</summary>
    Info
}

/// <summary>What kind of reference the finding describes.</summary>
internal enum ReferenceKind
{
    Local,
    CrossDatabase,
    LinkedServer
}

/// <summary>
/// A raw dependency row read from <c>sys.sql_expression_dependencies</c> joined to <c>sys.objects</c>.
/// Plain data — no SQL/VS dependencies — so it can be unit tested without a database.
/// </summary>
internal struct RawDependency
{
    /// <summary>sys.objects.object_id of the referencing module — used to fetch its definition.</summary>
    public int ReferencingId;

    /// <summary>sys.objects type code of the referencing module: V, P, FN, IF, TF, TR.</summary>
    public string ReferencingType;
    public string ReferencingSchema;
    public string ReferencingName;

    public string ReferencedServer;
    public string ReferencedDatabase;
    public string ReferencedSchema;
    public string ReferencedEntity;

    /// <summary>True when sys.sql_expression_dependencies.referenced_id is NULL (could not be bound).</summary>
    public bool ReferencedIdIsNull;

    public bool IsCallerDependent;
    public bool IsAmbiguous;
}

/// <summary>
/// One row in the Schema Validation grid: a reference made by a module and the verdict on its target.
/// </summary>
internal sealed class ValidationIssue
{
    /// <summary>The database that holds the referencing module.</summary>
    public string DatabaseName { get; set; }

    public string ReferencingType { get; set; }
    public string ReferencingSchema { get; set; }
    public string ReferencingName { get; set; }

    public ReferenceKind Kind { get; set; }

    public string ReferencedServer { get; set; }
    public string ReferencedDatabase { get; set; }
    public string ReferencedSchema { get; set; }
    public string ReferencedEntity { get; set; }

    public IssueSeverity Severity { get; set; }

    /// <summary>Human-readable description of the verdict.</summary>
    public string Issue { get; set; }

    // Connection context for grid actions (open in schema viewer). Not used by the classifier.
    public string ConnectionString { get; set; }
    public string ConnectionKey { get; set; }

    // --- Display helpers (bound by the grid) ---

    public string SeverityText => Severity == IssueSeverity.Error ? "Error" : "Info";

    public string ReferencingTypeLabel => ReferencingType?.Trim() switch
    {
        "V" => "View",
        "P" => "Procedure",
        "FN" => "Scalar Function",
        "IF" => "Inline Function",
        "TF" => "Table Function",
        "TR" => "Trigger",
        _ => ReferencingType
    };

    public string KindLabel => Kind switch
    {
        ReferenceKind.Local => "Local",
        ReferenceKind.CrossDatabase => "Cross-database",
        ReferenceKind.LinkedServer => "Linked server",
        _ => ""
    };

    public string ReferencingDisplay => $"[{ReferencingSchema}].[{ReferencingName}]";

    /// <summary>Fully qualified referenced name, using only the parts that are present.</summary>
    public string ReferencedDisplay
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(4);
            if (!string.IsNullOrEmpty(ReferencedServer)) parts.Add($"[{ReferencedServer}]");
            if (!string.IsNullOrEmpty(ReferencedDatabase)) parts.Add($"[{ReferencedDatabase}]");
            if (!string.IsNullOrEmpty(ReferencedSchema)) parts.Add($"[{ReferencedSchema}]");
            if (!string.IsNullOrEmpty(ReferencedEntity)) parts.Add($"[{ReferencedEntity}]");
            return string.Join(".", parts);
        }
    }
}
