param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $packageRoot "..")).Path
$buildRoot = Join-Path $packageRoot "build"
$distributionRoot = Join-Path $buildRoot "dist"
$workRoot = Join-Path $buildRoot "pyinstaller"
$artifactRoot = Join-Path $packageRoot "artifacts"
$virtualEnvironment = Join-Path $env:TEMP "markitdown-gui-build-venv"
$python = Join-Path $virtualEnvironment "Scripts\python.exe"
$pyinstaller = Join-Path $virtualEnvironment "Scripts\pyinstaller.exe"
$markitdownRevision = "cc0ca9edd8e23b2c7f7b778c7b148c1565730498"
$markitdownDependency = "markitdown[all] @ git+https://github.com/microsoft/markitdown.git@$markitdownRevision#subdirectory=packages/markitdown"

if (-not (Test-Path -LiteralPath $python)) {
    python -m venv $virtualEnvironment
}

& $python -m pip install --disable-pip-version-check --upgrade `
    "pyinstaller>=6.14,<7" `
    $markitdownDependency

& $python -m pip install --disable-pip-version-check --upgrade --no-deps `
    $packageRoot

New-Item -ItemType Directory -Force -Path $distributionRoot, $workRoot, $artifactRoot | Out-Null

& $pyinstaller `
    --noconfirm `
    --clean `
    --distpath $distributionRoot `
    --workpath $workRoot `
    (Join-Path $PSScriptRoot "markitdown-gui.spec")

$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$innoCompiler = $innoCandidates | Select-Object -First 1
if (-not $innoCompiler) {
    throw "Inno Setup 6 was not found. Install it before building the installer."
}

& $innoCompiler `
    "/DMyAppVersion=$Version" `
    "/DMySourceDir=$(Join-Path $distributionRoot 'MarkItDown')" `
    "/DMyOutputDir=$artifactRoot" `
    "/DMyRepoRoot=$repositoryRoot" `
    "/DMyPackageRoot=$packageRoot" `
    (Join-Path $PSScriptRoot "markitdown-gui.iss")

$installer = Join-Path $artifactRoot "MarkItDown-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer was not created at $installer"
}

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $installer
"$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($installer))" |
    Set-Content -LiteralPath "$installer.sha256" -Encoding ascii

Write-Host "Installer: $installer"
Write-Host "SHA256:    $($hash.Hash.ToLowerInvariant())"
