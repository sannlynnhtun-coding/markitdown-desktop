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
$focus = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Focus-UpdateWindow' }, $true)
$source = $source.Remove($focus.Extent.StartOffset, $focus.Extent.EndOffset - $focus.Extent.StartOffset).Insert($focus.Extent.StartOffset, @'
function Focus-UpdateWindow {
    $script:focusCalls++
    return $script:focusVisible
}
'@)
$mutexName = 'Local\MarkItDownDesktopUpdaterTest-' + [guid]::NewGuid()
$source = $source.Replace('Local\MarkItDownDesktopUpdater', $mutexName)
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
    return ([pscustomobject]@{ HasExited = $script:appExited } | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {
        param($timeout)
        if ($timeout -ne 3600000) { throw 'Unexpected exit timeout' }
        $script:events.Add('wait')
        return $true
    } -PassThru)
}
function Start-Process($FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru) {
    if ($Wait -or !$PassThru -or !(Test-Path -LiteralPath $FilePath)) { throw 'Setup must wait only for the installer, not its descendants' }
    if ($ArgumentList -ne ('/DIR="' + $script:install + '"')) { throw 'Installation directory was not preserved' }
    $script:events.Add('setup')
    $script:stagedInstaller = $FilePath
    return ([pscustomobject]@{ ExitCode = 0 } |
        Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { $script:events.Add('setup-wait') } -PassThru |
        Add-Member -MemberType ScriptMethod -Name Refresh -Value {} -PassThru)
}

$script:install = Join-Path ([IO.Path]::GetTempPath()) ('MarkItDown-updater-test-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $script:install | Out-Null
$appFile = Join-Path $script:install 'markitdown-desktop.exe'
[IO.File]::WriteAllBytes($appFile, $script:bytes)
try {
    $script:appExited = $false
    foreach ($script:corrupt in $false, $true) {
        $script:events = [Collections.Generic.List[string]]::new()
        $script:messages = [Collections.Generic.List[string]]::new()
        & $updater -CurrentVersion '0.4.0' -AppProcessId $PID -InstallDirectory $script:install -Manual
        if ($script:corrupt) {
            if ($script:events.Count -ne 0 -or !($script:messages | Where-Object { $_ -like '*checksum mismatch*' })) {
                throw 'Corrupt download was not rejected before setup'
            }
        } elseif (($script:events -join ',') -ne 'wait,setup,setup-wait' -or (Test-Path -LiteralPath $script:stagedInstaller)) {
            throw "Unexpected update flow: $($script:events -join ','); $($script:messages -join '; ')"
        }
    }
    $script:corrupt = $false
    foreach ($scenario in 'checking', 'prompt', 'closed') {
        $ready = Join-Path $script:install 'lock-ready'
        $release = Join-Path $script:install 'release-lock'
        $holder = Start-Job -ScriptBlock {
            param($name, $readyFile, $releaseFile, $scenario)
            $mutex = [Threading.Mutex]::new($false, $name)
            $mutex.WaitOne() | Out-Null
            try {
                [IO.File]::WriteAllText($readyFile, 'ready')
                if ($scenario -eq 'checking') { Start-Sleep -Milliseconds 1500 }
                else {
                    $deadline = [datetime]::UtcNow.AddSeconds(15)
                    while (!(Test-Path -LiteralPath $releaseFile) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
                }
            } finally { $mutex.ReleaseMutex(); $mutex.Dispose() }
        } -ArgumentList $mutexName, $ready, $release, $scenario
        try {
            $deadline = [datetime]::UtcNow.AddSeconds(10)
            while (!(Test-Path -LiteralPath $ready) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
            if (!(Test-Path -LiteralPath $ready)) { throw 'Concurrent lock holder did not start' }
            $script:events = [Collections.Generic.List[string]]::new()
            $script:messages = [Collections.Generic.List[string]]::new()
            $script:focusCalls = 0
            $script:focusVisible = $scenario -eq 'prompt'
            $script:appExited = $scenario -eq 'closed'
            $timer = [Diagnostics.Stopwatch]::StartNew()
            & $updater -CurrentVersion '0.4.1' -AppProcessId $PID -InstallDirectory $script:install -Manual
            if ($script:messages | Where-Object { $_ -like '*already running*' -or $_ -like '*Update failed*' }) { throw "Concurrent $scenario check failed" }
            if ($scenario -eq 'checking') {
                if ($timer.ElapsedMilliseconds -lt 500 -or ($script:events -join ',') -ne 'wait,setup,setup-wait') { throw 'Manual check did not wait for startup and continue' }
            } elseif ($script:events.Count -ne 0 -or $script:messages.Count -ne 0) { throw 'Concurrent request opened a second update flow' }
            if ($scenario -eq 'prompt' -and $script:focusCalls -ne 1) { throw 'Existing prompt was not focused' }
            if ($scenario -eq 'closed' -and $script:focusCalls -ne 0) { throw 'Queued check continued after app exit' }
        } finally {
            [IO.File]::WriteAllText($release, 'release')
            Wait-Job $holder -Timeout 15 | Out-Null
            Receive-Job $holder -ErrorAction Stop | Out-Null
            Remove-Job $holder -Force
            foreach ($file in $ready, $release) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force } }
        }
    }
    Write-Output 'Updater download, verification, app-exit and setup regression checks passed'
    Write-Output 'Real-mutex startup handoff, prompt focus and queued-check cancellation passed'
} finally {
    Remove-Item -LiteralPath $appFile -Force
    [IO.Directory]::Delete($script:install)
}
