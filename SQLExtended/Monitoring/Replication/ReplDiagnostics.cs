using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.Monitoring.Replication;

/// <summary>
/// The thresholds the replication rules judge against. Transactional replication normally runs a few seconds
/// behind, so the warning level starts well above that — a rule that fires on a healthy topology teaches people
/// to ignore the tab.
/// </summary>
internal sealed class ReplThresholds
{
    /// <summary>End-to-end latency (log reader + distribution) that counts as degraded, then as broken.</summary>
    public double LatencyWarningSeconds { get; set; } = 60d;
    public double LatencyCriticalSeconds { get; set; } = 300d;

    /// <summary>
    /// Fraction of the distribution retention period a subscription may go without activity before it is
    /// reported. Past 1.0 the expiry job deactivates it and it needs reinitializing, so the default leaves room.
    /// </summary>
    public double ExpiryWarningFraction { get; set; } = 0.75d;

    /// <summary>Undelivered command count above which a backlog is reported. Only ever set by the on-demand load.</summary>
    public long PendingCommandWarning { get; set; } = 100_000L;
}

/// <summary>
/// Turns a collected <see cref="ReplSnapshot"/> into a ranked list of findings.
///
/// Replication needs this more than Always On does. Its state is spread over four history tables that store
/// numeric run statuses with no descriptive column, latency in milliseconds in one place and seconds in another,
/// and the two failures that actually take a server down — a subscription about to expire, and a publisher log
/// that cannot truncate — are not expressed as a status anywhere at all. A grid of raw columns leaves the reader
/// to know all of that; this does not.
///
/// The severity choices worth defending:
///  * A <b>deactivated subscription</b> is critical even though nothing is erroring. It has passed the
///    distribution retention window, and the fix is a reinitialize and a fresh snapshot, not a restart.
///  * A <b>published log held by REPLICATION</b> is critical once the log is nearly full and a warning before
///    that, because the consequence is a full disk rather than stale data.
///  * A <b>disabled agent job</b> is a warning rather than an error: it is often deliberate during maintenance,
///    but nothing will move until it is back, and no latency figure says so.
///
/// Pure and side-effect free apart from filling <see cref="ReplSnapshot.Issues"/>.
/// </summary>
internal static class ReplDiagnostics
{
    public static void Evaluate(ReplSnapshot snapshot, ReplCapabilities caps, ReplThresholds thresholds)
    {
        if (snapshot == null || !snapshot.IsAvailable) return;
        thresholds = thresholds ?? new ReplThresholds();

        var issues = snapshot.Issues;
        issues.Clear();

        CheckTopology(snapshot, caps, issues);
        CheckSubscriptions(snapshot, thresholds, issues);
        CheckAgents(snapshot, issues);
        CheckPublications(snapshot, issues);
        CheckPublisherDatabases(snapshot, thresholds, issues);

        issues.Sort((a, b) =>
        {
            int bySeverity = a.Severity.CompareTo(b.Severity);
            if (bySeverity != 0) return bySeverity;
            int byArea = string.Compare(a.Area, b.Area, StringComparison.OrdinalIgnoreCase);
            return byArea != 0 ? byArea : string.Compare(a.Subject, b.Subject, StringComparison.OrdinalIgnoreCase);
        });

        AddAllClear(snapshot, caps, issues);
    }

    private static void Add(List<ReplIssueRow> issues, ReplIssueSeverity severity, string area, string subject, string detail, string recommendation) =>
        issues.Add(new ReplIssueRow { Severity = severity, Area = area, Subject = subject, Detail = detail, Recommendation = recommendation });

    // -------------------------------------------------------------------------------------------------
    // Topology — what this connection can and cannot see
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Says what is missing from the picture before the reader concludes anything from an empty grid. A publisher
    /// with a remote distributor legitimately shows no subscriptions here, and that must not read as "no
    /// subscriptions exist".
    /// </summary>
    private static void CheckTopology(ReplSnapshot snapshot, ReplCapabilities caps, List<ReplIssueRow> issues)
    {
        var role = snapshot.Role;

        if (!role.IsDistributor)
        {
            string distributor = string.IsNullOrWhiteSpace(role.DistributorName) ? null : role.DistributorName;
            Add(issues, ReplIssueSeverity.Information, "Topology", snapshot.ServerName ?? "(server)",
                distributor != null
                    ? $"This instance is not the distributor — its distributor is {distributor}."
                    : "This instance is not a distributor, so the distribution database is not readable from here.",
                "Publications, subscriptions, agents and errors all live in the distribution database. Connect a query window to the distributor for those tabs; the Publisher tab works from here regardless.");
        }

        if (caps != null && role.IsDistributor && !caps.CanReadJobs)
        {
            Add(issues, ReplIssueSeverity.Information, "Topology", snapshot.ServerName ?? "(server)",
                "msdb is not readable by this login, so the agent-job columns are blank.",
                "Job enabled and running state come from msdb.dbo.sysjobs and sysjobactivity. Without them a stalled agent cannot be distinguished from an agent whose job is simply disabled. SQLAgentReaderRole is enough.");
        }

        if (role.MaxDistributionRetentionHours != null)
        {
            Add(issues, ReplIssueSeverity.Information, "Topology", role.DistributorName ?? snapshot.ServerName ?? "(distributor)",
                $"Distribution retention is {Describe(role.MinDistributionRetentionHours)} to {Describe(role.MaxDistributionRetentionHours)}; history retention {Describe(role.HistoryRetentionHours)}.",
                "A subscription that goes without activity for longer than the maximum retention is deactivated by the expiry job and has to be reinitialized. That window is what the expiry warnings below are measured against.");
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Subscriptions — the rows that answer "is it working"
    // -------------------------------------------------------------------------------------------------

    private static void CheckSubscriptions(ReplSnapshot snapshot, ReplThresholds thresholds, List<ReplIssueRow> issues)
    {
        foreach (var sub in snapshot.Subscriptions)
        {
            string subject = $"{sub.Publication} → {sub.Subscriber}.{sub.SubscriberDb}";

            if (string.Equals(sub.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, ReplIssueSeverity.Critical, "Subscription", subject,
                    "The subscription is marked inactive.",
                    "It went longer than the distribution retention period without activity and the expiry job deactivated it. Restarting the agent will not fix this — the subscription has to be reinitialized and a new snapshot applied.");
            }

            if (sub.RunStatus == ReplRunStatus.Failed)
            {
                string error = string.IsNullOrWhiteSpace(sub.LastError) ? sub.LastComment : sub.LastError;
                Add(issues, ReplIssueSeverity.Critical, "Subscription", subject,
                    "The distribution agent's last run failed." + (string.IsNullOrWhiteSpace(error) ? "" : " " + Trim(error)),
                    "Nothing is being applied at the subscriber while this stands. The Errors tab has the full text and the transaction sequence number it stopped on.");
            }
            else if (sub.RunStatus == ReplRunStatus.Retrying)
            {
                Add(issues, ReplIssueSeverity.Warning, "Subscription", subject,
                    "The distribution agent is retrying." + (string.IsNullOrWhiteSpace(sub.LastComment) ? "" : " " + Trim(sub.LastComment)),
                    "A transient failure the agent expects to recover from — a blocked apply, a brief network loss. If it is still retrying on the next few polls, treat it as failed.");
            }

            double? latency = sub.TotalLatencySeconds;
            if (latency != null)
            {
                if (latency > thresholds.LatencyCriticalSeconds)
                {
                    Add(issues, ReplIssueSeverity.Critical, "Latency", subject,
                        $"End-to-end latency is {Describe(latency.Value / 3600d)} ({Describe(sub.LogReaderLatencySeconds / 3600d)} log reader + {Describe(sub.DistributionLatencySeconds / 3600d)} distribution).",
                        "The subscriber is that far behind the publisher. The split says where to look: log reader latency is publisher-to-distributor, distribution latency is distributor-to-subscriber.");
                }
                else if (latency > thresholds.LatencyWarningSeconds)
                {
                    Add(issues, ReplIssueSeverity.Warning, "Latency", subject,
                        $"End-to-end latency is {Describe(latency.Value / 3600d)}.",
                        "Above the warning threshold but still moving. Load pending commands on the Subscriptions tab to see whether a backlog is draining or growing.");
                }
            }

            double? used = sub.RetentionUsedFraction;
            if (used != null && used >= 1d)
            {
                Add(issues, ReplIssueSeverity.Critical, "Expiry", subject,
                    $"No activity for {Describe(sub.HoursSinceActivity)}, past the {Describe(sub.RetentionHours)} distribution retention window.",
                    "The subscription is at or past expiry. Expect the expiry job to deactivate it, after which it needs reinitializing rather than restarting.");
            }
            else if (used != null && used > thresholds.ExpiryWarningFraction)
            {
                Add(issues, ReplIssueSeverity.Warning, "Expiry", subject,
                    $"No activity for {Describe(sub.HoursSinceActivity)} of a {Describe(sub.RetentionHours)} retention window ({used.Value * 100:N0}% used).",
                    "Get the agent running again before the window closes. Once it expires, recovery costs a new snapshot rather than a restart.");
            }

            if (sub.JobEnabled == false)
            {
                Add(issues, ReplIssueSeverity.Warning, "Agent job", subject,
                    $"The agent's SQL Server Agent job{(string.IsNullOrEmpty(sub.JobName) ? "" : $" ({sub.JobName})")} is disabled.",
                    "Nothing will be delivered until it is enabled, and the subscription's retention window keeps running down in the meantime. Often deliberate during maintenance — but it does not resume by itself.");
            }

            if (sub.UndeliveredCommands.GetValueOrDefault() > thresholds.PendingCommandWarning)
            {
                Add(issues, ReplIssueSeverity.Warning, "Backlog", subject,
                    $"{sub.UndeliveredCommands:N0} undelivered command(s) queued in the distribution database.",
                    "Refresh the pending counts again in a minute: falling means it is draining, level or rising means the agent cannot keep up with the publisher.");
            }
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Agents and publications
    // -------------------------------------------------------------------------------------------------

    private static void CheckAgents(ReplSnapshot snapshot, List<ReplIssueRow> issues)
    {
        foreach (var agent in snapshot.Agents)
        {
            // Distribution-agent failures are already reported against their subscription, which names the
            // publication and subscriber — a far more useful subject than the agent's own generated name.
            if (agent.AgentType == ReplAgentType.Distribution) continue;

            string subject = Describe(agent);

            if (agent.RunStatus == ReplRunStatus.Failed)
            {
                string error = string.IsNullOrWhiteSpace(agent.LastError) ? agent.Comments : agent.LastError;
                var severity = agent.AgentType == ReplAgentType.LogReader ? ReplIssueSeverity.Critical : ReplIssueSeverity.Warning;

                Add(issues, severity, agent.AgentTypeText, subject,
                    "The agent's last run failed." + (string.IsNullOrWhiteSpace(error) ? "" : " " + Trim(error)),
                    agent.AgentType == ReplAgentType.LogReader
                        ? "Nothing is reaching the distributor from this published database, and its transaction log cannot be truncated while that is true. Check the Publisher tab for the log growth this causes."
                        : "The snapshot or merge run did not complete. New and reinitialized subscriptions cannot start without a good snapshot.");
            }
            else if (agent.RunStatus == ReplRunStatus.Retrying)
            {
                Add(issues, ReplIssueSeverity.Warning, agent.AgentTypeText, subject,
                    "The agent is retrying." + (string.IsNullOrWhiteSpace(agent.Comments) ? "" : " " + Trim(agent.Comments)),
                    "Transient by design. Worth a second look if it is still retrying on the next few polls.");
            }

            if (agent.JobEnabled == false)
            {
                Add(issues, ReplIssueSeverity.Warning, "Agent job", subject,
                    $"The agent's SQL Server Agent job{(string.IsNullOrEmpty(agent.JobName) ? "" : $" ({agent.JobName})")} is disabled.",
                    "The agent cannot run at all until the job is enabled.");
            }

            if (agent.Conflicts.GetValueOrDefault() > 0)
            {
                Add(issues, ReplIssueSeverity.Warning, "Merge", subject,
                    $"The last merge session resolved {agent.Conflicts:N0} conflict(s).",
                    "Merge replication resolves conflicts silently according to the article's resolver, so a losing change has been discarded or overwritten. The conflict tables in the publication database hold what happened.");
            }
        }
    }

    private static void CheckPublications(ReplSnapshot snapshot, List<ReplIssueRow> issues)
    {
        foreach (var publication in snapshot.Publications)
        {
            string subject = $"{publication.Publisher}.{publication.PublisherDb} / {publication.Publication}";

            if (publication.SnapshotStatus == ReplRunStatus.Failed)
            {
                Add(issues, ReplIssueSeverity.Warning, "Snapshot", subject,
                    "The publication's last snapshot run failed.",
                    "Existing subscriptions keep flowing, but a new or reinitialized one cannot be started until a snapshot succeeds.");
            }

            if (publication.SubscriptionCount == 0)
            {
                Add(issues, ReplIssueSeverity.Warning, "Publication", subject,
                    "The publication has no subscriptions.",
                    "For a transactional publication the log reader still marks every change in the publisher's log as needing replication, so the log cannot be truncated even though nothing consumes it. Drop the publication if it is genuinely unused.");
            }
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Publisher side — the failure that costs you the server rather than just the data
    // -------------------------------------------------------------------------------------------------

    private static void CheckPublisherDatabases(ReplSnapshot snapshot, ReplThresholds thresholds, List<ReplIssueRow> issues)
    {
        foreach (var db in snapshot.PublisherDatabases)
        {
            if (db.IsLogHeldByReplication)
            {
                string fill = db.LogPercentUsed == null ? "" : $" The log is {db.LogPercentUsed.Value:N0}% full.";
                bool nearlyFull = db.LogPercentUsed.GetValueOrDefault() >= 80d;

                Add(issues, nearlyFull ? ReplIssueSeverity.Critical : ReplIssueSeverity.Warning, "Publisher log", db.DatabaseName,
                    "log_reuse_wait_desc is REPLICATION — the transaction log cannot be truncated." + fill,
                    nearlyFull
                        ? "This is how stalled replication takes an instance down: the log grows until the disk fills, and no log backup will help. Get the log reader draining, or if replication has been abandoned here, remove it (sp_removedbreplication) rather than adding disk."
                        : "The log reader has not yet drained the changes it needs to. Normal in short bursts; a standing condition means the log will keep growing.");
            }

            if (db.ReplicationLatencySeconds.GetValueOrDefault() > thresholds.LatencyWarningSeconds)
            {
                Add(issues, ReplIssueSeverity.Warning, "Publisher log", db.DatabaseName,
                    $"sp_replcounters reports {Describe(db.ReplicationLatencySeconds / 3600d)} of replication latency with {db.ReplicatedTransactions:N0} transaction(s) waiting.",
                    "This is the publisher's own measure of how far behind the log reader is, taken before the distributor is involved at all — so it isolates the first hop.");
            }

            if (db.IsPublished && string.Equals(db.RecoveryModel, "SIMPLE", StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, ReplIssueSeverity.Information, "Publisher log", db.DatabaseName,
                    "The database is published and in SIMPLE recovery.",
                    "Transactional replication works in SIMPLE recovery — the log is still held until the log reader drains it — but point-in-time recovery is not available, so a failure means reinitializing subscriptions from the last full backup.");
            }
        }
    }

    /// <summary>
    /// When nothing fired, say so and list what was looked at. An empty grid reads equally as "healthy" and as
    /// "this tab is broken", and "no issues" without a scope is a claim the dashboard has not earned.
    /// </summary>
    private static void AddAllClear(ReplSnapshot snapshot, ReplCapabilities caps, List<ReplIssueRow> issues)
    {
        if (issues.Any(i => i.Severity != ReplIssueSeverity.Information)) return;

        string scope = $"{snapshot.Publications.Count} publication(s), {snapshot.Subscriptions.Count} subscription(s), {snapshot.Agents.Count} agent(s)";
        string roles = caps?.DescribeRoles() ?? "";

        string recommendation = "Checked: agent run status and errors, end-to-end latency, subscription expiry against the distribution retention window, agent job state, snapshot health, merge conflicts, and whether any published database's log is held by replication.";

        if (!snapshot.Role.IsDistributor)
            recommendation += " This connection is not the distributor, so only the publisher-side checks ran — connect to the distributor for the rest.";

        issues.Insert(0, new ReplIssueRow
        {
            Severity = ReplIssueSeverity.Information,
            Area = "Summary",
            Subject = snapshot.ServerName ?? "(server)",
            Detail = $"No problems found across {scope}" + (string.IsNullOrEmpty(roles) ? "." : $" — this instance is {roles}."),
            Recommendation = recommendation
        });
    }

    // -------------------------------------------------------------------------------------------------
    // Formatting — findings are read as prose, so numbers get units here rather than in the XAML
    // -------------------------------------------------------------------------------------------------

    private static string Describe(ReplAgentRow agent)
    {
        if (agent.AgentType == ReplAgentType.LogReader) return $"{agent.Publisher}.{agent.PublisherDb}";
        if (!string.IsNullOrEmpty(agent.Subscriber)) return $"{agent.Publication} → {agent.Subscriber}.{agent.SubscriberDb}";
        return $"{agent.Publisher}.{agent.PublisherDb} / {agent.Publication}";
    }

    /// <summary>Formats an hour count as a readable duration. Hours because every retention value here is in hours.</summary>
    private static string Describe(double? hours)
    {
        if (hours == null || double.IsNaN(hours.Value) || double.IsInfinity(hours.Value) || hours < 0) return "an unknown time";

        double seconds = hours.Value * 3600d;
        if (seconds < 1) return "under a second";
        if (seconds < 60) return $"{seconds:N0}s";
        if (seconds < 3600) return TimeSpan.FromSeconds(seconds).ToString(@"m\m\ ss\s");
        if (seconds < 86400) return TimeSpan.FromSeconds(seconds).ToString(@"h\h\ mm\m");
        return TimeSpan.FromSeconds(seconds).ToString(@"d\d\ h\h");
    }

    /// <summary>Collapses an agent comment or error onto one line — they routinely arrive with embedded newlines.</summary>
    private static string Trim(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
