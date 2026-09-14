[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '0.1.0',

    [string] $CertificateThumbprint,

    [string] $TimestampUrl = 'http://timestamp.digicert.com',

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot 'publish'
$installerRoot = Join-Path $artifactsRoot 'installer'
$pythonExecutable = Join-Path $repositoryRoot 'runtime\python\python.exe'
$webView2Installer = Join-Path $repositoryRoot 'build\downloads\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'

function Assert-ChildPath {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $fullPath"
    }
}

function Reset-Directory {
    param([Parameter(Mandatory)][string] $Path)

    Assert-ChildPath $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Get-SignTool {
    $windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -Path $windowsKitsRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw 'signtool.exe was not found in the Windows SDK.'
    }
    return $candidate.FullName
}

function Invoke-Sign {
    param([Parameter(Mandatory)][string] $Path)

    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        return
    }

    $signTool = Get-SignTool
    & $signTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed for $Path."
    }
}

$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msBuild = & $vsWhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msBuild) {
    throw 'Visual Studio MSBuild was not found.'
}

if (-not (Test-Path -LiteralPath $pythonExecutable)) {
    & (Join-Path $repositoryRoot 'build\Build-PythonRuntime.ps1')
}
& (Join-Path $repositoryRoot 'build\Get-WebView2Runtime.ps1')

if (-not $SkipTests) {
    & (Join-Path $repositoryRoot 'build\Test.ps1') -Configuration Release
}

Reset-Directory $publishRoot
Reset-Directory $installerRoot

$appProject = Join-Path $repositoryRoot 'MarkItDown.Desktop\MarkItDown.Desktop.csproj'
& $msBuild $appProject /restore /t:Publish /p:TargetFramework=net10.0-windows10.0.26100 /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:PublishTrimmed=false /p:WindowsAppSDKSelfContained=true /p:WindowsPackageType=None "/p:ApplicationDisplayVersion=$Version" "/p:PublishDir=$publishRoot\" /nologo /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Application publish failed with exit code $LASTEXITCODE."
}

# Python and ExifTool are release payloads, not WinUI resources. Keeping them out
# of the app's Content items avoids PRI interpreting package names as qualifiers.
$runtimePublishRoot = Join-Path $publishRoot 'Runtime'
$pythonPublishRoot = Join-Path $runtimePublishRoot 'Python'
$exifToolPublishRoot = Join-Path $runtimePublishRoot 'ExifTool'
New-Item -ItemType Directory -Path $pythonPublishRoot, $exifToolPublishRoot -Force | Out-Null
Copy-Item -Path (Join-Path $repositoryRoot 'runtime\python\*') -Destination $pythonPublishRoot -Recurse -Force
Copy-Item -Path (Join-Path $repositoryRoot 'runtime\exiftool\*') -Destination $exifToolPublishRoot -Recurse -Force

$requiredPayloads = @(
    (Join-Path $publishRoot 'MarkItDown.Desktop.exe'),
    (Join-Path $publishRoot 'Runtime\Worker\markitdown_worker.py'),
    (Join-Path $pythonPublishRoot 'python.exe'),
    (Join-Path $exifToolPublishRoot 'exiftool.exe')
)
foreach ($requiredPayload in $requiredPayloads) {
    if (-not (Test-Path -LiteralPath $requiredPayload -PathType Leaf)) {
        throw "Required release payload is missing: $requiredPayload"
    }
}

Invoke-Sign (Join-Path $publishRoot 'MarkItDown.Desktop.exe')

$msiProject = Join-Path $repositoryRoot 'installer\MarkItDown.Desktop.Installer\MarkItDown.Desktop.Installer.wixproj'
& dotnet build $msiProject --configuration Release "-p:AppVersion=$Version" "-p:PayloadDir=$publishRoot" "-p:OutputPath=$installerRoot\"
if ($LASTEXITCODE -ne 0) {
    throw "MSI build failed with exit code $LASTEXITCODE."
}

$msiPath = Join-Path $installerRoot 'MarkItDownDesktop-x64.msi'
Invoke-Sign $msiPath

$bundleProject = Join-Path $repositoryRoot 'installer\MarkItDown.Desktop.Bundle\MarkItDown.Desktop.Bundle.wixproj'
& dotnet build $bundleProject --configuration Release "-p:AppVersion=$Version" "-p:MsiPath=$msiPath" "-p:WebView2Installer=$webView2Installer" "-p:OutputPath=$installerRoot\"
if ($LASTEXITCODE -ne 0) {
    throw "Setup bundle build failed with exit code $LASTEXITCODE."
}

$setupPath = Join-Path $installerRoot 'MarkItDownDesktop-Setup-x64.exe'
Invoke-Sign $setupPath

Get-FileHash -Algorithm SHA256 -LiteralPath $msiPath, $setupPath |
    ForEach-Object { "{0} *{1}" -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path) } |
    Set-Content -LiteralPath (Join-Path $installerRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM

Write-Host "Release $Version is ready in $installerRoot"
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    Write-Warning 'The release is unsigned. Sign it with a trusted Authenticode certificate before public distribution.'
}
