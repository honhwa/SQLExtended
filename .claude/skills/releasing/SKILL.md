---
name: releasing
description: Build and publish a SQLExtended .vsix release — version.txt stamping, the version.json update feed, and the guards that keep the assembly, the VSIX manifest and the feed agreeing. Use when cutting, publishing, or debugging a release, or changing publish-release.ps1, version.txt or the update check.
---

# Releasing

The `.vsix` is the only distribution — there is no installer. `SoluitionDocs/Tools/publish-release.ps1`
builds it and publishes a GitHub Release with the `version.json` the in-IDE update check polls;
`SoluitionDocs/Deployment.md` is the long form. `-NoPublish` does everything except touch GitHub.

**The build stays local, and that is not a stopgap.** `SQLExtended.csproj` references three SSMS 22
internal assemblies out of the install folder (`SqlWorkbench.Interfaces.dll`, `SQLEditors.dll`,
`Microsoft.SqlServer.GridControl.dll`); a hosted runner has no SSMS. `.github/workflows/ci.yml` therefore
builds and tests `SQLExtended.Tests` only, which works because that project links the platform-free sources
directly. Don't "fix" CI by dropping the reference paths.

**`version.txt` at the repo root is the only place the version is written**, and the reason it exists is
that three consumers have to agree and did not: the assembly's `FileVersion` (what
`UpdateCheckService.GetCurrentVersion` compares the feed against), the `.vsix` manifest's
`Identity/@Version` (what Manage Extensions shows and what an upgrade compares), and `version.json`. The
assembly reported `1.0.0.0` while the manifest said `1.0.19`, so **any** published release would have read
as newer than every installed build — InfoBar, install, InfoBar again, forever, with no way to stop it but
disabling the check. Three things hold it together, and each fails silently without its guard:

- The `StampVsixVersion` target writes `$(Version)` into the **merged** manifest
  (`$(IntermediateMergedVsixManifest)`, between `MergeVsixManifestFile` and `DetokenizeVsixManifestFile`),
  never the committed `source.extension.vsixmanifest` — so a release build leaves the tree clean and the
  committed manifest is never hand-bumped. `XmlPoke` writes nothing and *says* nothing when its XPath
  matches nothing, which would ship the committed manifest's stale version, so the target reads the value
  back and errors.
- The publish script re-verifies both the built container and the built assembly before uploading. That
  check is the point of the arrangement, not a formality.
- **No leading zero in the version.** `yyyy.M.d.HHmm` before 10:00 gives `0834`, which the manifest stores
  verbatim while `System.Version` normalises the assembly's to `834` — one release, two versions. The
  script casts the component to `[int]` and rejects any `-Version` not already in normal form. This is how
  the check above earned its place: it caught exactly this on the first dry run.

**The feed is `releases/latest/download/version.json`, deliberately not the GitHub API.** That path
redirects to the newest published non-prerelease release's asset of that name, so there is no API call and
no 60-per-hour-per-IP rate limit (one corporate NAT shares it), the download host is less often
proxy-blocked than `api.github.com`, and the manifest stays ours — `minRequiredVersion` has no equivalent
in a GitHub release. Drafts and prereleases are invisible to that URL, which is what makes `-Draft` a safe
dry run.

**`version.json` must not carry a BOM.** Json.NET reads a leading U+FEFF as an unexpected character and
throws, and the only report was a `Debug.WriteLine` that isn't compiled into Release — so the update check
would silently never find anything again. The script writes UTF-8 without one (`Set-Content -Encoding utf8`
on PowerShell 5.1 writes one), `FetchManifestAsync` strips it defensively, and that method's failures now
also go to `SQLExtendedLog` for the reason the Diagnostics section gives.

**No extension can silently update itself** — it cannot replace its own loaded assembly, and VSIXInstaller
refuses to touch an extension while the IDE is running. So the InfoBar says to close SSMS first (the Inno
installer used to do that via `CloseApplications`; nothing does now). The one step up is a private gallery
Atom feed under `Tools → Options → Environment → Extensions`, which puts the update in SSMS's own Manage
Extensions → Updates; we don't host such a feed, but www.vsixgallery.com's `/feed/` is one (see below).

**The gallery upload is step 6, and it is not the feed.** The script POSTs the same `.vsix` to
`https://www.vsixgallery.com/api/upload` after the GitHub release succeeds; the extension's update check
still reads `version.json` from GitHub and knows nothing about the gallery. Three things about it are
load-bearing and all three are why it sits last: uploads are authenticated by an `X-Manage-Token` **we
choose**, and an untokened *first* upload makes the gallery mint one and show it once — so the step skips
itself when neither `-GalleryToken` nor `$env:VSIXGALLERY_TOKEN` is set rather than uploading without one
and losing the listing; a gallery upload is public the instant it lands, so `-Draft` skips it too, or a
draft release would advertise the version it is deliberately hiding; and a failed upload is a warning, not
a `Fail`, because the GitHub release is already published by then and must not read as a failed publish.
`SoluitionDocs/Deployment.md` §7 has the rest, including the README badge that is deliberately not in
`README.md` until the first upload exists.

Release notes default to `Build <version>`. **Nothing is auto-extracted from `release-notes.md`** — each
entry there is one unstructured block, so "the first paragraph" is the whole changelog, which is what the
InfoBar's Release notes view would then display.

**There is no `azure-pipelines.yml` in this repo.** An earlier version of this project published the feed
to an Azure Blob container from a shared Azure DevOps pipeline that lives elsewhere; those instructions
were removed when releases moved to GitHub, rather than kept as a second half-true release process naming
internal infrastructure in a public repo. `SoluitionDocs/Deployment.md` closes with what any replacement
host has to guarantee.
