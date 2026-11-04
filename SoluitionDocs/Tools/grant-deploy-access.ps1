<#
.SYNOPSIS
  Creates the SQLExtended folder under SSMS's Extensions directory and grants the current user write
  access to it, so the CopyToSsms MSBuild target can deploy on every build again.

.DESCRIPTION
  SSMS installs its Extensions folder under Program Files with BUILTIN\Users holding only
  ReadAndExecute, so a non-elevated build cannot create ...\IDE\Extensions\SQLExtended and CopyToSsms
  reports MSB3191 ("Access to the path ... is denied"). The build degrades to a warning instead of
  failing, but F5 then launches the previous build - this script is the one-time fix that restores the
  deploy.

  Needs elevation, because it writes into Program Files. Run it once per machine, and again after an
  SSMS reinstall or an SSMS major-version upgrade, both of which rebuild that folder tree. The ACL is
  applied to our own subfolder only: Extensions itself is left exactly as the installer made it, so
  nothing else that ships with SSMS is affected.

  It also grants Modify on Extensions\extensions.configurationchanged when that file exists. CopyToSsms
  touches that file after a successful deploy so the shell rebuilds its pkgdef merge - and with it the
  command table, i.e. menus, keyboard bindings and context-menu entries. Without write access the touch
  is skipped with a warning and a .vsct change only takes effect after a manual
  `Ssms.exe /updateconfiguration`.

.PARAMETER SsmsInstallDir
  The SSMS Common7\IDE folder. Must match SsmsInstallDir in SQLExtended.csproj, because the target
  deploys to $SsmsInstallDir\Extensions\SQLExtended.

.PARAMETER UserName
  The account to grant, as DOMAIN\user or user@domain. Defaults to the account running the script, which
  is what you want when you elevated your own session. Pass it explicitly if you elevated a different
  account, or to grant a colleague who builds the same tree.

.EXAMPLE
  .\SoluitionDocs\Tools\grant-deploy-access.ps1
  From an already-elevated prompt: grant the current user and report the resulting ACL.

.EXAMPLE
  Start-Process pwsh -Verb RunAs -ArgumentList '-NoExit','-File','.\SoluitionDocs\Tools\grant-deploy-access.ps1'
  Elevate from a normal prompt, keeping the window open so the output can be read.
#>
[CmdletBinding()]
param(
    [string] $SsmsInstallDir = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE',
    [string] $UserName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Step($message) { Write-Host "`n==> $message" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------------------------------
# 0. Prerequisites
# ---------------------------------------------------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isElevated = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isElevated) {
    throw ("Not elevated. This writes an ACL under Program Files, which needs an administrator token. " +
           "Re-run from an elevated prompt (or see the second .EXAMPLE in this script's help).")
}

if (-not $UserName) { $UserName = $identity.Name }

$extRoot = Join-Path $SsmsInstallDir 'Extensions'
if (-not (Test-Path -LiteralPath $extRoot)) {
    throw ("No Extensions folder at '$extRoot'. SSMS is either not installed there or lives somewhere " +
           "else - pass -SsmsInstallDir, using the same value as SsmsInstallDir in SQLExtended.csproj.")
}

$extDir = Join-Path $extRoot 'SQLExtended'

Step "Granting Modify on $extDir to $UserName"

if (-not (Test-Path -LiteralPath $extDir)) {
    New-Item -ItemType Directory -Path $extDir | Out-Null
    Write-Host "    created the folder"
}

# (OI)(CI) makes the grant inheritable, so every file CopyToSsms drops in later is writable too without
# a second pass. icacls rather than Set-Acl because it also fixes up inheritance on an existing tree,
# which is the case that matters after a reinstall left the folder owned by TrustedInstaller.
& icacls $extDir '/grant' "${UserName}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "icacls failed on '$extDir' (exit $LASTEXITCODE)."
}

# The touch target. Absent on a fresh install until SSMS has run once, hence optional and non-fatal.
$configFile = Join-Path $extRoot 'extensions.configurationchanged'
if (Test-Path -LiteralPath $configFile) {
    & icacls $configFile '/grant' "${UserName}:M" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not grant write on '$configFile'; a .vsct change will need Ssms.exe /updateconfiguration."
    }
    else {
        Write-Host "    granted write on extensions.configurationchanged (so SSMS picks up command-table changes)"
    }
}
else {
    Write-Warning ("No extensions.configurationchanged at '$configFile'. That is normal before SSMS has " +
                   "been started once; rerun this script afterwards if a .vsct change does not take effect.")
}

# ---------------------------------------------------------------------------------------------------
# 1. Verify by writing, not by reading the ACL back
# ---------------------------------------------------------------------------------------------------
# The ACL is what we just set; whether it actually lets this account write is the question the build
# asks. A successful write is the same proof CopyToSsms's own probe uses.
Step "Verifying"

$probe = Join-Path $extDir '.write-probe'
try {
    Set-Content -LiteralPath $probe -Value 'probe' -Encoding ascii
    Remove-Item -LiteralPath $probe -Force
    Write-Host "    write test passed" -ForegroundColor Green
}
catch {
    throw "Write test failed on '$extDir': $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Done. Rebuild SQLExtended and CopyToSsms will deploy into:" -ForegroundColor Green
Write-Host "  $extDir"
Write-Host "Pass -p:SsmsDeploy=false to skip that deploy on machines that never run SSMS."
