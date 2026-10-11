$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'dist\FrameSpool'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'The .NET Framework 4.x C# compiler is required.' }
& (Join-Path $taskRoot 'pack-assets.ps1')
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$taskArchive = Join-Path $taskRoot 'obj\FrameSpool.Assets.zip'
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll ("/resource:" + $taskArchive + ',FrameSpool.Assets.zip') ("/win32icon:" + (Join-Path $taskRoot 'assets\application.ico')) ("/out:" + (Join-Path $taskOutput 'FrameSpool.exe')) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($taskDoc in @('README.md', 'ROADMAP.md', 'ASSET_NOTICES.md', 'LICENSE')) {
    if (Test-Path -LiteralPath (Join-Path $taskRoot $taskDoc)) {
        Copy-Item -LiteralPath (Join-Path $taskRoot $taskDoc) -Destination $taskOutput -Force
    }
}
Write-Output ('Built portable executable: ' + (Join-Path $taskOutput 'FrameSpool.exe'))
