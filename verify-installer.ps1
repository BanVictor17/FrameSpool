param([string]$MsiPath, [string]$PreviousMsiPath)
$ErrorActionPreference='Stop'
if(-not $MsiPath){$MsiPath=Join-Path $PSScriptRoot 'dist\release\FrameSpool-0.2.1-win-x64.msi'}
$MsiPath=[IO.Path]::GetFullPath($MsiPath)
$taskResults=Join-Path $PSScriptRoot 'obj\verification-msi'
$taskInstall=Join-Path $taskResults ('installed-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $taskResults | Out-Null
$taskEngine=New-Object -ComObject WindowsInstaller.Installer
function Read-MsiProperty([string]$Path,[string]$Name) {
    $taskDatabase=$taskEngine.OpenDatabase($Path,0)
    $taskView=$taskDatabase.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'")
    $null=$taskView.Execute();$taskRecord=$taskView.Fetch()
    try{return $taskRecord.GetType().InvokeMember('StringData',[Reflection.BindingFlags]::GetProperty,$null,$taskRecord,@(1))}finally{$null=$taskView.Close()}
}
if(-not ('FrameSpoolMsiVerification' -as [type])) {
    Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class FrameSpoolMsiVerification { [DllImport("msi.dll", EntryPoint="MsiQueryProductStateW", CharSet=CharSet.Unicode)] public static extern int Query(string product); }'
}
function Read-ProductState([string]$Code) {return [FrameSpoolMsiVerification]::Query($Code)}
function Invoke-Msi([string[]]$Arguments) {
    $taskProcess=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if(-not $taskProcess.WaitForExit(120000)){throw 'Windows Installer did not finish within two minutes.'}
    $taskProcess.Refresh();if($taskProcess.ExitCode -notin @(0,3010)){throw ('Windows Installer returned '+$taskProcess.ExitCode+'. Read the verification-msi logs.')}
}
$taskProductCode=Read-MsiProperty $MsiPath 'ProductCode'
$taskVersion=Read-MsiProperty $MsiPath 'ProductVersion'
if((Read-ProductState $taskProductCode) -eq 5){throw 'This MSI is already installed. Verification will not replace an existing installation.'}
$taskInstalledCode=$null;$taskPreviousCode=$null;$taskChecks=New-Object Collections.Generic.List[string]
@{status='running';version=$taskVersion} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskResults 'installer-verification.json') -Encoding UTF8
try {
    if($PreviousMsiPath) {
        $PreviousMsiPath=[IO.Path]::GetFullPath($PreviousMsiPath);$taskPreviousCode=Read-MsiProperty $PreviousMsiPath 'ProductCode'
        if((Read-ProductState $taskPreviousCode) -eq 5){throw 'The older test MSI is already installed.'}
        Write-Output 'Installing previous version in the isolated verification folder.'
        Invoke-Msi @('/i',('"'+$PreviousMsiPath+'"'),('APPDIR="'+$taskInstall+'"'),'INSTALLDESKTOPSHORTCUT=0','/qn','/norestart','/L*v',('"'+(Join-Path $taskResults 'install-previous.log')+'"'))
        $taskInstalledCode=$taskPreviousCode
    }
    Write-Output 'Installing/upgrading the release MSI.'
    Invoke-Msi @('/i',('"'+$MsiPath+'"'),('APPDIR="'+$taskInstall+'"'),'INSTALLDESKTOPSHORTCUT=0','/qn','/norestart','/L*v',('"'+(Join-Path $taskResults 'install.log')+'"'))
    $taskInstalledCode=$taskProductCode
    if((Read-ProductState $taskProductCode) -ne 5){throw 'The installed product was not registered.'};$taskChecks.Add('MSI product registration')
    if($taskPreviousCode){if((Read-ProductState $taskPreviousCode) -eq 5){throw 'Major upgrade did not remove the previous product.'};$taskChecks.Add('Major upgrade removes previous product')}
    $taskExe=Join-Path $taskInstall 'FrameSpool.exe'
    if(-not(Test-Path -LiteralPath $taskExe)){throw 'The executable was not installed.'}
    if((Get-FileHash -LiteralPath $taskExe).Hash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'dist\FrameSpool\FrameSpool.exe')).Hash){throw 'Installed executable differs from the verified portable app.'};$taskChecks.Add('Installed EXE matches portable SHA256')
    $taskShortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'FrameSpool\FrameSpool.lnk'
    if(-not(Test-Path -LiteralPath $taskShortcut)){throw 'Start menu shortcut is missing.'}
    $taskShell=New-Object -ComObject WScript.Shell
    if($taskShell.CreateShortcut($taskShortcut).TargetPath -ne $taskExe){throw 'Start menu shortcut targets the wrong executable.'};$taskChecks.Add('Start menu shortcut targets installed app')
    foreach($taskNotice in @('LICENSE','ASSET_NOTICES.md','THIRD_PARTY_NOTICES.md','third_party\LibRaw\LICENSE.CDDL','third_party\LibRaw\LibRaw-0.22.2.zip')){if(-not(Test-Path -LiteralPath (Join-Path $taskInstall $taskNotice))){throw ('Installer notice/source missing: '+$taskNotice)}};$taskChecks.Add('License notices and corresponding decoder source installed')
    Write-Output 'Running application checks against the installed executable.'
    $taskRun=Start-Process -FilePath $taskExe -ArgumentList @('--self-test',('"'+(Join-Path $taskResults 'app-tests')+'"')) -WorkingDirectory $taskInstall -WindowStyle Hidden -PassThru
    if(-not $taskRun.WaitForExit(180000)){Stop-Process -Id $taskRun.Id;throw 'Installed app verification timed out.'}
    $taskRun.Refresh();if($taskRun.ExitCode -ne 0){throw 'Installed app verification failed.'};$taskChecks.Add('Installed application verification passes')
    Write-Output 'Checking uninstall.'
    Invoke-Msi @('/x',$taskProductCode,'/qn','/norestart','/L*v',('"'+(Join-Path $taskResults 'uninstall.log')+'"'))
    $taskInstalledCode=$null
    if((Test-Path -LiteralPath $taskExe) -or (Test-Path -LiteralPath $taskShortcut) -or (Read-ProductState $taskProductCode) -eq 5){throw 'Uninstall did not remove the installed executable, shortcut or registration.'};$taskChecks.Add('Uninstall removes executable, shortcut and product registration')
    @{status='passed';version=$taskVersion;checks=$taskChecks.ToArray()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskResults 'installer-verification.json') -Encoding UTF8
    Get-Content -LiteralPath (Join-Path $taskResults 'installer-verification.json')
}
catch {
    @{status='failed';version=$taskVersion;checks=$taskChecks.ToArray();error=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskResults 'installer-verification.json') -Encoding UTF8
    throw
}
finally {
    if($taskInstalledCode){Invoke-Msi @('/x',$taskInstalledCode,'/qn','/norestart','/L*v',('"'+(Join-Path $taskResults 'cleanup.log')+'"'))}
}
