param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='0.2.6')
$ErrorActionPreference='Stop'
$taskApp=Join-Path $PSScriptRoot 'dist\FrameSpool'
$taskRelease=Join-Path $PSScriptRoot 'dist\release'
$taskMsi=Join-Path $taskRelease ('FrameSpool-'+$Version+'-win-x64.msi')
if(-not(Test-Path -LiteralPath $taskMsi)){throw 'Build the app and MSI before packaging.'}
$taskExe=Join-Path $taskRelease ('FrameSpool-'+$Version+'-win-x64.exe')
Copy-Item -LiteralPath (Join-Path $taskApp 'FrameSpool.exe') -Destination $taskExe -Force
$taskZip=Join-Path $taskRelease ('FrameSpool-'+$Version+'-win-x64-portable.zip')
Compress-Archive -Path (Join-Path $taskApp '*') -DestinationPath $taskZip -Force
$taskSource=Join-Path $taskRelease 'LibRaw-0.22.2-source.zip'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'third_party\LibRaw\LibRaw-0.22.2.zip') -Destination $taskSource -Force
$taskChecksums=@($taskExe,$taskMsi,$taskZip,$taskSource) | ForEach-Object { ((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())+' *'+[IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $taskRelease 'SHA256SUMS.txt'),$taskChecksums)
Write-Output ('Release assets ready: '+$taskRelease)
