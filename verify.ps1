param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskExe = Join-Path $taskRoot 'dist\FrameSpool\FrameSpool.exe'
if (-not (Test-Path -LiteralPath $taskExe)) { throw 'Build first with build.ps1.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'obj\verification' }
$taskResults = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
$taskStandalone = Join-Path ([IO.Path]::GetTempPath()) ('FrameSpool-standalone-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStandalone | Out-Null
$taskStandaloneExe = Join-Path $taskStandalone 'FrameSpool.exe'
Copy-Item -LiteralPath $taskExe -Destination $taskStandaloneExe
$taskVerifier = Start-Process -FilePath $taskStandaloneExe -ArgumentList @('--self-test', ('"' + $taskResults + '"')) -WorkingDirectory $taskStandalone -WindowStyle Hidden -PassThru
if (-not $taskVerifier.WaitForExit(60000)) {
    Stop-Process -Id $taskVerifier.Id
    throw 'Standalone verification exceeded 60 seconds.'
}
$taskVerifier.Refresh()
if ($taskVerifier.ExitCode -ne 0) { throw ('Verification failed. Read ' + (Join-Path $taskResults 'verification.txt')) }
$taskReport = Get-Content -LiteralPath (Join-Path $taskResults 'verification.json') -Raw | ConvertFrom-Json
if ($taskReport.status -ne 'passed' -or $taskReport.failed -ne 0) { throw 'Verification report contains failures.' }
[pscustomobject]@{
    Status = $taskReport.status
    Assertions = $taskReport.assertions
    Failed = $taskReport.failed
    StandaloneFolder = $taskStandalone
    FilesAlongsideExe = @(Get-ChildItem -LiteralPath $taskStandalone -File | Where-Object Name -ne 'FrameSpool.exe').Count
    Results = $taskResults
} | ConvertTo-Json
