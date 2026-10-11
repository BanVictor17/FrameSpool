param([string]$PreviousExePath)
$ErrorActionPreference='Stop'
$taskNewExe=Join-Path $PSScriptRoot 'dist\FrameSpool\FrameSpool.exe'
if(-not $PreviousExePath){$PreviousExePath=$taskNewExe}
$taskDirectory=Join-Path $PSScriptRoot ('obj\verification-updater\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskDirectory | Out-Null
$taskTarget=Join-Path $taskDirectory 'Renamed Photo App.exe'
$taskPayload=Join-Path $taskDirectory 'payload.exe'
$taskHelper=Join-Path $taskDirectory 'helper.exe'
Copy-Item -LiteralPath $PreviousExePath -Destination $taskTarget
Copy-Item -LiteralPath $taskNewExe -Destination $taskPayload
Copy-Item -LiteralPath $taskNewExe -Destination $taskHelper
$taskVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($taskNewExe).FileVersion
$taskParent=Start-Process -FilePath $taskTarget -ArgumentList @('--self-test',('"'+(Join-Path $taskDirectory 'before')+'"')) -WindowStyle Hidden -PassThru
$taskPlan=Join-Path $taskDirectory 'update.json'
@{Target=$taskTarget;Payload=$taskPayload;Hash=(Get-FileHash -LiteralPath $taskPayload -Algorithm SHA256).Hash;Version=$taskVersion;Installed=$false;ParentId=$taskParent.Id;ParentStarted=$taskParent.StartTime.ToUniversalTime().Ticks;Restart=$false;Quiet=$true} | ConvertTo-Json | Set-Content -LiteralPath $taskPlan -Encoding UTF8
Write-Output 'Testing real portable helper against a running, renamed executable.'
$taskUpdater=Start-Process -FilePath $taskHelper -ArgumentList @('--apply-update',('"'+$taskPlan+'"')) -WindowStyle Hidden -PassThru
if(-not $taskUpdater.WaitForExit(120000)){throw 'Portable updater verification timed out.'}
$taskUpdater.Refresh();if($taskUpdater.ExitCode -ne 0){throw ('Portable updater failed: '+(Get-Content -LiteralPath (Join-Path $taskDirectory 'update.log') -Raw))}
if((Get-FileHash -LiteralPath $taskTarget).Hash -ne (Get-FileHash -LiteralPath $taskNewExe).Hash){throw 'Portable helper installed the wrong executable.'}
$taskParent.Refresh();if(-not $taskParent.HasExited){throw 'The helper did not wait for the application to exit.'}
Write-Output 'Checking the upgraded portable app in isolation.'
$taskTest=Start-Process -FilePath $taskTarget -ArgumentList @('--self-test',('"'+(Join-Path $taskDirectory 'after')+'"')) -WindowStyle Hidden -PassThru
if(-not $taskTest.WaitForExit(120000)){throw 'Updated portable verification timed out.'}
$taskTest.Refresh();if($taskTest.ExitCode -ne 0){throw 'Updated portable app checks failed.'}
@{status='passed';version=$taskVersion;directory=$taskDirectory;checks=@('Waits for the running app to exit','Replaces renamed portable EXE','Installed EXE matches SHA256','Updated standalone app passes verification')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'obj\verification-updater\verification.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'obj\verification-updater\verification.json')
