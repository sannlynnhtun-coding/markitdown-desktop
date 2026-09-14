[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vsWhere)) {
    throw 'Visual Studio Installer vswhere.exe was not found.'
}

$msBuild = & $vsWhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msBuild) {
    throw 'Visual Studio MSBuild was not found.'
}

& dotnet test (Join-Path $repositoryRoot 'MarkItDown.Desktop.Tests\MarkItDown.Desktop.Tests.csproj') --framework net10.0 --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Unit tests failed with exit code $LASTEXITCODE."
}

& (Join-Path $repositoryRoot 'build\Test-Worker.ps1')

& $msBuild (Join-Path $repositoryRoot 'MarkItDown.Desktop\MarkItDown.Desktop.csproj') /restore /t:Build /p:TargetFramework=net10.0-windows10.0.26100 /p:Configuration=$Configuration /p:Platform=x64 /nologo /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Windows app build failed with exit code $LASTEXITCODE."
}

Write-Host 'All automated tests and the Windows x64 build passed.'
