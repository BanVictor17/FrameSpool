param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='0.2.3')
$ErrorActionPreference='Stop'
$taskSource=Join-Path $PSScriptRoot 'dist\FrameSpool'
$taskObject=Join-Path $PSScriptRoot 'obj\installer'
$taskRelease=Join-Path $PSScriptRoot 'dist\release'
$taskWix=Join-Path $PSScriptRoot 'obj\tools\wix'
if (-not (Test-Path -LiteralPath (Join-Path $taskWix 'candle.exe'))) { & (Join-Path $PSScriptRoot 'setup-build-tools.ps1') -WixOnly }
if (-not (Test-Path -LiteralPath (Join-Path $taskSource 'FrameSpool.exe'))) { throw 'Build the portable app first.' }
New-Item -ItemType Directory -Force -Path $taskObject,$taskRelease | Out-Null
$taskRtf=Join-Path $taskObject 'License.rtf'
$taskLicense=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Raw
$taskLicense += "`r`n`r`nThird-party runtime, decoder and artwork have separate terms. See the installed ASSET_NOTICES.md and THIRD_PARTY_NOTICES.md and Help > About > Licenses."
$taskLicense=$taskLicense.Replace('\','\\').Replace('{','\{').Replace('}','\}').Replace("`r",'').Replace("`n",'\par ')
[IO.File]::WriteAllText($taskRtf,'{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 '+$taskLicense+'}')
$taskManifest=Join-Path $taskObject 'Documentation.wxs'
$taskNamespace='http://schemas.microsoft.com/wix/2006/wi'
$taskDocument=New-Object Xml.XmlDocument
$taskDocument.LoadXml('<Wix xmlns="'+$taskNamespace+'"><Fragment><DirectoryRef Id="APPDIR" /></Fragment><Fragment><ComponentGroup Id="Documentation" /></Fragment></Wix>')
$taskManager=New-Object Xml.XmlNamespaceManager($taskDocument.NameTable);$taskManager.AddNamespace('w',$taskNamespace)
$taskDirectoryRoot=$taskDocument.SelectSingleNode('//w:DirectoryRef',$taskManager)
$taskGroup=$taskDocument.SelectSingleNode('//w:ComponentGroup',$taskManager)
$taskDirectories=@{ '' = $taskDirectoryRoot }
$taskHash=[Security.Cryptography.SHA256]::Create()
foreach($taskFile in Get-ChildItem -LiteralPath $taskSource -Recurse -File | Where-Object Name -ne 'FrameSpool.exe' | Sort-Object FullName) {
    $taskRelative=$taskFile.FullName.Substring($taskSource.Length+1)
    $taskDirectory=[IO.Path]::GetDirectoryName($taskRelative)
    $taskCurrent=''
    foreach($taskPart in $taskDirectory.Split('\') | Where-Object { $_ }) {
        $taskParent=$taskCurrent;$taskCurrent=if($taskCurrent){$taskCurrent+'\'+$taskPart}else{$taskPart}
        if(-not $taskDirectories.ContainsKey($taskCurrent)) {
            $taskNode=$taskDocument.CreateElement('Directory',$taskNamespace)
            $taskId='D'+([BitConverter]::ToString($taskHash.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskCurrent))).Replace('-','').Substring(0,20))
            $taskNode.SetAttribute('Id',$taskId);$taskNode.SetAttribute('Name',$taskPart)
            $taskDirectories[$taskParent].AppendChild($taskNode) | Out-Null;$taskDirectories[$taskCurrent]=$taskNode
        }
    }
    $taskBytes=$taskHash.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskRelative));$taskId='F'+([BitConverter]::ToString($taskBytes).Replace('-','').Substring(0,20))
    $taskGuid=[Guid]::new([byte[]]$taskBytes[0..15])
    $taskComponent=$taskDocument.CreateElement('Component',$taskNamespace);$taskComponent.SetAttribute('Id',$taskId);$taskComponent.SetAttribute('Guid',$taskGuid.ToString());$taskComponent.SetAttribute('Win64','yes')
    $taskFileNode=$taskDocument.CreateElement('File',$taskNamespace);$taskFileNode.SetAttribute('Id',$taskId+'File');$taskFileNode.SetAttribute('Source',$taskFile.FullName);$taskComponent.AppendChild($taskFileNode) | Out-Null
    $taskKey=$taskDocument.CreateElement('RegistryValue',$taskNamespace);$taskKey.SetAttribute('Root','HKCU');$taskKey.SetAttribute('Key','Software\FrameSpool\Installer\Files');$taskKey.SetAttribute('Name',$taskId);$taskKey.SetAttribute('Type','integer');$taskKey.SetAttribute('Value','1');$taskKey.SetAttribute('KeyPath','yes');$taskComponent.AppendChild($taskKey) | Out-Null
    $taskDirectories[$taskDirectory].AppendChild($taskComponent) | Out-Null
    $taskReference=$taskDocument.CreateElement('ComponentRef',$taskNamespace);$taskReference.SetAttribute('Id',$taskId);$taskGroup.AppendChild($taskReference) | Out-Null
}
$taskCleanup=$taskDocument.CreateElement('Component',$taskNamespace);$taskCleanup.SetAttribute('Id','DocumentationCleanup');$taskCleanup.SetAttribute('Guid','F23EF11B-C11B-4A55-989E-2CDBA71B75DC');$taskCleanup.SetAttribute('Win64','yes')
$taskCleanupKey=$taskDocument.CreateElement('RegistryValue',$taskNamespace);$taskCleanupKey.SetAttribute('Root','HKCU');$taskCleanupKey.SetAttribute('Key','Software\FrameSpool\Installer');$taskCleanupKey.SetAttribute('Name','Documentation');$taskCleanupKey.SetAttribute('Type','integer');$taskCleanupKey.SetAttribute('Value','1');$taskCleanupKey.SetAttribute('KeyPath','yes');$taskCleanup.AppendChild($taskCleanupKey) | Out-Null
foreach($taskDirectory in $taskDirectories.Values | Where-Object { $_ -ne $taskDirectoryRoot }) {
    $taskRemoval=$taskDocument.CreateElement('RemoveFolder',$taskNamespace);$taskRemoval.SetAttribute('Id','Remove'+$taskDirectory.GetAttribute('Id'));$taskRemoval.SetAttribute('Directory',$taskDirectory.GetAttribute('Id'));$taskRemoval.SetAttribute('On','uninstall');$taskCleanup.AppendChild($taskRemoval) | Out-Null
}
$taskDirectoryRoot.AppendChild($taskCleanup) | Out-Null
$taskReference=$taskDocument.CreateElement('ComponentRef',$taskNamespace);$taskReference.SetAttribute('Id','DocumentationCleanup');$taskGroup.AppendChild($taskReference) | Out-Null
$taskProductCode=[Guid]::new([byte[]]($taskHash.ComputeHash([Text.Encoding]::UTF8.GetBytes('FrameSpool MSI '+$Version)))[0..15])
$taskHash.Dispose();$taskDocument.Save($taskManifest)
& (Join-Path $taskWix 'candle.exe') -nologo -arch x64 ('-dProductCode='+$taskProductCode.ToString()) ('-dAppVersion='+$Version) ('-dSourceDir='+$taskSource) ('-dAppIcon='+(Join-Path $PSScriptRoot 'assets\application.ico')) ('-dLicenseRtf='+$taskRtf) -out ($taskObject+'/') (Join-Path $PSScriptRoot 'installer\FrameSpool.wxs') $taskManifest
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$taskMsi=Join-Path $taskRelease ('FrameSpool-'+$Version+'-win-x64.msi')
# ICE91 concerns changing ALLUSERS. This package deliberately supports only per-user installation.
& (Join-Path $taskWix 'light.exe') -nologo -sice:ICE91 -ext WixUIExtension -out $taskMsi (Join-Path $taskObject 'FrameSpool.wixobj') (Join-Path $taskObject 'Documentation.wixobj')
if($LASTEXITCODE -ne 0){throw 'Installer linking or validation failed.'}
Write-Output ('Built MSI: '+$taskMsi)
