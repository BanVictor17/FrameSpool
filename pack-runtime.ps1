$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskNative = Join-Path $PSScriptRoot 'native'
$taskOutput = Join-Path $PSScriptRoot 'obj\FrameSpool.Runtime.zip'
New-Item -ItemType Directory -Force -Path $taskNative, (Split-Path $taskOutput) | Out-Null
if (Test-Path -LiteralPath $taskOutput) { Remove-Item -LiteralPath $taskOutput }
[IO.Compression.ZipFile]::CreateFromDirectory($taskNative, $taskOutput, [IO.Compression.CompressionLevel]::Optimal, $false)
$taskArchive = [IO.Compression.ZipFile]::Open($taskOutput, [IO.Compression.ZipArchiveMode]::Update)
try {
    foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'third_party') -Recurse -File) {
        $taskName = 'notices/' + $taskFile.FullName.Substring((Join-Path $PSScriptRoot 'third_party').Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskFile.FullName,$taskName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    foreach ($taskDoc in @('LICENSE','ASSET_NOTICES.md','THIRD_PARTY_NOTICES.md')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,(Join-Path $PSScriptRoot $taskDoc),('notices/'+$taskDoc),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskArchive.Dispose() }
