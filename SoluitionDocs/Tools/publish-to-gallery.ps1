<#
.SYNOPSIS
  Uploads a built .vsix to www.vsixgallery.com (Open VSIX Gallery).

.DESCRIPTION
  Step 6 of publish-release.ps1 calls this, and it stands alone for a retry after a failed upload — the
  GitHub release is the release, so a gallery failure never fails a publish.

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

.PARAMETER Token
  The X-Manage-Token. Defaults to $env:VSIXGALLERY_TOKEN. Required, deliberately: the gallery mints a
  token on an untokened first upload and returns it in that one response, so uploading without one can
  cost the ability to manage the listing.

.PARAMETER Repo
  owner/name on GitHub, used for the repo, issuetracker and readmeUrl links on the details page.

.EXAMPLE
  .\SoluitionDocs\Tools\publish-to-gallery.ps1 -Vsix .\artifacts\SQLExtended-2026.8.27.2011.vsix
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Vsix,
    [string] $Token = $env:VSIXGALLERY_TOKEN,
    [string] $Repo = 'JamTheRadar/SQLExtended'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $Vsix)) { throw "No such file: $Vsix" }
if (-not $Token) {
    throw "No manage token. Pass -Token, or set `$env:VSIXGALLERY_TOKEN. See SoluitionDocs\Deployment.md section 7."
}

$vsixPath = (Resolve-Path $Vsix).Path

# Escaped: these are URLs, and an unescaped one is truncated at its first & or #. repo/issuetracker/
# readmeUrl are what the details page links and renders; without readmeUrl the page shows only the
# manifest Description.
$repoUrl   = [Uri]::EscapeDataString("https://github.com/$Repo")
$issuesUrl = [Uri]::EscapeDataString("https://github.com/$Repo/issues")
$readmeUrl = [Uri]::EscapeDataString("https://raw.githubusercontent.com/$Repo/main/README.md")
$uri = "https://www.vsixgallery.com/api/upload?repo=$repoUrl&issuetracker=$issuesUrl&readmeUrl=$readmeUrl"

# Windows PowerShell 5.1 still defaults to TLS 1.0/1.1, which vsixgallery.com refuses.
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

Write-Host "  POST $([math]::Round($payload.Length / 1MB, 1)) MB to www.vsixgallery.com"

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
    if (-not $failed) { throw "VSIX Gallery upload failed with no response: $($_.Exception.Message)" }
    $status = [int] $failed.StatusCode
    $text = Read-Body $failed
    throw "VSIX Gallery returned HTTP $status. Response body:`n$text"
}

$result = $body | ConvertFrom-Json
Write-Host "  $($result.name) $($result.version) is live" -ForegroundColor Green
Write-Host "  details : https://www.vsixgallery.com/extension/$($result.id)/"
Write-Host "  manage  : $($result.manageUrl)"
Write-Host "  feed    : https://www.vsixgallery.com/feed/extension/$($result.id)"
$result
