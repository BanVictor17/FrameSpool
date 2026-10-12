param([string]$Compiler='gcc')
$ErrorActionPreference='Stop'
$taskArchive=Join-Path $PSScriptRoot 'third_party\LittleCMS\lcms2-2.19.1.zip'
$taskExpected='06d59795cb87a67f73daaa5b6f86bf77b256710aad5822e3fee7f89546a5f93f'
if((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected){throw 'LittleCMS source checksum mismatch.'}
$taskSourceRoot=Join-Path $PSScriptRoot 'obj\lcms-source'
Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskSourceRoot -Force
$taskSource=Join-Path $taskSourceRoot 'lcms2-2.19.1'
$taskOutput=Join-Path $PSScriptRoot 'native\lcms'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskFiles=@(Get-ChildItem -LiteralPath (Join-Path $taskSource 'src') -Filter '*.c' | ForEach-Object {$_.FullName})
& $Compiler -O2 -shared -static-libgcc -DCMS_DLL -DCMS_DLL_BUILD ('-I'+(Join-Path $taskSource 'include')) @taskFiles -o (Join-Path $taskOutput 'lcms2.dll') '-Wl,--no-insert-timestamp'
if($LASTEXITCODE -ne 0){throw 'LittleCMS compilation failed.'}
Copy-Item -LiteralPath (Join-Path $taskSource 'LICENSE') -Destination (Join-Path $PSScriptRoot 'third_party\LittleCMS\LICENSE') -Force
Get-FileHash -LiteralPath (Join-Path $taskOutput 'lcms2.dll') -Algorithm SHA256
