[CmdletBinding()]
param(
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$downloadsRoot = Join-Path $repositoryRoot 'build\downloads'
$destination = Join-Path $downloadsRoot 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
$downloadUri = 'https://go.microsoft.com/fwlink/p/?LinkId=2124701'
$expectedSha256 = 'EBEBC5EC130378FF1AB513F3917BE791A9CF84F849E970B1695FF01801A9D348'

New-Item -ItemType Directory -Path $downloadsRoot -Force | Out-Null
if ($ForceDownload -and (Test-Path -LiteralPath $destination)) {
    Remove-Item -LiteralPath $destination -Force
}

if (-not (Test-Path -LiteralPath $destination)) {
    & curl.exe --ssl-no-revoke --fail --location --retry 5 --retry-delay 2 --output $destination $downloadUri
    if ($LASTEXITCODE -ne 0) {
        throw "WebView2 download failed with exit code $LASTEXITCODE."
    }
}

$actualSha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
if ($actualSha256 -ne $expectedSha256) {
    throw "WebView2 installer hash mismatch. Expected $expectedSha256; got $actualSha256. The evergreen payload may have changed and must be reviewed and repinned."
}

Write-Host "Verified WebView2 offline installer: $destination"
