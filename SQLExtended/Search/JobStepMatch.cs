using System;

namespace SQLExtended.Search;

/// <summary>
/// One SQL Server Agent job step that matched the search term.
///
/// Deliberately not a <see cref="Cache.Models.SearchResult"/>: jobs live in msdb and belong to the *server*,
/// not to any of the databases the search is scoped to, and none of the cache's plumbing (the SQLite store,
/// the object/column indexes, IntelliSense) has anything to say about them. They are read live per search —
/// see <see cref="JobStepSearchService"/> — and joined to the object results only at the view-model layer.
/// </summary>
internal sealed class JobStepMatch
{
    public Guid JobId { get; set; }
    public string JobName { get; set; }
    public bool JobEnabled { get; set; }

    public int StepId { get; set; }
    public string StepName { get; set; }

    /// <summary>TSQL, CmdExec, PowerShell, SSIS, … — from <c>sysjobsteps.subsystem</c>.</summary>
    public string Subsystem { get; set; }

    /// <summary>The step's target database. Only meaningful for the TSQL subsystem; null otherwise.</summary>
    public string StepDatabase { get; set; }

    /// <summary>The step's full command text, shown in the preview pane.</summary>
    public string Command { get; set; }

    /// <summary>Which field the term was found in: Command, StepName, JobName, or JobStep when the server
    /// matched but the client-side scan could not say where (see <see cref="JobStepSearchService"/>).</summary>
    public string MatchedIn { get; set; }

    /// <summary>A one-line excerpt of the command around the match, or null when the match was on a name.</summary>
    public string Snippet { get; set; }
}
