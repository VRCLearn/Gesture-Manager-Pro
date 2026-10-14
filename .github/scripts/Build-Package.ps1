param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\Output~'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw | ConvertFrom-Json
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$archivePath = Join-Path $outputRoot ($manifest.name + '-' + $manifest.version + '.zip')
$includedDirectories = @('Scripts', 'Resources', 'ThirdParty')
$includedFiles = @('package.json', 'GestureManagerPro.prefab', 'LICENSE.md', 'README.md', 'CHANGELOG.md')
$files = [Collections.Generic.List[string]]::new()
foreach ($directory in $includedDirectories) {
    Get-ChildItem -LiteralPath (Join-Path $packageRoot $directory) -File -Recurse | ForEach-Object { $files.Add($_.FullName) }
    $meta = Join-Path $packageRoot ($directory + '.meta')
    if (Test-Path -LiteralPath $meta) { $files.Add($meta) }
}
foreach ($name in $includedFiles) {
    $files.Add((Join-Path $packageRoot $name))
    $meta = Join-Path $packageRoot ($name + '.meta')
    if (Test-Path -LiteralPath $meta) { $files.Add($meta) }
}
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::Create)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in ($files | Sort-Object -Unique)) {
            $relative = $file.Substring($packageRoot.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $inputStream = [IO.File]::OpenRead($file)
            $entryStream = $entry.Open()
            try { $inputStream.CopyTo($entryStream) }
            finally { $entryStream.Dispose(); $inputStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $stream.Dispose() }
$fileInfo = Get-Item -LiteralPath $archivePath
Write-Output ($fileInfo.FullName + ' (' + $fileInfo.Length + ' bytes)')
Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
