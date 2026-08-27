# SQLExtended — Releasing

The `.vsix` is built locally and published as a GitHub Release. The in-IDE update check polls a
`version.json` published as a release asset.

```
┌──────────────────────────────┐        ┌──────────────────────────────────────┐
│ your machine                 │        │ GitHub Release  v<version>           │
│ - dotnet build (needs SSMS)  │  gh    │   SQLExtended-<version>.vsix         │
│ - stamp version              ├───────▶│   version.json                       │
│ - write version.json         │        │                                      │
└──────────────────────────────┘        └──────────────────┬───────────────────┘
                                                           │ anonymous HTTPS GET
                                                           ▼
                                        ┌──────────────────────────────────────┐
                                        │ SSMS extension (on each PC)          │
                                        │ - polls version.json on startup      │
                                        │ - shows InfoBar if newer             │
                                        │ - user clicks Download → .vsix       │
                                        └──────────────────────────────────────┘
```

One command does all of it:

```powershell
.\SoluitionDocs\Tools\publish-release.ps1
```

The rest of this document is why it is shaped that way, and what to check when it goes wrong.

---

## 1. Why the build is local

`SQLExtended.csproj` references three SSMS 22 internal assemblies by path out of the install folder
(`$(SsmsInstallDir)`, hardcoded near the top of the project file): `SqlWorkbench.Interfaces.dll`,
`Extensions\Application\SQLEditors.dll` and `Microsoft.SqlServer.GridControl.dll`. More are loaded by
reflection at runtime (`SqlMgmt.dll`, `SQLManagerUI.dll`, the brokered-contracts assembly), but those need
no build-time reference. The three that do are not on NuGet, and a GitHub-hosted runner has no SSMS, so
**CI cannot build the extension.** `.github/workflows/ci.yml` builds and runs the test project only, which is possible
because it links the platform-free sources directly.

If you ever want the container built in CI, the options are, in the order I'd trust them:

1. **Self-hosted runner** with SSMS 22 installed.
2. **Commit the three reference assemblies** and override `SsmsInstallDir` to point at them. Fast and
   reproducible, but it redistributes Microsoft binaries — not acceptable in a public repo.
3. **Install SSMS 22 in the workflow.** Multi-gigabyte download, several minutes per run, and it breaks
   whenever the download URL moves.

Until one of those is worth doing, the local build is not a workaround — it is the release process.

---

## 2. The version, and why it has one source

`version.txt` at the repo root is the only place the version is written. It reaches three places that
have to agree:

| Consumer | Reads | Why it matters |
|---|---|---|
| `UpdateCheckService.GetCurrentVersion()` | the assembly's `FileVersion` | decides whether the feed is offering something newer |
| Manage Extensions, VSIXInstaller | the `.vsix` manifest's `Identity/@Version` | what the user sees, and what an upgrade compares |
| the update feed | `version.json`'s `version` | what is compared against the assembly |

**They did not agree before.** Nothing stamped the assembly, so it reported `1.0.0.0` while the manifest
said `1.0.19`. Any published version would have read as newer than every installed build: the InfoBar
would appear, the user would install, and it would appear again on the next check, forever, with no way to
make it stop except turning the update check off.

So `SQLExtended.csproj` reads `version.txt` into `$(Version)` (overridable with `-p:Version=`), and the
`StampVsixVersion` target writes it into the **merged** manifest the SDK generates — not into the
committed `source.extension.vsixmanifest`, so a release build leaves the working tree clean and the
committed manifest never needs hand-bumping. `XmlPoke` reports nothing when its XPath matches nothing, so
the target reads the value back and fails the build if the stamp did not take.

The publish script then verifies the built `.vsix` and the built assembly both report the version it
intended, and refuses to publish if they disagree. That check is the point of the whole arrangement.

### Version format

`yyyy.M.d.HHmm`, e.g. `2026.8.27.1430`. Monotonic, and every component stays inside `System.Version`'s
65535 ceiling.

**Never write a leading zero.** `2026.8.27.0834` is stored verbatim in the `.vsix` manifest but normalised
to `834` by `System.Version` in the assembly, so one release ships as two different versions. The script
generates the component as an integer and rejects any `-Version` that is not already in normal form.

---

## 3. Why the feed is `releases/latest/download`, not the GitHub API

The default in `SQLExtendedSettings.UpdateFeedUrl` is:

```
https://github.com/JamTheRadar/SQLExtended/releases/latest/download/version.json
```

That path redirects to the newest **published, non-prerelease** release's asset of that name. Which means:

- **No API call**, so no rate limit. `api.github.com/repos/.../releases/latest` allows 60 requests an hour
  per IP unauthenticated — shared by everyone behind one corporate NAT.
- The download CDN is less likely to be blocked by a corporate proxy than `api.github.com`.
- **The manifest stays ours.** `minRequiredVersion` has no equivalent field in a GitHub release, and it is
  the only way to publish a fix nobody can skip.

Drafts and prereleases are invisible to that URL, which is what makes `-Draft` a safe dry run.

`version.json` is uploaded under a fixed name so the URL always resolves. The `.vsix` asset is versioned,
and `version.json`'s `url` names that specific release's asset rather than the `latest/download` alias, so
an installed build is always offered the exact container the feed described.

**version.json must not have a BOM.** `Json.NET` reads a leading U+FEFF as an unexpected character and
throws; the only report of that is a `Debug.WriteLine` which is not compiled into a Release build, so the
update check would just silently never find anything again. The script writes UTF-8 without a BOM
(`Set-Content -Encoding utf8` on Windows PowerShell 5.1 writes one), and `FetchManifestAsync` strips a
leading BOM defensively in case a future publisher gets it wrong.

---

## 4. Publishing

```powershell
# Build and publish with a date-derived version
.\SoluitionDocs\Tools\publish-release.ps1

# Everything except touching GitHub — build, verify, stage artifacts\ for inspection
.\SoluitionDocs\Tools\publish-release.ps1 -NoPublish

# A draft nobody is offered, to check the release page itself
.\SoluitionDocs\Tools\publish-release.ps1 -Draft

# Explicit version and notes
.\SoluitionDocs\Tools\publish-release.ps1 -Version 2026.9.1.900 -NotesFile .\notes.md

# A fix nobody may skip
.\SoluitionDocs\Tools\publish-release.ps1 -MinRequiredVersion 2026.9.1.900
```

Needs the GitHub CLI (`gh auth login`) and, for the build, SSMS 22 installed. Commit the `version.txt`
bump afterwards so the tag and the tree agree.

Release notes default to `Build <version>`. **Nothing is extracted from `release-notes.md`
automatically** — each entry in that file is one long unstructured block, so "the first paragraph" of it is
the entire changelog, which is what the InfoBar's Release notes view would then show. Pass `-Notes` or
`-NotesFile` when the notes matter.

### Verifying a release

```powershell
# The feed the extensions poll — should return the version you just published
Invoke-RestMethod 'https://github.com/JamTheRadar/SQLExtended/releases/latest/download/version.json'

# The container that manifest points at — should be 200, don't download it
(Invoke-WebRequest -Method Head (Invoke-RestMethod 'https://github.com/JamTheRadar/SQLExtended/releases/latest/download/version.json').url).StatusCode
```

---

## 5. What users do

### First install

Send them the release page, or the asset URL directly. Close SSMS, then double-click the `.vsix` (or
`VSIXInstaller.exe SQLExtended-<version>.vsix`). It installs per-user, so no admin rights are needed, and
it offers SSMS 22 as the target because the manifest declares `Microsoft.VisualStudio.Ssms`. Uninstall is
the same route in reverse, from **SSMS → Extensions → Manage Extensions**.

If the **SQLExtended** menu doesn't appear, clear the MEF cache (`clearSSMScache.ps1` in the repo root) and
relaunch. The `.vsix` has no post-install step to do that, which the old Inno installer did.

For dropping files onto a test machine instead, see `ManualInstall.md`.

### Updates

On startup the extension polls `version.json`, at most once per 20h per machine, bypassable from
**SQLExtended → Check for Updates…**. If the feed offers something newer, an InfoBar appears at the top of
the IDE with **Download and install · Release notes · Skip this version**, and it says to close SSMS first
— **nothing closes SSMS for them.** That is the real limit of this mechanism: an extension cannot replace
its own loaded assembly, so there is no silent auto-update available at any price.

`minRequiredVersion` removes the Skip button for anyone below it.

### A note on true in-IDE updating

If the browser round trip is the objectionable part, the next step up is a **private gallery**: SSMS reads
Atom/SimpleFeed XML from `Tools → Options → Environment → Extensions → Additional Extension Galleries`, and
an extension in a registered gallery appears under **Manage Extensions → Updates** with an Update button.
Install still happens on restart via VSIXInstaller, but the user never leaves SSMS. GitHub Pages can host
that feed statically. Not implemented; it is independent of everything above.

---

## 6. Troubleshooting

**`StampVsixVersion failed: the .vsix manifest reports '…' but the build is version '…'.**
The `Identity/@Version` XPath no longer matches the merged manifest — most likely the manifest's XML
namespace changed with a VSSDK update. Fix the XPath in the target; do not remove the check.

**The publish script refuses: "The assembly says X, not Y".**
The build didn't pick up `-p:Version=`. Check that nothing else in the project sets `Version`,
`AssemblyVersion` or `FileVersion`, and that no `AssemblyInfo.cs` has reappeared with those attributes.

**Users see the InfoBar but Download 404s.**
`version.json` uploaded but the `.vsix` didn't, or the asset was deleted from the release. `gh` uploads
both in one call, so this means someone edited the release afterwards.

**The InfoBar never appears even though a newer release exists.**
- The release is a draft or prerelease — `releases/latest/download` does not resolve to either.
- `UpdateCheckEnabled: false` or an empty `UpdateFeedUrl` in
  `%APPDATA%\SQLExtended\SSMS\sqlextended-settings.json`.
- They clicked **Skip this version** — clear it via SQLExtended Settings → Updates → **Clear Skipped
  Version**.
- The 20h cooldown hasn't elapsed — **SQLExtended → Check for Updates…** bypasses it.
- The machine is offline or behind a proxy that blocks `github.com`; the check times out after 10s.
- `version.json` has a BOM (see above). Turn on **SQLExtended Settings → Diagnostics** and check for an
  `UpdateCheckService` entry — the failure is logged there and nowhere else.

**Users get an "unsigned extension" prompt.**
Expected — the container isn't code-signed. For internal distribution that's fine. To remove it, buy a
code-signing certificate and sign the `.vsix` as a post-build step.

**VSIXInstaller: "not installable on any currently installed products".**
The manifest targets `Microsoft.VisualStudio.Ssms` `[22.0,)` — that machine has no SSMS 22.

---

## Appendix: hosting the feed somewhere else

**Nothing in the extension is tied to GitHub.** `UpdateFeedUrl` is just a URL to a JSON document, so any
host that serves it anonymously over HTTPS will do — an Azure Blob container with public blob access, S3,
a plain web server, GitHub Pages. Only three things have to hold:

- The URL must resolve to the *newest* manifest without an API call. On GitHub that is what
  `releases/latest/download/<name>` gives; elsewhere it means overwriting one fixed path on each release.
- **Upload the `.vsix` before the manifest**, always. The other order leaves a window where the feed
  advertises a version whose download 404s.
- No BOM on the JSON (see above).

An earlier version of this project published to an Azure Blob container from a shared Azure DevOps
pipeline. Those instructions are no longer kept here.
