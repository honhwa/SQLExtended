"""
Regenerates SQLExtended/Monitoring/Performance/SqlBuildData.cs from sqlserverbuilds.blogspot.com.

Usage:
    python generate-sql-build-catalog.py                # downloads the page
    python generate-sql-build-catalog.py builds.html    # parses a saved copy

The site is one static HTML page: a "Quick summary" table carrying each release's lifecycle dates and RTM
build, then one detail table per release (`<h2 id=sqlNNNN>`) listing every known build newest-first. Both are
plain <tr>/<td> with no classes worth depending on beyond `class=cu` (marks a cumulative-update row), so the
parsing below is positional on the detail tables' seven columns:

    Build | Alternative builds | File version | Q | KB | KB / Description | Release Date
"""

import html
import io
import os
import re
import sys
import urllib.request

URL = "https://sqlserverbuilds.blogspot.com/"

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                   "SQLExtended", "Monitoring", "Performance", "SqlBuildData.cs")


# ---------------------------------------------------------------------------------------------------------
# HTML helpers
# ---------------------------------------------------------------------------------------------------------

# The "Latest CU" / "Latest SP" / "RTM" chips and the red "*new" marker are page chrome that reads as part of
# the description once the tags are gone — "Microsoft SQL Server 2025 RTM" became "…RTM RTM" until these went.
BADGE = re.compile(r"<span class=l(?:cu|sp|rtm)\b[^>]*>.*?</span>|<span style=\"font-size:x-small;color:red\">[^<]*</span>", re.S)


def text_of(fragment):
    """Visible text of an HTML fragment, with the chrome and tags stripped and entities decoded."""
    s = BADGE.sub(" ", fragment)
    s = re.sub(r"<br\s*/?>", " ", s)
    s = re.sub(r"<[^>]+>", "", s)
    # &nbsp; is used as a word separator in places (the 2008 R2 release name), so it has to become a space.
    s = html.unescape(s).replace(u"\xa0", " ")
    return re.sub(r"\s+", " ", s).strip()


def cells(row_html):
    """
    Splits a <tr> body into cells. The page omits nearly every closing </td>, so splitting on the opening
    tag is the only reliable cut — and it is why the columns are addressed by position.
    """
    parts = re.split(r"<t[dh]\b", row_html)[1:]
    out = []
    for p in parts:
        p = p.split(">", 1)[1] if ">" in p else p
        p = re.sub(r"</t[dh]>\s*$", "", p.strip())
        out.append(p)
    return out


def first_time(fragment):
    """The ISO date out of the first <time datetime="..."> in a fragment."""
    m = re.search(r'<time[^>]*datetime="(\d{4}-\d\d-\d\d)"', fragment)
    return m.group(1) if m else ""


# ---------------------------------------------------------------------------------------------------------
# Version numbers
# ---------------------------------------------------------------------------------------------------------

def parse_version(s):
    """'16.0.4265.3' -> (16, 0, 4265, 3), padded to four parts. None if it is not a build number."""
    s = s.strip()
    if not re.fullmatch(r"\d+(\.\d+){1,3}", s):
        return None
    parts = [int(p) for p in s.split(".")]
    while len(parts) < 4:
        parts.append(0)
    return tuple(parts)


def release_key(version):
    """
    The (major, minor) pair identifying a release. Minor matters: 10.50 is 2008 R2 and 10.0 is 2008, and
    they are different products with different support dates.
    """
    return "%d.%d" % (version[0], version[1])


# ---------------------------------------------------------------------------------------------------------
# The quick-summary table: lifecycle dates per release
# ---------------------------------------------------------------------------------------------------------

def parse_releases(page):
    """
    Reads the release name, codename, release date and the two support end dates out of the summary table's
    first column, plus the RTM build from its second.
    """
    start = page.index('<h2 id="quick-summary">')
    end = page.index("<h2 id=sql", start)
    table = page[start:end]

    releases = {}
    for row_html in re.findall(r"<tr>(.*?)</tr>", table, re.S):
        c = cells(row_html)
        if len(c) < 2:
            continue

        # The release name is the first anchor with any text — the one before it wraps the down-arrow image.
        # Matched on the extracted text, not the markup: "SQL&nbsp;Server&nbsp;2008&nbsp;R2" is spelled with
        # non-breaking spaces and a markup-level "SQL Server" pattern silently dropped that whole release.
        name = ""
        for anchor in re.findall(r"<a\b[^>]*>(.*?)</a>", c[0], re.S):
            candidate = text_of(anchor)
            if candidate.startswith("SQL Server"):
                name = candidate
                break
        if not name:
            continue

        rtm = parse_version(text_of(c[1]).split()[0]) if text_of(c[1]) else None
        if rtm is None:
            continue

        def dated(title):
            m = re.search(r'title="%s"[^>]*>[^<]*<time[^>]*datetime="(\d{4}-\d\d-\d\d)"' % title, c[0])
            return m.group(1) if m else ""

        codename = re.search(r'class=codename>codename ([^<]+)<', c[0])

        releases[release_key(rtm)] = {
            "key": release_key(rtm),
            "name": name,
            "codename": codename.group(1).strip() if codename else "",
            "released": dated("Release date"),
            "mainstream_end": dated("Mainstream Support End Date"),
            "extended_end": dated("Extended Support End Date"),
            "rtm": ".".join(str(p) for p in rtm[:3]) if rtm[3] == 0 and rtm[2] else text_of(c[1]).split()[0],
        }
    return releases


# ---------------------------------------------------------------------------------------------------------
# The detail tables: every known build
# ---------------------------------------------------------------------------------------------------------

# Ordered most specific first: the first rule that matches names the build. Pre-release builds come first —
# they are the one label worth getting right whatever else the description says, because a production instance
# running a CTP is a finding rather than a patch level.
LABEL_RULES = [
    (r"Release Candidate (?:Refresh )?(\d+)(?:\.\d+)?", lambda m: "RC%s" % m.group(1)),
    (r"Release Candidate", lambda m: "RC"),
    (r"\(CTP ?([\d.]+)\)", lambda m: "CTP %s" % m.group(1)),
    (r"Community Technology Preview|Public Preview|\bCTP\b", lambda m: "CTP"),
    # "Cumulative update 17 (CU17) for SQL Server 2016 Service Pack 2"
    (r"Cumulative update (?:package )?(\d+) \(CU\d+\)[^,]*?for SQL Server [\w. ]*?Service Pack (\d+)",
     lambda m: "SP%s CU%s" % (m.group(2), m.group(1))),
    (r"Cumulative update (?:package )?(\d+)(?: \(CU\d+\))?", lambda m: "CU%s" % m.group(1)),
    # "Security update for SQL Server 2025 CU6: July 14, 2026", and the wordier
    # "Security update for the Remote Code Execution vulnerability in SQL Server 2016 SP2 CU: August 2019".
    (r"[Ss]ecurity update.*?\bSP(\d+) CU(\d+)", lambda m: "SP%s CU%s + security update" % (m.group(1), m.group(2))),
    (r"[Ss]ecurity update.*?\bSP(\d+) CU\b", lambda m: "SP%s CU + security update" % m.group(1)),
    (r"[Ss]ecurity update.*?\bCU(\d+)", lambda m: "CU%s + security update" % m.group(1)),
    (r"[Ss]ecurity update.*?\bSP(\d+)", lambda m: "SP%s + security update" % m.group(1)),
    (r"[Ss]ecurity update.*?\b(?:RTM|GDR)\b", lambda m: "RTM + security update"),
    (r"[Ss]ecurity update.*?\bCU\b", lambda m: "CU + security update"),
    (r"On-demand hotfix[^.]*?Service Pack (\d+)", lambda m: "SP%s hotfix" % m.group(1)),
    (r"On-demand hotfix[^.]*?CU(\d+)", lambda m: "CU%s hotfix" % m.group(1)),
    (r"[Aa]zure Connect [Ff]eature [Pp]ack[^.]*?Service Pack (\d+)", lambda m: "SP%s Azure Connect FP" % m.group(1)),
    (r"[Aa]zure Connect [Ff]eature [Pp]ack", lambda m: "Azure Connect FP"),
    (r"Service Pack (\d+) \(SP\d+\)", lambda m: "SP%s" % m.group(1)),
    (r"\bSQL Server [\w. ]*?Service Pack (\d+)\b", lambda m: "SP%s" % m.group(1)),
    (r"\bRTM\b", lambda m: "RTM"),
    (r"\bGDR\b", lambda m: "GDR"),
    (r"^FIX:|[Hh]otfix", lambda m: "Hotfix"),
    (r"An unknown but existing build", lambda m: ""),
]

# Order matters: nearly every description also names the Service Pack it applies to, so "Service Pack" has to
# be the last thing tried or an on-demand hotfix and a feature pack both classify as a service pack. Beta is
# first for the same reason the label rules put it there.
KIND_RULES = [
    ("Beta", "Preview"),
    ("Community Technology Preview", "Preview"),
    ("Release Candidate", "Preview"),
    ("Cumulative update", "CumulativeUpdate"),
    ("security update", "SecurityUpdate"),
    ("Security update", "SecurityUpdate"),
    ("feature pack", "FeaturePack"),
    ("Feature Pack", "FeaturePack"),
    ("hotfix", "Hotfix"),
    ("Hotfix", "Hotfix"),
    ("FIX:", "Hotfix"),
    ("GDR", "SecurityUpdate"),
    ("RTM", "Rtm"),
    ("Service Pack", "ServicePack"),
]


def label_for(description, is_cu_row):
    for pattern, build in LABEL_RULES:
        m = re.search(pattern, description)
        if m:
            return build(m)
    return "CU" if is_cu_row else ""


def kind_for(description, is_cu_row):
    if is_cu_row and "Cumulative update" in description:
        return "CumulativeUpdate"
    for needle, kind in KIND_RULES:
        if needle in description:
            return kind
    return "Unknown"


def parse_builds(page, releases, dropped):
    """One record per build row of every detail table, keyed to its release by the build's (major, minor)."""
    sections = list(re.finditer(r"<h2 id=(sql[\w]+)>([^<]+)</h2>", page))
    builds = []

    for i, section in enumerate(sections):
        start = section.end()
        end = sections[i + 1].start() if i + 1 < len(sections) else len(page)
        table = page[start:end]

        for row_html in re.findall(r"<tr>(.*?)</tr>", table, re.S):
            c = cells(row_html)
            if len(c) < 7 or "<th" in row_html[:12]:
                continue

            version = parse_version(text_of(c[0]))
            if version is None:
                continue

            key = release_key(version)
            if key not in releases:
                # Reported rather than skipped quietly: a release the summary table stopped naming would
                # otherwise drop its entire build history without a word. This is how 2008 R2 went missing.
                dropped.append((section.group(2), text_of(c[0])))
                continue

            description = text_of(c[5])

            # A withdrawn build wears the CVE chip too ("<span class=cve>Withdrawn</span>"), which is worth
            # surfacing on its own — running one is a real finding, not a footnote.
            chips = re.findall(r"<span class=cve>([^<]+)</span>", c[5])
            withdrawn = any(chip.strip() == "Withdrawn" for chip in chips)
            cves = [chip for chip in chips if chip.strip() != "Withdrawn"]
            for chip in chips:
                description = description.replace(chip, "")
            description = re.sub(r"\s+", " ", description).strip()

            # The description opens with the KB number, which the KB column already carries.
            kb = ""
            m = re.match(r"^(\d{6,7})\s+", description)
            if m:
                kb = m.group(1)
                description = description[m.end():]
            else:
                m = re.search(r"KB(\d{6,7})", text_of(c[4]))
                if m:
                    kb = m.group(1)

            is_cu_row = re.search(r"<td class=cu\b", row_html) is not None

            builds.append({
                "release": key,
                "version": version,
                "build": text_of(c[0]),
                "file_version": text_of(c[2]),
                "label": label_for(description, is_cu_row),
                "kind": kind_for(description, is_cu_row),
                "kb": kb,
                "released": first_time(c[6]),
                "withdrawn": withdrawn,
                "cves": cves,
                "description": description,
            })

    return builds


# ---------------------------------------------------------------------------------------------------------
# Emit
# ---------------------------------------------------------------------------------------------------------

def escape(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


def emit(releases, builds, snapshot_date, out_path):
    by_release = {}
    for b in builds:
        by_release.setdefault(b["release"], []).append(b)

    for rows in by_release.values():
        rows.sort(key=lambda b: b["version"], reverse=True)

    order = sorted(releases.values(), key=lambda r: parse_version(r["rtm"]), reverse=True)

    lines = []
    for release in order:
        rows = by_release.get(release["key"], [])
        lines.append("R\t%s\t%s\t%s\t%s\t%s\t%s\t%s" % (
            release["key"], release["name"], release["codename"], release["rtm"],
            release["released"], release["mainstream_end"], release["extended_end"]))
        for b in rows:
            lines.append("B\t%s\t%s\t%s\t%s\t%s\t%s\t%s" % (
                b["build"], b["label"], b["kind"], b["kb"], b["released"],
                "1" if b["withdrawn"] else "", b["description"]))

    body = "\n".join('        "%s\\n" +' % escape(line) for line in lines)
    body = body.rstrip(" +")

    text = HEADER % {
        "snapshot": snapshot_date,
        "releases": len(order),
        "builds": len(builds),
        "body": body,
    }

    with io.open(out_path, "w", encoding="utf-8-sig", newline="\r\n") as f:
        f.write(text)

    print("wrote %s: %d releases, %d builds, %.0f KB" % (out_path, len(order), len(builds), len(text) / 1024.0))


HEADER = u'''namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// A snapshot of the SQL Server build list from <c>sqlserverbuilds.blogspot.com</c>, taken %(snapshot)s:
/// %(releases)s releases and %(builds)s builds.
///
/// <para><b>Generated — do not hand-edit.</b> Re-run
/// <c>SoluitionDocs/Tools/generate-sql-build-catalog.py</c> against the page to refresh it, which is also the
/// only thing that makes the data newer. The tab reads <see cref="SnapshotDate"/> and says so on screen, so a
/// stale answer is never presented as a current one.</para>
///
/// <para>One record per line so the whole thing is one string literal rather than %(builds)s object
/// initialisers — the C# compiler is markedly slower over the latter and the diff of a refresh is unreadable.
/// <c>R</c> starts a release and every <c>B</c> after it belongs to that release:</para>
///
/// <code>
/// R  key  name  codename  rtmBuild  released  mainstreamEnd  extendedEnd
/// B  build  label  kind  kb  released  withdrawn  description
/// </code>
///
/// <para>Parsed by <see cref="SqlBuildCatalog"/>. Tab-separated, and no field may contain a tab.</para>
/// </summary>
internal static class SqlBuildData
{
    /// <summary>When the page this was generated from was last modified.</summary>
    public const string SnapshotDate = "%(snapshot)s";

    /// <summary>Where it came from, shown on the tab so the numbers can be checked against the source.</summary>
    public const string SourceUrl = "https://sqlserverbuilds.blogspot.com/";

    public const string Catalog =
%(body)s;
}
'''


def main():
    if len(sys.argv) > 1:
        page = io.open(sys.argv[1], encoding="utf-8").read()
    else:
        request = urllib.request.Request(URL, headers={"User-Agent": "Mozilla/5.0"})
        page = urllib.request.urlopen(request).read().decode("utf-8")

    m = re.search(r'article:modified_time" content="(\d{4}-\d\d-\d\d)"', page)
    snapshot = m.group(1) if m else "unknown"

    releases = parse_releases(page)
    dropped = []
    builds = parse_builds(page, releases, dropped)

    if dropped:
        print("WARNING: %d build rows had no release in the summary table and were dropped:" % len(dropped))
        for section, build in dropped[:20]:
            print("    %s: %s" % (section, build))

    out = sys.argv[2] if len(sys.argv) > 2 else OUT
    emit(releases, builds, snapshot, os.path.abspath(out))


if __name__ == "__main__":
    main()
