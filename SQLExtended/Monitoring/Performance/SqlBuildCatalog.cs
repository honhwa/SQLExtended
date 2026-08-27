using System;
using System.Collections.Generic;
using System.Globalization;

namespace SQLExtended.Monitoring.Performance;

/// <summary>What kind of release a build is. Derived at generation time from the build list's own description.</summary>
internal enum SqlBuildKind
{
    Unknown,
    Preview,
    Rtm,
    ServicePack,
    CumulativeUpdate,
    SecurityUpdate,
    FeaturePack,
    Hotfix
}

/// <summary>
/// A four-part build number, compared component by component.
///
/// <para><b>Never compare these as strings.</b> <c>16.0.4265.3</c> sorts below <c>16.0.985.1</c>
/// lexically, which would report a fully patched server as years behind and a stale one as current — the
/// single most damaging thing this file could get wrong.</para>
/// </summary>
internal readonly struct SqlVersion : IComparable<SqlVersion>, IEquatable<SqlVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Build { get; }
    public int Revision { get; }

    public SqlVersion(int major, int minor, int build, int revision)
    {
        Major = major;
        Minor = minor;
        Build = build;
        Revision = revision;
    }

    public bool IsEmpty => Major == 0 && Minor == 0 && Build == 0 && Revision == 0;

    /// <summary>
    /// Parses "16.0.4265.3", and the two- and three-part forms the build list also carries ("8.0.194",
    /// "6.50.201"). Missing components are zero, which is what makes an RTM listed as three parts compare
    /// correctly against a four-part build number.
    /// </summary>
    public static bool TryParse(string text, out SqlVersion version)
    {
        version = default(SqlVersion);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Trim().Split('.');
        if (parts.Length < 2 || parts.Length > 4) return false;

        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            // int.Parse rather than a regex so a leading zero ("9.0.1399.06") is accepted as written.
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }

        version = new SqlVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>
    /// The (major, minor) pair naming the release. Minor is not decoration: 10.50 is SQL Server 2008 R2 and
    /// 10.0 is 2008 — different products, five years apart in support dates.
    /// </summary>
    public string ReleaseKey => Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(SqlVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Build.CompareTo(other.Build);
        return result != 0 ? result : Revision.CompareTo(other.Revision);
    }

    public bool Equals(SqlVersion other) => CompareTo(other) == 0;
    public override bool Equals(object obj) => obj is SqlVersion other && Equals(other);
    public override int GetHashCode() => (((Major * 397) ^ Minor) * 397 ^ Build) * 397 ^ Revision;

    public override string ToString() => string.Join(".", new[]
    {
        Major.ToString(CultureInfo.InvariantCulture),
        Minor.ToString(CultureInfo.InvariantCulture),
        Build.ToString(CultureInfo.InvariantCulture),
        Revision.ToString(CultureInfo.InvariantCulture)
    });

    public static bool operator >(SqlVersion a, SqlVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SqlVersion a, SqlVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(SqlVersion a, SqlVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(SqlVersion a, SqlVersion b) => a.CompareTo(b) <= 0;
    public static bool operator ==(SqlVersion a, SqlVersion b) => a.CompareTo(b) == 0;
    public static bool operator !=(SqlVersion a, SqlVersion b) => a.CompareTo(b) != 0;
}

/// <summary>One row of the build list.</summary>
internal sealed class SqlServerBuild
{
    public string Build { get; set; }
    public SqlVersion Version { get; set; }

    /// <summary>The servicing level in the form a DBA says it — "CU12", "SP2 CU17", "RTM + security update".</summary>
    public string Label { get; set; }

    public SqlBuildKind Kind { get; set; }
    public string KbNumber { get; set; }
    public DateTime? Released { get; set; }

    /// <summary>Microsoft pulled this build after shipping it. Running one is a finding, not a footnote.</summary>
    public bool Withdrawn { get; set; }

    /// <summary>The build list's own wording, kept verbatim so the derived <see cref="Label"/> is always checkable.</summary>
    public string Description { get; set; }

    public SqlServerRelease Release { get; set; }

    public string KbUrl => string.IsNullOrEmpty(KbNumber) ? null : "https://support.microsoft.com/help/" + KbNumber;

    /// <summary>"CU12 (16.0.4185.3)", or just the build number where no level could be derived.</summary>
    public string Display => string.IsNullOrEmpty(Label) ? Build : Label + " (" + Build + ")";

    /// <summary>The level as shown in the updates grid, saying so when the build was withdrawn.</summary>
    public string LevelDisplay => Withdrawn ? (string.IsNullOrEmpty(Label) ? "withdrawn" : Label + " (withdrawn)") : Label;

    /// <summary>Tints the row in the updates grid — a withdrawn build is one not to install.</summary>
    public bool IsWarning => Withdrawn;
}

/// <summary>One major release, its lifecycle dates, and every build listed under it.</summary>
internal sealed class SqlServerRelease
{
    public string Key { get; set; }
    public string Name { get; set; }
    public string Codename { get; set; }
    public string RtmBuild { get; set; }
    public DateTime? Released { get; set; }
    public DateTime? MainstreamSupportEnd { get; set; }
    public DateTime? ExtendedSupportEnd { get; set; }

    /// <summary>Descending by build number — newest first, as the source page lists them.</summary>
    public List<SqlServerBuild> Builds { get; } = new List<SqlServerBuild>();

    /// <summary>The highest build known for this release, whatever kind it is.</summary>
    public SqlServerBuild LatestBuild { get; set; }

    /// <summary>The highest cumulative update known for this release. Null for releases that predate CUs.</summary>
    public SqlServerBuild LatestCumulativeUpdate { get; set; }
}

/// <summary>Where a release sits in its support lifecycle.</summary>
internal enum SqlSupportPhase
{
    Unknown,
    Mainstream,
    Extended,
    OutOfSupport
}

/// <summary>
/// What the build list can say about one server's <c>ProductVersion</c>.
///
/// <para>The distinctions here are the point of the type. "I don't know this build", "I know it and it is the
/// newest listed" and "it is newer than anything the snapshot lists" are three different answers, and
/// collapsing any two of them turns an unpatched server into a clean bill of health.</para>
/// </summary>
internal sealed class SqlBuildMatch
{
    /// <summary>The version as reported by the server. Set even when nothing else could be resolved.</summary>
    public SqlVersion Version { get; set; }

    /// <summary>Null when the (major, minor) pair is not a release the snapshot knows — a much newer SQL Server.</summary>
    public SqlServerRelease Release { get; set; }

    /// <summary>The exact listed build, when there is one.</summary>
    public SqlServerBuild Exact { get; set; }

    /// <summary>
    /// The highest listed build below this one, when there is no exact match. The build list does not claim to
    /// be exhaustive, so this says "at least this level" rather than pretending to identify the build.
    /// </summary>
    public SqlServerBuild ClosestBelow { get; set; }

    /// <summary>Listed builds strictly newer than this one.</summary>
    public int NewerBuilds { get; set; }

    /// <summary>Listed cumulative updates strictly newer than this one — the number a DBA plans around.</summary>
    public int NewerCumulativeUpdates { get; set; }

    /// <summary>
    /// The build is above every build listed for its release, so the snapshot cannot speak to it. Distinct from
    /// "newest known": the honest reading is "the list is older than this server", not "you are current".
    /// </summary>
    public bool NewerThanCatalog { get; set; }

    /// <summary>Nothing newer is listed, and the build itself is listed.</summary>
    public bool IsLatestKnown => Exact != null && NewerBuilds == 0;

    /// <summary>The best identification available: the exact build, else the closest one below it.</summary>
    public SqlServerBuild Best => Exact ?? ClosestBelow;

    public SqlSupportPhase Phase { get; set; }

    /// <summary>Days until the end of the phase the release is in; negative once that date has passed.</summary>
    public int? DaysUntilSupportEnds { get; set; }
}

/// <summary>
/// The embedded SQL Server build list, parsed on first use, and the lookup that turns a server's
/// <c>ProductVersion</c> into a servicing level and a support-lifecycle position.
///
/// <para>Deliberately free of SqlClient and WPF so the test project can link it — every rule in here is one
/// that fails silently in the UI (a build matched to the wrong release, a version compared as text, a stale
/// snapshot reported as "current") and none of it can be checked against a live server.</para>
///
/// <para><b>The data is a snapshot and the code never pretends otherwise.</b> There is no runtime fetch: this
/// runs inside SSMS on machines that are frequently offline or locked down, and a monitoring tab is the last
/// place a surprise outbound HTTP request belongs. <see cref="SqlBuildData.SnapshotDate"/> is shown on the tab
/// and <see cref="SqlBuildMatch.NewerThanCatalog"/> exists so a server ahead of the snapshot is reported as
/// "the list is out of date", never as up to date.</para>
/// </summary>
internal static class SqlBuildCatalog
{
    private static readonly Lazy<Dictionary<string, SqlServerRelease>> ReleasesByKey =
        new Lazy<Dictionary<string, SqlServerRelease>>(Parse);

    public static string SnapshotDate => SqlBuildData.SnapshotDate;
    public static string SourceUrl => SqlBuildData.SourceUrl;

    /// <summary>Every release in the snapshot, newest first.</summary>
    public static IEnumerable<SqlServerRelease> Releases => ReleasesByKey.Value.Values;

    public static SqlServerRelease ReleaseFor(SqlVersion version)
    {
        SqlServerRelease release;
        return ReleasesByKey.Value.TryGetValue(version.ReleaseKey, out release) ? release : null;
    }

    /// <summary>
    /// Resolves a <c>SERVERPROPERTY('ProductVersion')</c> string against the snapshot. Never returns null: an
    /// unparseable or unknown version still yields a match object saying exactly that, because the caller has
    /// to display something and "unknown" is a legitimate answer.
    /// </summary>
    public static SqlBuildMatch Lookup(string productVersion, DateTime asOf)
    {
        var match = new SqlBuildMatch();

        SqlVersion version;
        if (!SqlVersion.TryParse(productVersion, out version)) return match;

        match.Version = version;
        match.Release = ReleaseFor(version);
        if (match.Release == null) return match;

        foreach (var build in match.Release.Builds)
        {
            int comparison = build.Version.CompareTo(version);

            if (comparison == 0)
            {
                // Builds are descending and a build number can appear twice (two hotfix articles, one build);
                // the first is the one the page leads with.
                if (match.Exact == null) match.Exact = build;
                continue;
            }

            if (comparison > 0)
            {
                match.NewerBuilds++;
                if (build.Kind == SqlBuildKind.CumulativeUpdate) match.NewerCumulativeUpdates++;
                continue;
            }

            // Descending order: the first build below this version is the closest one below it.
            if (match.ClosestBelow == null) match.ClosestBelow = build;
        }

        match.NewerThanCatalog = match.Exact == null && match.NewerBuilds == 0 && match.Release.Builds.Count > 0;

        ApplySupportPhase(match, asOf);
        return match;
    }

    private static void ApplySupportPhase(SqlBuildMatch match, DateTime asOf)
    {
        var release = match.Release;
        DateTime today = asOf.Date;

        if (release.MainstreamSupportEnd != null && today <= release.MainstreamSupportEnd.Value)
        {
            match.Phase = SqlSupportPhase.Mainstream;
            match.DaysUntilSupportEnds = (int)(release.MainstreamSupportEnd.Value - today).TotalDays;
            return;
        }

        if (release.ExtendedSupportEnd != null)
        {
            match.Phase = today <= release.ExtendedSupportEnd.Value ? SqlSupportPhase.Extended : SqlSupportPhase.OutOfSupport;
            match.DaysUntilSupportEnds = (int)(release.ExtendedSupportEnd.Value - today).TotalDays;
            return;
        }

        // Mainstream has ended and no extended date is listed. Saying "out of support" is the safe direction to
        // be wrong in — the alternative is a silent "Unknown" on a release nobody is patching.
        match.Phase = release.MainstreamSupportEnd != null ? SqlSupportPhase.OutOfSupport : SqlSupportPhase.Unknown;
    }

    // =====================================================================================================
    // Parsing the embedded snapshot
    // =====================================================================================================

    private static Dictionary<string, SqlServerRelease> Parse()
    {
        var releases = new Dictionary<string, SqlServerRelease>(StringComparer.Ordinal);
        SqlServerRelease current = null;

        foreach (var line in SqlBuildData.Catalog.Split('\n'))
        {
            if (line.Length == 0) continue;

            var f = line.Split('\t');

            if (f[0] == "R" && f.Length >= 8)
            {
                current = new SqlServerRelease
                {
                    Key = f[1],
                    Name = f[2],
                    Codename = Empty(f[3]),
                    RtmBuild = Empty(f[4]),
                    Released = Date(f[5]),
                    MainstreamSupportEnd = Date(f[6]),
                    ExtendedSupportEnd = Date(f[7])
                };
                releases[current.Key] = current;
                continue;
            }

            if (f[0] != "B" || f.Length < 8 || current == null) continue;

            SqlVersion version;
            if (!SqlVersion.TryParse(f[1], out version)) continue;

            var build = new SqlServerBuild
            {
                Build = f[1],
                Version = version,
                Label = Empty(f[2]),
                Kind = Kind(f[3]),
                KbNumber = Empty(f[4]),
                Released = Date(f[5]),
                Withdrawn = f[6] == "1",
                Description = Empty(f[7]),
                Release = current
            };

            current.Builds.Add(build);

            if (current.LatestBuild == null || build.Version > current.LatestBuild.Version)
                current.LatestBuild = build;

            if (build.Kind == SqlBuildKind.CumulativeUpdate && !build.Withdrawn
                && (current.LatestCumulativeUpdate == null || build.Version > current.LatestCumulativeUpdate.Version))
                current.LatestCumulativeUpdate = build;
        }

        // The generator emits them newest-first, but Lookup's "closest build below" walk depends on the order,
        // so it is established here rather than assumed of the data.
        foreach (var release in releases.Values)
            release.Builds.Sort((a, b) => b.Version.CompareTo(a.Version));

        return releases;
    }

    private static string Empty(string value) => value.Length == 0 ? null : value;

    private static DateTime? Date(string value)
    {
        DateTime parsed;
        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            ? parsed
            : (DateTime?)null;
    }

    private static SqlBuildKind Kind(string value)
    {
        switch (value)
        {
            case "Preview": return SqlBuildKind.Preview;
            case "Rtm": return SqlBuildKind.Rtm;
            case "ServicePack": return SqlBuildKind.ServicePack;
            case "CumulativeUpdate": return SqlBuildKind.CumulativeUpdate;
            case "SecurityUpdate": return SqlBuildKind.SecurityUpdate;
            case "FeaturePack": return SqlBuildKind.FeaturePack;
            case "Hotfix": return SqlBuildKind.Hotfix;
            default: return SqlBuildKind.Unknown;
        }
    }
}
