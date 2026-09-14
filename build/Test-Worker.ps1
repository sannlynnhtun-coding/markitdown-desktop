[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$python = Join-Path $repositoryRoot 'runtime\python\python.exe'

if (-not (Test-Path -LiteralPath $python)) {
    throw 'The bundled Python runtime is missing. Run build\Build-PythonRuntime.ps1 first.'
}

& $python -I -m unittest discover -s (Join-Path $repositoryRoot 'worker-tests') -p 'test_*.py' -v
if ($LASTEXITCODE -ne 0) {
    throw "Worker tests failed with exit code $LASTEXITCODE."
}
