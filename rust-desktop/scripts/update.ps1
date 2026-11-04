[CmdletBinding()]
param(
    [string]$CurrentVersion,
    [int]$AppProcessId,
    [string]$InstallDirectory,
    [switch]$Manual,
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'

function Get-ReleaseVersion([string]$Tag) {
    if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'Invalid stable release version'
    }
    return [version]$Tag.Substring(1)
}

function Test-InstallerHash([string]$Installer, [string]$Checksum) {
    if ($Checksum -cnotmatch '^([a-fA-F0-9]{64})  MarkItDown-Setup-x64\.exe\s*$') {
        throw 'Invalid installer checksum file'
    }
    if ((Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash -ne $Matches[1]) {
        throw 'Installer checksum mismatch; the update will not run'
    }
}

if ($SelfTest) {
    if ((Get-ReleaseVersion 'v0.10.0') -le (Get-ReleaseVersion 'v0.9.9')) { throw 'Version ordering failed' }
    foreach ($tag in 'v0.4.0-beta', 'v01.2.3', '../setup', 'V0.4.0', 'v0.4.0/evil') {
        $rejected = $false
        try { Get-ReleaseVersion $tag | Out-Null } catch { $rejected = $true }
        if (!$rejected) { throw "Accepted invalid tag: $tag" }
    }
    $fixture = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($fixture, 'installer fixture')
        $hash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
        Test-InstallerHash $fixture "$hash  MarkItDown-Setup-x64.exe"
        foreach ($checksum in ((('0' * 64) + '  MarkItDown-Setup-x64.exe'), "$hash  other.exe", 'invalid')) {
            $rejected = $false
            try { Test-InstallerHash $fixture $checksum } catch { $rejected = $true }
            if (!$rejected) { throw 'Accepted invalid checksum' }
        }
    } finally { Remove-Item -LiteralPath $fixture -Force }
    Write-Output 'Updater self-checks passed'
    return
}

Add-Type -AssemblyName System.Windows.Forms
function Show-UpdateMessage([string]$Message, [System.Windows.Forms.MessageBoxButtons]$Buttons = 'OK') {
    return [System.Windows.Forms.MessageBox]::Show($Message, 'MarkItDown update', $Buttons)
}

$mutex = [Threading.Mutex]::new($false, 'Local\MarkItDownDesktopUpdater')
$owned = $false
$stage = $null
try {
    try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (!$owned) {
        if ($Manual) { Show-UpdateMessage 'An update check or download is already running.' | Out-Null }
        return
    }
    $current = Get-ReleaseVersion "v$CurrentVersion"
    $app = Get-Process -Id $AppProcessId
    if (!(Test-Path -LiteralPath (Join-Path $InstallDirectory 'markitdown-desktop.exe') -PathType Leaf)) {
        throw 'Application installation not found'
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $repo = 'https://github.com/sannlynnhtun-coding/markitdown-desktop'
    $headers = @{ 'User-Agent' = "MarkItDown/$CurrentVersion"; Accept = 'application/vnd.github+json' }
    $release = Invoke-RestMethod 'https://api.github.com/repos/sannlynnhtun-coding/markitdown-desktop/releases/latest' -Headers $headers -TimeoutSec 30
    if ($release.draft -or $release.prerelease) { throw 'Expected a published stable release' }
    $latest = Get-ReleaseVersion $release.tag_name
    if ($latest -le $current) {
        if ($Manual) { Show-UpdateMessage "MarkItDown $CurrentVersion is up to date." | Out-Null }
        return
    }
    $base = "$repo/releases/download/$($release.tag_name)"
    foreach ($name in 'MarkItDown-Setup-x64.exe', 'MarkItDown-Setup-x64.exe.sha256') {
        $asset = @($release.assets | Where-Object { $_.name -ceq $name })
        if ($asset.Count -ne 1 -or $asset[0].browser_download_url -cne "$base/$name") {
            throw "Release is missing the expected update asset: $name"
        }
    }
    if ((Show-UpdateMessage "MarkItDown $latest is available (installed: $CurrentVersion). Download the update? You can keep working during the download. Installation will wait for you to export your work and close MarkItDown." 'YesNo') -ne 'Yes') { return }
    $stage = Join-Path ([IO.Path]::GetTempPath()) ('MarkItDown-update-' + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $stage | Out-Null
    $installer = Join-Path $stage 'MarkItDown-Setup-x64.exe'
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest "$base/MarkItDown-Setup-x64.exe" -UseBasicParsing -Headers $headers -TimeoutSec 600 -OutFile $installer
    $checksum = Invoke-WebRequest "$base/MarkItDown-Setup-x64.exe.sha256" -UseBasicParsing -Headers $headers -TimeoutSec 30
    Test-InstallerHash $installer ([string]$checksum.Content)
    if ((Show-UpdateMessage "Update downloaded and verified. Export any results you want to keep, then close MarkItDown. Click OK to wait for the app to close and start setup, or Cancel to discard the download. The wait expires after one hour." 'OKCancel') -ne 'OK') { return }
    if (!$app.WaitForExit(3600000)) { throw 'Update cancelled because MarkItDown is still open. Check for updates again when ready.' }
    $setup = Start-Process -FilePath $installer -ArgumentList ('/DIR="' + $InstallDirectory + '"') -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Setup did not complete (exit code $($setup.ExitCode)). Your existing installation is still available." }
} catch {
    # Offline startup checks stay quiet; requested checks and accepted updates report errors.
    if ($Manual -or $stage) { Show-UpdateMessage "Update failed: $($_.Exception.Message)" | Out-Null }
} finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        # Delete only files this invocation created; never recursively remove a computed path.
        foreach ($name in 'MarkItDown-Setup-x64.exe', 'MarkItDown-Setup-x64.exe.sha256') {
            $file = Join-Path $stage $name
            if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
        }
        [IO.Directory]::Delete($stage)
    }
    if ($owned) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
