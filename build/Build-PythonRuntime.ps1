[CmdletBinding()]
param(
    [switch] $ForceDownload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runtimeRoot = Join-Path $repositoryRoot 'runtime'
$downloadsRoot = Join-Path $repositoryRoot 'build\downloads'
$pythonRoot = Join-Path $runtimeRoot 'python'
$exifToolRoot = Join-Path $runtimeRoot 'exiftool'
$licensesRoot = Join-Path $runtimeRoot 'licenses'

$pythonVersion = '3.13.13'
$pythonArchiveName = "python-$pythonVersion-embeddable-amd64.zip"
$pythonUri = "https://www.python.org/ftp/python/$pythonVersion/$pythonArchiveName"
$pythonSha256 = '142666A4A9079507815D395B9BFB73546EC391003D385BEB559A9D68FB240062'

$exifToolVersion = '13.59'
$exifToolArchiveName = "exiftool-$($exifToolVersion)_64.zip"
$exifToolUri = "https://master.dl.sourceforge.net/project/exiftool/${exifToolArchiveName}?viasf=1"
$exifToolSha256 = '44B512B25AF500724BA579D0A53C8FC5851628B692DD5E5D94AE4A15C2CBA9EC'

function Assert-ChildPath {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $fullPath"
    }
}

function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory)][string] $Uri,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][string] $Sha256
    )

    Assert-ChildPath $Destination
    if ($ForceDownload -and (Test-Path -LiteralPath $Destination)) {
        Remove-Item -LiteralPath $Destination -Force
    }

    if (-not (Test-Path -LiteralPath $Destination)) {
        Write-Host "Downloading $(Split-Path -Leaf $Destination)..."
        if ($Uri.Contains('sourceforge.net', [System.StringComparison]::OrdinalIgnoreCase)) {
            & curl.exe --ssl-no-revoke --fail --location --retry 5 --retry-delay 2 --output $Destination $Uri
            if ($LASTEXITCODE -ne 0) {
                throw "Download failed with exit code $LASTEXITCODE."
            }
        }
        else {
            Invoke-WebRequest -Uri $Uri -OutFile $Destination
        }
    }

    $actualHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    if ($actualHash -ne $Sha256) {
        throw "Hash mismatch for $(Split-Path -Leaf $Destination). Expected $Sha256; got $actualHash."
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

New-Item -ItemType Directory -Path $downloadsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $licensesRoot -Force | Out-Null

$pythonArchive = Join-Path $downloadsRoot $pythonArchiveName
$exifToolArchive = Join-Path $downloadsRoot $exifToolArchiveName
Get-VerifiedDownload -Uri $pythonUri -Destination $pythonArchive -Sha256 $pythonSha256
Get-VerifiedDownload -Uri $exifToolUri -Destination $exifToolArchive -Sha256 $exifToolSha256

Reset-Directory $pythonRoot
Expand-Archive -LiteralPath $pythonArchive -DestinationPath $pythonRoot -Force

$pathConfiguration = Join-Path $pythonRoot 'python313._pth'
$pathLines = @(
    'python313.zip'
    '.'
    'Lib\site-packages'
    'import site'
)
[System.IO.File]::WriteAllLines($pathConfiguration, $pathLines, [System.Text.UTF8Encoding]::new($false))

$uvCommand = Get-Command uv -ErrorAction SilentlyContinue
if ($null -eq $uvCommand) {
    throw 'uv is required to materialize the hash-locked Python environment. Install uv on the build machine only.'
}

$lockFile = Join-Path $runtimeRoot 'requirements.lock'
& $uvCommand.Source pip install --python (Join-Path $pythonRoot 'python.exe') --require-hashes --no-deps --no-cache -r $lockFile
if ($LASTEXITCODE -ne 0) {
    throw "uv pip install failed with exit code $LASTEXITCODE."
}

Reset-Directory $exifToolRoot
$exifToolExtractRoot = Join-Path $downloadsRoot "exiftool-$exifToolVersion-extracted"
Reset-Directory $exifToolExtractRoot
Expand-Archive -LiteralPath $exifToolArchive -DestinationPath $exifToolExtractRoot -Force
$exifToolSource = Join-Path $exifToolExtractRoot "exiftool-$($exifToolVersion)_64"
Copy-Item -Path (Join-Path $exifToolSource '*') -Destination $exifToolRoot -Recurse -Force
Move-Item -LiteralPath (Join-Path $exifToolRoot 'exiftool(-k).exe') -Destination (Join-Path $exifToolRoot 'exiftool.exe') -Force
Remove-Item -LiteralPath $exifToolExtractRoot -Recurse -Force

Copy-Item -LiteralPath (Join-Path $pythonRoot 'LICENSE.txt') -Destination (Join-Path $licensesRoot 'PYTHON-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $exifToolRoot 'exiftool_files\LICENSE') -Destination (Join-Path $licensesRoot 'EXIFTOOL-LICENSE.txt') -Force

$markItDownLicense = Get-ChildItem -Path (Join-Path $pythonRoot 'Lib\site-packages') -Filter 'LICENSE*' -Recurse |
    Where-Object { $_.FullName -match 'markitdown-[^\\]+\.dist-info' } |
    Select-Object -First 1
if ($null -ne $markItDownLicense) {
    Copy-Item -LiteralPath $markItDownLicense.FullName -Destination (Join-Path $licensesRoot 'MARKITDOWN-LICENSE.txt') -Force
}

$inventoryPath = Join-Path $licensesRoot 'PYTHON-PACKAGES.txt'
$inventoryScript = @'
from importlib.metadata import distributions
for dist in sorted(distributions(), key=lambda item: (item.metadata.get("Name") or "").casefold()):
    name = dist.metadata.get("Name") or "unknown"
    license_name = dist.metadata.get("License-Expression") or dist.metadata.get("License") or "See package metadata"
    print(f"{name} {dist.version} | {license_name.replace(chr(10), ' ').strip()}")
'@
$inventory = & (Join-Path $pythonRoot 'python.exe') -I -c $inventoryScript
[System.IO.File]::WriteAllLines($inventoryPath, $inventory, [System.Text.UTF8Encoding]::new($false))

$validationScript = 'from importlib.metadata import version; from markitdown import MarkItDown; assert version("markitdown") == "0.1.7"; print("MarkItDown 0.1.7 runtime ready")'
& (Join-Path $pythonRoot 'python.exe') -I -c $validationScript
if ($LASTEXITCODE -ne 0) {
    throw 'The bundled Python runtime failed validation.'
}

$reportedExifToolVersion = & (Join-Path $exifToolRoot 'exiftool.exe') -ver
if ($LASTEXITCODE -ne 0) {
    throw 'The bundled ExifTool failed validation.'
}
if ($reportedExifToolVersion -ne $exifToolVersion) {
    throw "The bundled ExifTool version is $reportedExifToolVersion; expected $exifToolVersion."
}

Write-Host "Bundled Python $pythonVersion and ExifTool $exifToolVersion are ready."
