param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\Output~'))

$ErrorActionPreference = 'Stop'
& python (Join-Path $PSScriptRoot 'build_package.py') --output $OutputDirectory
exit $LASTEXITCODE
