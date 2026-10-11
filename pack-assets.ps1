$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskRoot = $PSScriptRoot
$taskAssets = Join-Path $taskRoot 'assets'
if (-not (Test-Path -LiteralPath (Join-Path $taskAssets 'layouts.json'))) { throw 'The built-in layout catalog is missing.' }
$taskUnexpected = @(Get-ChildItem -LiteralPath $taskAssets -Recurse -File | Where-Object { $_.Extension -in @('.exe', '.dll') })
if ($taskUnexpected.Count -gt 0) { throw 'The asset bundle must contain data, not executable files.' }
$taskObjectDirectory = Join-Path $taskRoot 'obj'
New-Item -ItemType Directory -Path $taskObjectDirectory -Force | Out-Null
$taskArchive = Join-Path $taskObjectDirectory 'FrameSpool.Assets.zip'
if (Test-Path -LiteralPath $taskArchive) { Remove-Item -LiteralPath $taskArchive }
[IO.Compression.ZipFile]::CreateFromDirectory($taskAssets, $taskArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
