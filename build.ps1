param([switch]$Installer)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'dist\FrameSpool'
$taskDotnet = Join-Path $taskRoot 'obj\tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) {
    $taskCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $taskCommand) { throw 'Install the .NET 10 SDK (global.json), then run build.ps1 again.' }
    $taskDotnet = $taskCommand.Source
}
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
& $taskDotnet restore (Join-Path $taskRoot 'FrameSpool.csproj') -r win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Runtime restore failed.' }
$taskDependencies=Join-Path $taskRoot 'third_party\DotNet'
New-Item -ItemType Directory -Force -Path $taskDependencies | Out-Null
$taskProject=[xml](Get-Content -LiteralPath (Join-Path $taskRoot 'FrameSpool.csproj') -Raw)
$taskRuntimeVersion=[string]$taskProject.Project.PropertyGroup.RuntimeFrameworkVersion
$taskPackageCache=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
foreach ($taskPackage in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $taskPackageRoot=Join-Path $taskPackageCache ($taskPackage+'\'+$taskRuntimeVersion)
    foreach ($taskNotice in Get-ChildItem -LiteralPath $taskPackageRoot -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' }) {
        Copy-Item -LiteralPath $taskNotice.FullName -Destination (Join-Path $taskDependencies ($taskPackage+'-'+$taskRuntimeVersion+'-'+$taskNotice.Name)) -Force
    }
}
# Remove stale files from the previous Framework build, within this exact output folder.
Get-ChildItem -LiteralPath $taskOutput -File | Remove-Item -Force
$taskCompilationOutput=(Join-Path $taskRoot 'obj\publish-build').Replace('\','/')+'/'
& $taskDotnet publish (Join-Path $taskRoot 'FrameSpool.csproj') -c Release -r win-x64 --self-contained true -o $taskOutput "-p:BaseOutputPath=$taskCompilationOutput" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($taskDoc in @('README.md', 'ROADMAP.md', 'ASSET_NOTICES.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    if (Test-Path -LiteralPath (Join-Path $taskRoot $taskDoc)) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskDoc) -Destination $taskOutput -Force }
}
Write-Output ('Built self-contained portable executable: ' + (Join-Path $taskOutput 'FrameSpool.exe'))
Copy-Item -LiteralPath (Join-Path $taskRoot 'third_party') -Destination $taskOutput -Recurse -Force
if(Test-Path -LiteralPath (Join-Path $taskRoot 'docs')){Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $taskOutput -Recurse -Force}
if ($Installer) { & (Join-Path $taskRoot 'build-msi.ps1') }
