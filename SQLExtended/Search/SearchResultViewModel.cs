using System;
using System.Windows;
using SQLExtended.Cache.Models;

namespace SQLExtended.Search;

/// <summary>
/// View model wrapping a <see cref="SearchResult"/> — or a <see cref="JobStepMatch"/> — for display in the
/// results list.
///
/// Job steps are the one result that is not a database object: they have no schema, no database, and nothing
/// to script, so they are carried as a separate payload rather than squeezed into <see cref="SearchResult"/>'s
/// schema/object fields. <see cref="IsJobStep"/> is what every consumer branches on; the two Visibility
/// properties exist so the item template can swap its name line without a converter.
/// </summary>
internal sealed class SearchResultViewModel
{
    private readonly SearchResult _result;
    private readonly JobStepMatch _jobStep;

    public SearchResultViewModel(SearchResult result, string databaseName, string connectionKey, string connectionString)
    {
        _result = result;
        DatabaseName = databaseName;
        ConnectionKey = connectionKey;
        ConnectionString = connectionString;
    }

    public SearchResultViewModel(JobStepMatch jobStep, string connectionKey, string connectionString)
    {
        _jobStep = jobStep;
        // The step's *target* database, not one of the searched databases — the job itself lives in msdb.
        DatabaseName = jobStep.StepDatabase;
        ConnectionKey = connectionKey;
        ConnectionString = connectionString;
    }

    public bool IsJobStep => _jobStep != null;
    public JobStepMatch JobStep => _jobStep;

    public Visibility ObjectNameVisibility => IsJobStep ? Visibility.Collapsed : Visibility.Visible;
    public Visibility JobNameVisibility => IsJobStep ? Visibility.Visible : Visibility.Collapsed;

    public string SchemaName => _result?.SchemaName;
    public string ObjectName => IsJobStep ? _jobStep.StepName : _result.ObjectName;
    public string ObjectType => IsJobStep ? "JobStep" : _result.ObjectType;
    public string MatchLocation => IsJobStep ? _jobStep.MatchedIn : _result.MatchLocation;
    public string MatchDetail => IsJobStep ? _jobStep.Snippet : _result.MatchDetail;
    public string DatabaseName { get; }
    public string ConnectionKey { get; }
    public string ConnectionString { get; }

    // --- Job step display ---

    public string JobName => _jobStep?.JobName;

    /// <summary>"Step 3: Load facts" — the step's addressable identity within its job.</summary>
    public string StepDisplay => IsJobStep ? $"Step {_jobStep.StepId}: {_jobStep.StepName}" : "";

    /// <summary>Subsystem, target database and enabled state, parenthesised for the end of the name line.</summary>
    public string JobContext
    {
        get
        {
            string context = BuildJobContext();
            return string.IsNullOrEmpty(context) ? "" : $"  ({context})";
        }
    }

    private string BuildJobContext()
    {
        if (!IsJobStep) return "";

        string context = _jobStep.Subsystem ?? "";
        // database_name is only populated for TSQL steps; for the other subsystems it holds nothing useful.
        if (!string.IsNullOrEmpty(_jobStep.StepDatabase) && string.Equals(_jobStep.Subsystem, "TSQL", StringComparison.OrdinalIgnoreCase))
            context = string.IsNullOrEmpty(context) ? _jobStep.StepDatabase : $"{context} · {_jobStep.StepDatabase}";
        if (!_jobStep.JobEnabled)
            context = string.IsNullOrEmpty(context) ? "disabled" : $"{context} · disabled";
        return context;
    }

    // --- Shared display ---

    public string DisplayName => IsJobStep
        ? _jobStep.StepName
        : (MatchLocation == "ColumnName" ? _result.MatchDetail : _result.ObjectName);

    public string ColumnSuffix => !IsJobStep && MatchLocation == "ColumnName" ? $".{_result.MatchDetail}" : "";

    public string QualifiedName => IsJobStep
        ? $"{_jobStep.JobName} — {StepDisplay}"
        : (MatchLocation == "ColumnName"
            ? $"{_result.SchemaName}.{_result.ObjectName}.{_result.MatchDetail}"
            : $"{_result.SchemaName}.{_result.ObjectName}");

    public string TypeIcon => ObjectType switch
    {
        "U" => "📋",   // table
        "V" => "👁",   // view (eye)
        "P" => "⚙",          // procedure (gear)
        "FN" or "IF" or "TF" => "ƒ", // function
        "SN" => "↔",         // synonym
        "Column" => "📄", // column
        "JobStep" => "⏱",    // agent job step (stopwatch)
        _ => "□"             // generic square
    };

    public string TypeLabel => ObjectType switch
    {
        "U" => "TABLE",
        "V" => "VIEW",
        "P" => "PROCEDURE",
        "FN" => "SCALAR FUNCTION",
        "IF" => "INLINE FUNCTION",
        "TF" => "TABLE FUNCTION",
        "SN" => "SYNONYM",
        "TT" => "TABLE TYPE",
        "Column" => "COLUMN",
        "JobStep" => "AGENT JOB STEP",
        _ => ObjectType
    };

    public string MatchDescription => MatchLocation switch
    {
        "ObjectName" => "Matched: object name",
        "ColumnName" => $"Matched: column in {_result.SchemaName}.{_result.ObjectName}",
        "Definition" => string.IsNullOrEmpty(_result.MatchDetail)
            ? "Matched: definition"
            : $"Matched: definition — {_result.MatchDetail.Trim()}",
        "Command" => string.IsNullOrEmpty(_jobStep?.Snippet)
            ? "Matched: job step command"
            : $"Matched: job step command — {_jobStep.Snippet}",
        "StepName" => "Matched: job step name",
        "JobName" => $"Matched: job name — {_jobStep?.JobName}",
        "JobStep" => "Matched: job step",
        _ => $"Matched: {MatchLocation}"
    };
}
