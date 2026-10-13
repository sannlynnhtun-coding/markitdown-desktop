[CmdletBinding()]
param(
    [ValidateSet('check', 'test', 'run', 'release')][string]$Mode = 'run',
    [string]$RuntimeDirectory = (Join-Path $env:LOCALAPPDATA 'MarkItDown\runtime'),
    [string]$DistributionDirectory = (Join-Path $env:LOCALAPPDATA 'MarkItDown\dist'),
    [switch]$Installer
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$cargoBin = Join-Path $env:USERPROFILE '.cargo\bin'
if (Test-Path -LiteralPath $cargoBin) { $env:PATH = "$cargoBin;$env:PATH" }
if (!(Get-Command cargo -ErrorAction SilentlyContinue)) { throw 'Install the Rust MSVC toolchain using rustup first.' }
if (!(Get-Command cmake -ErrorAction SilentlyContinue)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $cmake = & $vswhere -all -prerelease -products '*' -find 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' | Select-Object -First 1
        if ($cmake) { $env:PATH = "$(Split-Path $cmake);$env:PATH" }
    }
}
if (!$env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR = Join-Path $env:LOCALAPPDATA 'MarkItDown\rust-target' }
$env:MARKITDOWN_RUNTIME = [IO.Path]::GetFullPath($RuntimeDirectory)
$manifest = Join-Path $project 'Cargo.toml'
switch ($Mode) {
    'check' { & cargo check --locked --manifest-path $manifest --all-targets }
    'test' { & cargo test --locked --manifest-path $manifest --no-default-features }
    'run' { & cargo run --locked --manifest-path $manifest --bin markitdown-desktop }
    'release' { & cargo build --locked --release --manifest-path $manifest --bins }
}
if ($LASTEXITCODE -ne 0) { throw "Cargo $Mode failed" }
if ($Mode -ne 'release') { return }
if (!(Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'pdfium.dll')) -or !(Test-Path -LiteralPath (Join-Path $RuntimeDirectory 'tesseract\tesseract.exe'))) {
    throw 'Prepare the runtime before packaging a release.'
}
foreach ($model in 'mya','eng') {
    if (!(Test-Path -LiteralPath (Join-Path $RuntimeDirectory "tessdata\$model.traineddata"))) { throw "Missing OCR model: $model" }
}
$dist = [IO.Path]::GetFullPath($DistributionDirectory)
New-Item -ItemType Directory -Force $dist | Out-Null
foreach ($exe in 'markitdown-desktop.exe','pdf-to-markdown.exe') {
    Copy-Item -LiteralPath (Join-Path $env:CARGO_TARGET_DIR "release\$exe") -Destination $dist -Force
}
# The GPUI executable imports the MSVC runtime; ship it beside the executable.
$vcRuntime = Join-Path $RuntimeDirectory 'tesseract\vcruntime140.dll'
if (!(Test-Path -LiteralPath $vcRuntime)) { throw 'Missing native VC runtime: prepare the complete Tesseract runtime before packaging.' }
Get-ChildItem -LiteralPath (Join-Path $RuntimeDirectory 'tesseract') -Filter 'vcruntime140*.dll' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $dist -Force
}
$distRuntime = Join-Path $dist 'runtime'
New-Item -ItemType Directory -Force $distRuntime | Out-Null
foreach ($runtimeEntry in 'pdfium.dll', 'tesseract', 'tessdata', 'licenses', 'native-packages.txt') {
    $runtimeSource = Join-Path $RuntimeDirectory $runtimeEntry
    if (Test-Path -LiteralPath $runtimeSource) {
        Copy-Item -LiteralPath $runtimeSource -Destination $distRuntime -Recurse -Force
    }
}
Copy-Item -LiteralPath (Join-Path $project 'README.md'), (Join-Path $project 'THIRD_PARTY.md'), (Join-Path $project 'HOW_TO_USE.md') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $project 'assets\markitdown.ico') -Destination $dist -Force
$rustLicenseRoot = Join-Path $dist 'runtime\licenses\rust'
New-Item -ItemType Directory -Force $rustLicenseRoot | Out-Null
$metadataText = & cargo metadata --locked --offline --manifest-path $manifest --filter-platform x86_64-pc-windows-msvc --format-version 1
if ($LASTEXITCODE -ne 0) { throw 'Could not gather Rust dependency license metadata' }
$rustMetadata = $metadataText | ConvertFrom-Json
$rustNotices = foreach ($package in $rustMetadata.packages) {
    $packageRoot = Split-Path $package.manifest_path
    $licenseFiles = Get-ChildItem -LiteralPath $packageRoot | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|COPYRIGHT)' }
    $packageDestination = Join-Path $rustLicenseRoot "$($package.name)-$($package.version)"
    if ($licenseFiles) {
        New-Item -ItemType Directory -Force $packageDestination | Out-Null
        $licenseFiles | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $packageDestination -Recurse -Force }
    }
    "$($package.name) $($package.version) | $($package.license) | $($package.repository)"
}
$rustNotices | Set-Content -Encoding utf8 (Join-Path $rustLicenseRoot 'packages.txt')
Get-ChildItem -LiteralPath $dist -Recurse -File | Where-Object {
    $_.Name -ne 'SHA256SUMS.txt' -and !$_.FullName.StartsWith((Join-Path $dist 'installer') + '\', [StringComparison]::OrdinalIgnoreCase)
} | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($dist, $_.FullName)
    "$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLower())  $relative"
} | Set-Content -Encoding utf8 (Join-Path $dist 'SHA256SUMS.txt')
if ($Installer) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($isccCommand) { $isccCommand.Source } else { $null }
    if (!$iscc) {
        $userCandidate = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $userCandidate) { $iscc = $userCandidate }
    }
    if (!$iscc) {
        $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $candidate) { $iscc = $candidate }
    }
    if (!$iscc) { throw "Portable release is ready at $dist. Install Inno Setup 6 to produce the installer." }
    & $iscc '/Qp' "/DSourceDir=$dist" "/DOutputDir=$dist\installer" (Join-Path $PSScriptRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup build failed' }
    $setup = Join-Path $dist 'installer\MarkItDown-Setup-x64.exe'
    "$((Get-FileHash -LiteralPath $setup).Hash.ToLower())  MarkItDown-Setup-x64.exe" | Set-Content -Encoding ascii "$setup.sha256"
}
Write-Output "Release ready: $dist"
