<#
.SYNOPSIS
  Builds the .vsix locally and publishes it as a GitHub Release, with the version.json the in-IDE
  update check polls.

.DESCRIPTION
  The .vsix cannot be built on a GitHub-hosted runner: SQLExtended.csproj references four SSMS 22
  internal assemblies by path out of the install folder, and hosted runners have no SSMS. So the build
  stays local and only publishing is automated. Run this from the repo root.

  What it does, in order:
    1. Resolves the version (a -Version argument, else yyyy.M.d.HHmm) and writes it to version.txt.
    2. Builds Release with -p:Version=, which stamps the assembly and the .vsix manifest from that one
       value (see the StampVsixVersion target in SQLExtended.csproj).
    3. Verifies the built .vsix manifest and the built assembly both report that version, and refuses to
       publish if they disagree. This is the check that matters: the update InfoBar compares the feed's
       version against the *assembly's* FileVersion, so a .vsix whose manifest and assembly disagree
       installs cleanly and then nags forever, which is exactly the bug this release process replaced.
    4. Writes version.json.
    5. Creates the release with `gh` and uploads SQLExtended-<version>.vsix + version.json.
    6. Uploads the same .vsix to www.vsixgallery.com, if a manage token is available. Optional, non-fatal,
       and not what the extension's own update check reads — see .PARAMETER GalleryToken.

  version.json is uploaded under a fixed name so the feed URL
  (…/releases/latest/download/version.json) always resolves to the newest release's copy. The .vsix
  asset is versioned, and version.json's url names that specific release's asset rather than the
  latest/download alias, so an installed build is always offered the exact container the feed described.

.PARAMETER Version
  Four-part version to publish. Defaults to yyyy.M.d.HHmm (local time). Every component must be <= 65535
  — System.Version's limit, and Version.TryParse in UpdateCheckService is what reads it.

.PARAMETER Notes
  Release notes text. Defaults to "Build <version>".

.PARAMETER NotesFile
  Path to a file whose contents become the release notes. Nothing is extracted from release-notes.md
  automatically: that file is one long unstructured block per release, so "the first paragraph" of it is
  the entire changelog, which is what gets shown in the InfoBar's Release notes view.

.PARAMETER MinRequiredVersion
  Sets minRequiredVersion in version.json, which removes the InfoBar's "Skip this version" button for
  anyone below it. Use for a fix that must not be skippable.

.PARAMETER Draft
  Create the release as a draft. Nothing is offered to users until it is published, because
  releases/latest/download resolves only published, non-prerelease releases. Good for a dry run.

.PARAMETER NoPublish
  Do everything except create the release: resolve the version, build, verify, and write the assets into
  artifacts\. Nothing touches GitHub, so this needs no auth and no repo. Use it to check the build and the
  version.json you are about to ship.

.PARAMETER SkipBuild
  Publish the .vsix already in bin\Release\net48 instead of rebuilding. The version checks in step 3
  still run, so a stale container is rejected rather than published.

.PARAMETER GalleryToken
  The X-Manage-Token for www.vsixgallery.com. Defaults to $env:VSIXGALLERY_TOKEN; when neither is set the
  gallery step is skipped rather than uploaded untokened, because the gallery mints a token on an
  untokened first upload and shows it only in the response of that one request — lose it and the listing
  can't be managed again. The token is yours to choose: any string, sent with every upload.

.PARAMETER NoGallery
  Skip the gallery upload and publish the GitHub release only. Also implied by -Draft, since a gallery
  upload is public the moment it lands and a draft release deliberately is not.

.EXAMPLE
  .\SoluitionDocs\Tools\publish-release.ps1
  Build and publish with a date-derived version.

.EXAMPLE
  .\SoluitionDocs\Tools\publish-release.ps1 -Version 2026.9.1.0900 -Draft
  Dry run: builds, verifies, creates a draft release nobody is offered.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Notes,
    [string] $NotesFile,
    [string] $MinRequiredVersion,
    [switch] $Draft,
    [switch] $SkipBuild,
    [switch] $NoPublish,
    [string] $GalleryToken,
    [switch] $NoGallery
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo        = 'JamTheRadar/SQLExtended'
$RepoRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Project     = Join-Path $RepoRoot 'SQLExtended\SQLExtended.csproj'
$VersionFile = Join-Path $RepoRoot 'version.txt'
$OutDir      = Join-Path $RepoRoot 'SQLExtended\bin\Release\net48'
$BuiltVsix   = Join-Path $OutDir 'SQLExtended.vsix'
$BuiltDll    = Join-Path $OutDir 'SQLExtended.dll'
$StageDir    = Join-Path $RepoRoot 'artifacts'

function Step($message) { Write-Host "`n==> $message" -ForegroundColor Cyan }
function Fail($message) { throw $message }

# ---------------------------------------------------------------------------------------------------
# 0. Prerequisites
# ---------------------------------------------------------------------------------------------------
Step 'Checking prerequisites'

if (-not $NoPublish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Fail "The GitHub CLI (gh) is not on PATH. Install it from https://cli.github.com, then 'gh auth login'."
    }
    # `gh auth status` exits non-zero when not logged in. Check now rather than after a five-minute build.
    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { Fail "Not logged in to GitHub. Run 'gh auth login' first." }
}

if (-not (Test-Path $Project)) { Fail "Cannot find $Project. Run this from the repo, not a copy of the script." }

# ---------------------------------------------------------------------------------------------------
# 1. Version
# ---------------------------------------------------------------------------------------------------
Step 'Resolving version'

if (-not $Version) {
    $now = Get-Date
    # yyyy.M.d.HHmm — monotonic (HHmm read as an integer still increases through the day), and every
    # component stays inside System.Version's 65535 ceiling.
    #
    # The cast to [int] is load-bearing: before 10:00, HHmm is '0834', and a leading zero is where the
    # assembly and the manifest part company. The manifest keeps the string it was handed ('2026.8.27.0834')
    # while System.Version normalises the component to 834, so the built assembly reports 2026.8.27.834 —
    # two different versions for one release, which is the drift this whole mechanism exists to prevent.
    $Version = '{0}.{1}.{2}.{3}' -f $now.Year, $now.Month, $now.Day, [int] $now.ToString('HHmm')
}

$parsed = $null
if (-not [System.Version]::TryParse($Version, [ref] $parsed)) {
    Fail "'$Version' is not a valid version. UpdateCheckService parses this with Version.TryParse, which needs numeric components."
}
if ($parsed.Revision -lt 0) {
    Fail "'$Version' has fewer than four components. The extension compares four-part versions; a three-part version sorts unpredictably against them."
}
# Reject anything System.Version would renumber — a leading zero, mainly. The .vsix manifest stores the
# string verbatim while the assembly stores the parsed value, so '2026.8.27.0834' ships as two different
# versions and the verification step below would fail after a full build. Catch it in the first second.
if ($parsed.ToString() -ne $Version) {
    Fail "'$Version' is not in normal form — System.Version reads it as '$($parsed.ToString())'. Pass '$($parsed.ToString())' instead: the manifest would keep your spelling while the assembly took the normalised one."
}

if (-not $NoPublish) {
    $existing = & gh release view "v$Version" --repo $Repo --json tagName 2>$null
    if ($LASTEXITCODE -eq 0 -and $existing) {
        Fail "Release v$Version already exists in $Repo. Pass a different -Version; re-tagging a published release changes what existing installs were offered."
    }
}

Write-Host "  version: $Version"
Set-Content -Path $VersionFile -Value $Version -Encoding ascii -NoNewline
Add-Content -Path $VersionFile -Value "`n" -NoNewline

# ---------------------------------------------------------------------------------------------------
# 2. Build
# ---------------------------------------------------------------------------------------------------
if ($SkipBuild) {
    Step 'Skipping build (-SkipBuild)'
    if (-not (Test-Path $BuiltVsix)) { Fail "-SkipBuild was passed but $BuiltVsix does not exist." }
} else {
    Step 'Building Release'
    # Clean the container first: CreateVsixContainer is happy to leave a previous .vsix in place if the
    # build short-circuits, and publishing a stale container is indistinguishable from publishing a good one.
    if (Test-Path $BuiltVsix) { Remove-Item $BuiltVsix -Force }

    & dotnet build $Project --configuration Release -p:Version=$Version -v q --nologo
    if ($LASTEXITCODE -ne 0) { Fail 'Build failed.' }
    if (-not (Test-Path $BuiltVsix)) { Fail "Build reported success but produced no $BuiltVsix." }
}

# ---------------------------------------------------------------------------------------------------
# 3. Verify what was actually built
# ---------------------------------------------------------------------------------------------------
Step 'Verifying the built artifacts carry that version'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($BuiltVsix)
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -eq 'extension.vsixmanifest' }
    if (-not $entry) { Fail "$BuiltVsix contains no extension.vsixmanifest." }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { $manifestXml = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally {
    $zip.Dispose()
}

$manifestVersion = ([xml] $manifestXml).PackageManifest.Metadata.Identity.Version
$assemblyVersion = (Get-Item $BuiltDll).VersionInfo.FileVersion

Write-Host "  .vsix manifest : $manifestVersion"
Write-Host "  assembly       : $assemblyVersion"

if ($manifestVersion -ne $Version) {
    Fail "The .vsix manifest says '$manifestVersion', not '$Version'. The StampVsixVersion target did not take — do not publish this."
}
if ($assemblyVersion -ne $Version) {
    Fail "The assembly says '$assemblyVersion', not '$Version'. The update check reads the assembly's FileVersion, so publishing this would nag every user forever."
}

# ---------------------------------------------------------------------------------------------------
# 4. Stage the assets
# ---------------------------------------------------------------------------------------------------
Step 'Staging assets'

if ($Notes -and $NotesFile) { Fail 'Pass either -Notes or -NotesFile, not both.' }
if ($NotesFile) {
    if (-not (Test-Path $NotesFile)) { Fail "-NotesFile '$NotesFile' does not exist." }
    # -Encoding UTF8 explicitly: Get-Content on Windows PowerShell 5.1 defaults to the ANSI code page,
    # which turns every em dash in these notes into mojibake on the release page.
    $Notes = (Get-Content $NotesFile -Raw -Encoding UTF8).Trim()
    if (-not $Notes) { Fail "-NotesFile '$NotesFile' is empty." }
}
if (-not $Notes) { $Notes = "Build $Version" }

if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item -ItemType Directory -Path $StageDir | Out-Null

$vsixAssetName = "SQLExtended-$Version.vsix"
$vsixAsset     = Join-Path $StageDir $vsixAssetName
Copy-Item $BuiltVsix $vsixAsset

$manifest = [ordered] @{
    version = $Version
    url     = "https://github.com/$Repo/releases/download/v$Version/$vsixAssetName"
    notes   = $Notes
}
if ($MinRequiredVersion) { $manifest.minRequiredVersion = $MinRequiredVersion }

$jsonAsset = Join-Path $StageDir 'version.json'
# No BOM, deliberately. Set-Content -Encoding utf8 on Windows PowerShell 5.1 writes one, and a leading
# U+FEFF makes JsonConvert.DeserializeObject throw — which in a Release build is completely silent, since
# the only report of it is a Debug.WriteLine that isn't compiled in. The update check would simply never
# find an update again, and nothing on the machine would say why.
[System.IO.File]::WriteAllText($jsonAsset, ($manifest | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))

Write-Host "  $vsixAssetName ($([math]::Round((Get-Item $vsixAsset).Length / 1MB, 1)) MB)"
Write-Host "  version.json:"
Get-Content $jsonAsset | ForEach-Object { Write-Host "    $_" }

# ---------------------------------------------------------------------------------------------------
# 5. Publish
# ---------------------------------------------------------------------------------------------------
if ($NoPublish) {
    Step 'Stopping before publish (-NoPublish)'
    Write-Host "Assets are staged in $StageDir. Nothing was sent to GitHub." -ForegroundColor Yellow
    Write-Host "Re-run without -NoPublish to create release v$Version." -ForegroundColor Yellow
    return
}

Step "Creating release v$Version in $Repo"

$ghArgs = @(
    'release', 'create', "v$Version",
    $vsixAsset, $jsonAsset,
    '--repo', $Repo,
    '--title', "SQLExtended $Version",
    '--notes', $Notes
)
if ($Draft) { $ghArgs += '--draft' }

& gh @ghArgs
if ($LASTEXITCODE -ne 0) { Fail 'gh release create failed.' }

# ---------------------------------------------------------------------------------------------------
# 6. VSIX Gallery
# ---------------------------------------------------------------------------------------------------
# www.vsixgallery.com is a second, optional distribution channel: one POST of the .vsix body to
# /api/upload, no account, no review queue. It matters for two reasons — it renders a public details
# page (README, tags, version history) that a GitHub release does not, and it exposes a private-gallery
# Atom feed that users can paste into Tools > Options > Environment > Extensions, which is the only way
# an update ever shows up in SSMS's own Manage Extensions > Updates list. It does not replace the
# version.json feed above and the extension does not read it.
#
# Deliberately last, and deliberately non-fatal. The GitHub release is the release; if the gallery is
# down or the token is wrong, that must not read as a failed publish, so this warns and prints the
# one-liner to retry with the .vsix already staged in artifacts\.
if ($NoGallery) {
    Step 'Skipping VSIX Gallery (-NoGallery)'
} elseif ($Draft) {
    # There is no such thing as a draft on the gallery: an upload is live immediately, which would
    # advertise a version the GitHub feed is deliberately still hiding.
    Step 'Skipping VSIX Gallery (draft release)'
    Write-Host '  A gallery upload is public the moment it lands; a draft is not. Re-run after publishing.' -ForegroundColor Yellow
} else {
    Step 'Publishing to VSIX Gallery'

    $galleryId = ([xml] $manifestXml).PackageManifest.Metadata.Identity.Id

    $galleryToken = if ($GalleryToken) { $GalleryToken } else { $env:VSIXGALLERY_TOKEN }
    if (-not $galleryToken) {
        Write-Host '  Skipped: no -GalleryToken and no $env:VSIXGALLERY_TOKEN.' -ForegroundColor Yellow
        Write-Host '  The gallery mints a manage token on an untokened first upload and never shows it again, so' -ForegroundColor Yellow
        Write-Host '  uploading without one can cost the ability to manage the listing. See Deployment.md section 7.' -ForegroundColor Yellow
    } else {
        # Escaped: these values are URLs, and an unescaped one is truncated at its first & or #.
        $repoUrl    = [Uri]::EscapeDataString("https://github.com/$Repo")
        $issuesUrl  = [Uri]::EscapeDataString("https://github.com/$Repo/issues")
        $readmeUrl  = [Uri]::EscapeDataString("https://raw.githubusercontent.com/$Repo/main/README.md")
        # repo/issuetracker/readmeUrl are what the details page links and renders; without readmeUrl the
        # page shows only the manifest Description.
        $galleryQuery = "repo=$repoUrl&issuetracker=$issuesUrl&readmeUrl=$readmeUrl"

        # Windows PowerShell 5.1 still defaults to TLS 1.0/1.1, which vsixgallery.com refuses.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

        try {
            $response = Invoke-RestMethod -Method Post -Uri "https://www.vsixgallery.com/api/upload?$galleryQuery" `
                -Headers @{ 'X-Manage-Token' = $galleryToken } -ContentType 'application/octet-stream' `
                -InFile $vsixAsset -TimeoutSec 300
            Write-Host "  https://www.vsixgallery.com/extension/$galleryId/" -ForegroundColor Green
            if ($response) { $response | ConvertTo-Json -Depth 4 -Compress | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray } }
        } catch {
            Write-Warning "VSIX Gallery upload failed: $($_.Exception.Message)"
            Write-Warning "The GitHub release is published and unaffected. Retry the gallery alone with:"
            Write-Warning "  Invoke-RestMethod -Method Post -Uri 'https://www.vsixgallery.com/api/upload?$galleryQuery' -Headers @{ 'X-Manage-Token' = `$env:VSIXGALLERY_TOKEN } -ContentType 'application/octet-stream' -InFile '$vsixAsset'"
        }
    }
}

Step 'Done'
if ($Draft) {
    Write-Host "Draft created. Nothing is offered to users until it is published: releases/latest/download" -ForegroundColor Yellow
    Write-Host "resolves only published, non-prerelease releases." -ForegroundColor Yellow
} else {
    Write-Host "Users will be offered $Version within 20h of their next SSMS start (or immediately via" -ForegroundColor Green
    Write-Host "SQLExtended > Check for Updates...). Verify the feed is anonymously readable:" -ForegroundColor Green
    Write-Host "  Invoke-RestMethod 'https://github.com/$Repo/releases/latest/download/version.json'"
}
Write-Host "`nCommit the version.txt bump so the tag and the tree agree." -ForegroundColor DarkGray
