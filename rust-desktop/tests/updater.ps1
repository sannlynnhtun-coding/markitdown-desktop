$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../scripts/update.ps1') -Raw
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
if ($errors) { throw 'Updater parse failed' }
# Replace only native dialogs; run the production script with network/process test doubles.
$dialog = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Show-UpdateMessage' }, $true)
$source = $source.Remove($dialog.Extent.StartOffset, $dialog.Extent.EndOffset - $dialog.Extent.StartOffset).Insert($dialog.Extent.StartOffset, @'
function Show-UpdateMessage($Message, $Buttons) {
    $script:messages.Add($Message)
    if ($Buttons -eq 'YesNo') { return 'Yes' }
    return 'OK'
}
'@)
$source = $source.Replace('Local\MarkItDownDesktopUpdater', ('Local\MarkItDownDesktopUpdaterTest-' + [guid]::NewGuid()))
$updater = [scriptblock]::Create($source)
$script:bytes = [Text.Encoding]::ASCII.GetBytes('test installer; never execute')
$sha = [Security.Cryptography.SHA256]::Create()
try { $script:hash = [BitConverter]::ToString($sha.ComputeHash($script:bytes)).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }

function Invoke-RestMethod {
    return @{
        tag_name = 'v9.0.0'; draft = $false; prerelease = $false
        assets = @('MarkItDown-Setup-x64.exe', 'MarkItDown-Setup-x64.exe.sha256') | ForEach-Object {
            @{ name = $_; browser_download_url = "https://github.com/sannlynnhtun-coding/markitdown-desktop/releases/download/v9.0.0/$_" }
        }
    }
}
function Invoke-WebRequest($Uri, $Headers, $TimeoutSec, [switch]$UseBasicParsing, $OutFile) {
    $payload = $script:bytes
    if ($Uri.EndsWith('.sha256')) {
        $hash = if ($script:corrupt) { '0' * 64 } else { $script:hash }
        $payload = [Text.Encoding]::ASCII.GetBytes("$hash  MarkItDown-Setup-x64.exe`r`n")
    }
    # GitHub's application/octet-stream response is byte[] in Windows PowerShell 5.1.
    if (!$OutFile) { return @{ Content = $payload } }
    [IO.File]::WriteAllBytes($OutFile, $payload)
}
function Get-Process {
    return ([pscustomobject]@{} | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {
        param($timeout)
        if ($timeout -ne 3600000) { throw 'Unexpected exit timeout' }
        $script:events.Add('wait')
        return $true
    } -PassThru)
}
function Start-Process($FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru) {
    if (!$Wait -or !$PassThru -or !(Test-Path -LiteralPath $FilePath)) { throw 'Invalid setup invocation' }
    if ($ArgumentList -ne ('/DIR="' + $script:install + '"')) { throw 'Installation directory was not preserved' }
    $script:events.Add('setup')
    $script:stagedInstaller = $FilePath
    return @{ ExitCode = 0 }
}

$script:install = Join-Path ([IO.Path]::GetTempPath()) ('MarkItDown-updater-test-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $script:install | Out-Null
$appFile = Join-Path $script:install 'markitdown-desktop.exe'
[IO.File]::WriteAllBytes($appFile, $script:bytes)
try {
    foreach ($script:corrupt in $false, $true) {
        $script:events = [Collections.Generic.List[string]]::new()
        $script:messages = [Collections.Generic.List[string]]::new()
        & $updater -CurrentVersion '0.4.0' -AppProcessId $PID -InstallDirectory $script:install -Manual
        if ($script:corrupt) {
            if ($script:events.Count -ne 0 -or !($script:messages | Where-Object { $_ -like '*checksum mismatch*' })) {
                throw 'Corrupt download was not rejected before setup'
            }
        } elseif (($script:events -join ',') -ne 'wait,setup' -or (Test-Path -LiteralPath $script:stagedInstaller)) {
            throw "Unexpected update flow: $($script:events -join ','); $($script:messages -join '; ')"
        }
    }
    Write-Output 'Updater download, verification, app-exit and setup regression checks passed'
} finally {
    Remove-Item -LiteralPath $appFile -Force
    [IO.Directory]::Delete($script:install)
}
