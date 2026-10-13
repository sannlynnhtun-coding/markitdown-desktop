[CmdletBinding()]
param(
    [string]$RuntimeDirectory = (Join-Path $env:LOCALAPPDATA 'MarkItDown\runtime'),
    [string]$TesseractDirectory,
    [switch]$OfflineRevocation
)
$ErrorActionPreference = 'Stop'
$runtimeRoot = [IO.Path]::GetFullPath($RuntimeDirectory)
$cache = Join-Path $env:LOCALAPPDATA 'MarkItDown\build-cache'
New-Item -ItemType Directory -Force $runtimeRoot, $cache, (Join-Path $runtimeRoot 'tessdata'), (Join-Path $runtimeRoot 'licenses') | Out-Null

function Get-VerifiedFile([string]$Url, [string]$Destination, [string]$Sha256) {
    if (!(Test-Path -LiteralPath $Destination) -or (Get-FileHash -LiteralPath $Destination).Hash -ne $Sha256) {
        $curlOptions = @('--fail', '--location', '--retry', '3', '--connect-timeout', '30', '--max-time', '300')
        if ($OfflineRevocation) { $curlOptions += '--ssl-revoke-best-effort' }
        & curl.exe @curlOptions $Url --output $Destination
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $Url" }
    }
    if ((Get-FileHash -LiteralPath $Destination).Hash -ne $Sha256) { throw "Checksum mismatch: $Destination" }
}

$pdfArchive = Join-Path $cache 'pdfium-8100-win-x64.tgz'
Get-VerifiedFile 'https://github.com/bblanchon/pdfium-binaries/releases/download/chromium/8100/pdfium-win-x64.tgz' $pdfArchive '51621bcc1d9be9ea448f4b84b0516862b928e3968d3fca899af1a6d6e2848d6d'
$pdfExtract = Join-Path $cache 'pdfium-8100'
New-Item -ItemType Directory -Force $pdfExtract | Out-Null
& tar.exe -xf $pdfArchive -C $pdfExtract
if ($LASTEXITCODE -ne 0) { throw 'PDFium extraction failed' }
Copy-Item -LiteralPath (Join-Path $pdfExtract 'bin\pdfium.dll') -Destination (Join-Path $runtimeRoot 'pdfium.dll') -Force
$pdfLicenseRoot = Join-Path $runtimeRoot 'licenses\pdfium'
New-Item -ItemType Directory -Force $pdfLicenseRoot | Out-Null
Get-ChildItem -LiteralPath (Join-Path $pdfExtract 'licenses') | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $pdfLicenseRoot -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $pdfExtract 'LICENSE') -Destination (Join-Path $runtimeRoot 'licenses\PDFium-LICENSE') -Force

if (!$TesseractDirectory) {
    $mamba = Join-Path $cache 'micromamba.exe'
    Get-VerifiedFile 'https://github.com/mamba-org/micromamba-releases/releases/download/2.9.0-0/micromamba-win-64.exe' $mamba 'a6d804394b2418991c4e29562853eaace2f2ce9d9da661a98e74e02e8dbb44b0'
    $prefix = Join-Path $cache 'tesseract-env'
    $mambaRoot = Join-Path $cache 'mamba'
    $previousRevoke = $env:MAMBA_SSL_NO_REVOKE
    try {
        if ($OfflineRevocation) { $env:MAMBA_SSL_NO_REVOKE = 'true' }
        & $mamba create --yes --no-rc --root-prefix $mambaRoot --prefix $prefix --channel conda-forge 'tesseract=5.5.3' 'libarchive=*=lgpl*'
        if ($LASTEXITCODE -ne 0) { throw 'Tesseract dependency resolution failed' }
    } finally { $env:MAMBA_SSL_NO_REVOKE = $previousRevoke }
    $TesseractDirectory = Join-Path $prefix 'Library\bin'
    & $mamba list --no-rc --prefix $prefix --explicit | Set-Content -Encoding utf8 (Join-Path $runtimeRoot 'native-packages.txt')
}
if (!(Test-Path -LiteralPath (Join-Path $TesseractDirectory 'tesseract.exe'))) { throw 'TesseractDirectory must contain tesseract.exe and its dependency DLLs' }
$ocrPrefix = Split-Path (Split-Path $TesseractDirectory -Parent) -Parent
$metadataRoot = Join-Path $ocrPrefix 'conda-meta'
if (Test-Path -LiteralPath $metadataRoot) {
    $licenseRoot = Join-Path $runtimeRoot 'licenses\tesseract'
    New-Item -ItemType Directory -Force $licenseRoot | Out-Null
    $nativeRecords = foreach ($metadata in Get-ChildItem -LiteralPath $metadataRoot -Filter '*.json') {
        $record = Get-Content -LiteralPath $metadata.FullName -Raw | ConvertFrom-Json
        if ($record.link.source) {
            $licenses = Join-Path $record.link.source 'info\licenses'
            if (Test-Path -LiteralPath $licenses) {
                $packageLicenses = Join-Path $licenseRoot $record.name
                New-Item -ItemType Directory -Force $packageLicenses | Out-Null
                Get-ChildItem -LiteralPath $licenses | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $packageLicenses -Recurse -Force }
            }
        }
        "$($record.url)  sha256:$($record.sha256)  license:$($record.license)"
    }
    $nativeRecords | Set-Content -Encoding utf8 (Join-Path $runtimeRoot 'native-packages.txt')
}
$ocrRoot = Join-Path $runtimeRoot 'tesseract'
New-Item -ItemType Directory -Force $ocrRoot | Out-Null
Get-ChildItem -LiteralPath $TesseractDirectory -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'tesseract.exe' } | ForEach-Object {
    if ($_.Name -match '^python') { throw 'Unexpected Python dependency in OCR runtime' }
    Copy-Item -LiteralPath $_.FullName -Destination $ocrRoot -Force
}

$modelCommit = 'e12c65a915945e4c28e237a9b52bc4a8f39a0cec'
Get-VerifiedFile "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$modelCommit/mya.traineddata" (Join-Path $runtimeRoot 'tessdata\mya.traineddata') '629dc3ab7adf4321f329d99f8c737d920308df7519734e36277b773c24df0699'
Get-VerifiedFile "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$modelCommit/eng.traineddata" (Join-Path $runtimeRoot 'tessdata\eng.traineddata') '8280aed0782fe27257a68ea10fe7ef324ca0f8d85bd2fd145d1c2b560bcb66ba'
& curl.exe --fail --location --retry 3 "https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$modelCommit/LICENSE" --output (Join-Path $runtimeRoot 'licenses\tessdata-LICENSE')
if ($LASTEXITCODE -ne 0) { throw 'Could not download model license' }
& (Join-Path $ocrRoot 'tesseract.exe') --tessdata-dir (Join-Path $runtimeRoot 'tessdata') --list-langs
if ($LASTEXITCODE -ne 0) { throw 'Bundled OCR runtime could not start' }
Write-Output "Runtime ready: $runtimeRoot"
Write-Output "For development: set MARKITDOWN_RUNTIME to this directory."
