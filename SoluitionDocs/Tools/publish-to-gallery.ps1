<#
.SYNOPSIS
  Uploads a built .vsix to an Open VSIX Gallery — www.vsixgallery.com, or the SSMS-only one at
  ssmsgallery.azurewebsites.net.

.DESCRIPTION
  Step 6 of publish-release.ps1 calls this once per gallery, and it stands alone for a retry after a
  failed upload — the GitHub release is the release, so a gallery failure never fails a publish.

  **The two galleries are separate deployments of the same server** (madskristensen/VsixGallery) with
  separate storage, separate listings and separate manage tokens, so uploading to one lists nothing on
  the other — our id returned 200 on www.vsixgallery.com and 404 on ssmsgallery.azurewebsites.net for as
  long as only the first upload existed. The SSMS gallery's dev guide is the parent site's guide with the
  branding swapped and the URLs left behind: **every endpoint it prints says www.vsixgallery.com**, so
  following it literally re-publishes to the gallery you are already on. The host to POST to is the host
  you want to appear on.

  **The upload is multipart/form-data, not a raw body.** The gallery's own dev guide says to POST the
  .vsix "as the request body"; that returns 500 with "This request does not have a Content-Type header.
  Forms are available from requests with bodies like POSTs and a form Content-Type of either
  application/x-www-form-urlencoded or multipart/form-data." — the server reads Request.Form. So the
  bytes go in a form file field, and -InFile/-Body on their own will not do.

  HttpWebRequest rather than Invoke-RestMethod, for one reason: on a non-2xx the useful text is in the
  *response body*, and Invoke-RestMethod on PowerShell 5.1 throws away everything but "(500) Internal
  Server Error". Reading that body is what identified the multipart requirement; a retry that hides it
  would leave the next person with the same dead end.

.PARAMETER Vsix
  The .vsix to upload. Use the container that was released, not a rebuild — the gallery's copy and the
  GitHub asset for a version should be the same bytes.

.PARAMETER Gallery
  Which gallery to upload to. 'VsixGallery' (default) is www.vsixgallery.com, the general VS/SSMS one;
  'SsmsGallery' is ssmsgallery.azurewebsites.net, which lists SSMS extensions only and is what the SSMS
  Extension Manager installs and updates from.

.PARAMETER Token
  The X-Manage-Token, defaulting to the chosen gallery's variable — $env:VSIXGALLERY_TOKEN or
  $env:SSMSGALLERY_TOKEN. Required, deliberately: a gallery mints a token on an untokened first upload
  and returns it in that one response, so uploading without one can cost the ability to manage the
  listing. **The tokens are per gallery and not interchangeable**, because the listings are.

.PARAMETER Repo
  owner/name on GitHub, used for the repo, issuetracker and readmeUrl links on the details page.

.EXAMPLE
  .\SoluitionDocs\Tools\publish-to-gallery.ps1 -Vsix .\artifacts\SQLExtended-2026.8.27.2011.vsix

.EXAMPLE
  .\SoluitionDocs\Tools\publish-to-gallery.ps1 -Vsix .\artifacts\SQLExtended-2026.8.27.2011.vsix -Gallery SsmsGallery
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Vsix,
    [ValidateSet('VsixGallery', 'SsmsGallery')] [string] $Gallery = 'VsixGallery',
    [string] $Token,
    [string] $Repo = 'JamTheRadar/SQLExtended'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Everything host-specific lives here. The same server runs both, but not the same database, so the
# listing, the manage token and every URL printed below belong to one host only.
$galleries = @{
    VsixGallery = @{ Name = 'Open VSIX Gallery';      Host = 'www.vsixgallery.com';           BaseUrl = 'https://www.vsixgallery.com';           TokenVar = 'VSIXGALLERY_TOKEN' }
    SsmsGallery = @{ Name = 'Open SSMS VSIX Gallery'; Host = 'ssmsgallery.azurewebsites.net'; BaseUrl = 'https://ssmsgallery.azurewebsites.net'; TokenVar = 'SSMSGALLERY_TOKEN' }
}
$target = $galleries[$Gallery]
if (-not $Token) { $Token = [Environment]::GetEnvironmentVariable($target.TokenVar) }

if (-not (Test-Path $Vsix)) { throw "No such file: $Vsix" }
if (-not $Token) {
    throw "No manage token for $($target.Name). Pass -Token, or set `$env:$($target.TokenVar). See SoluitionDocs\Deployment.md section 7."
}

$vsixPath = (Resolve-Path $Vsix).Path

# Escaped: these are URLs, and an unescaped one is truncated at its first & or #. repo/issuetracker/
# readmeUrl are what the details page links and renders; without readmeUrl the page shows only the
# manifest Description.
$repoUrl   = [Uri]::EscapeDataString("https://github.com/$Repo")
$issuesUrl = [Uri]::EscapeDataString("https://github.com/$Repo/issues")
$readmeUrl = [Uri]::EscapeDataString("https://raw.githubusercontent.com/$Repo/main/README.md")
$uri = "$($target.BaseUrl)/api/upload?repo=$repoUrl&issuetracker=$issuesUrl&readmeUrl=$readmeUrl"

# Windows PowerShell 5.1 still defaults to TLS 1.0/1.1, which both galleries refuse.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$boundary = '----SQLExtendedBoundary' + [Guid]::NewGuid().ToString('N')
$nl = "`r`n"
$header = "--$boundary$nl" +
          "Content-Disposition: form-data; name=`"file`"; filename=`"$([IO.Path]::GetFileName($vsixPath))`"$nl" +
          "Content-Type: application/octet-stream$nl$nl"
$footer = "$nl--$boundary--$nl"

$buffer = New-Object IO.MemoryStream
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes($header); $buffer.Write($bytes, 0, $bytes.Length)
    $bytes = [IO.File]::ReadAllBytes($vsixPath);      $buffer.Write($bytes, 0, $bytes.Length)
    $bytes = [Text.Encoding]::UTF8.GetBytes($footer); $buffer.Write($bytes, 0, $bytes.Length)
    $payload = $buffer.ToArray()
} finally {
    $buffer.Dispose()
}

Write-Host "  POST $([math]::Round($payload.Length / 1MB, 1)) MB to $($target.Host)"

$request = [Net.HttpWebRequest]::Create($uri)
$request.Method = 'POST'
$request.ContentType = "multipart/form-data; boundary=$boundary"
$request.Timeout = 300000
$request.ReadWriteTimeout = 300000
$request.Headers.Add('X-Manage-Token', $Token)
$request.ContentLength = $payload.Length

$stream = $request.GetRequestStream()
try { $stream.Write($payload, 0, $payload.Length) } finally { $stream.Close() }

function Read-Body($response) {
    $reader = New-Object IO.StreamReader($response.GetResponseStream())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

try {
    $response = $request.GetResponse()
    $body = Read-Body $response
    $response.Dispose()
} catch [Net.WebException] {
    $failed = $_.Exception.Response
    if (-not $failed) { throw "$($target.Name) upload failed with no response: $($_.Exception.Message)" }
    $status = [int] $failed.StatusCode
    $text = Read-Body $failed
    throw "$($target.Name) returned HTTP $status. Response body:`n$text"
}

$result = $body | ConvertFrom-Json
Write-Host "  $($result.name) $($result.version) is live on $($target.Host)" -ForegroundColor Green
Write-Host "  details : $($target.BaseUrl)/extension/$($result.id)/"
Write-Host "  manage  : $($result.manageUrl)"
Write-Host "  feed    : $($target.BaseUrl)/feed/extension/$($result.id)"
$result
