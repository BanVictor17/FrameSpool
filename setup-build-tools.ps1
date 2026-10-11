param([switch]$WixOnly)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$taskTools = Join-Path $PSScriptRoot 'obj\tools'
New-Item -ItemType Directory -Force -Path $taskTools | Out-Null
if (-not $WixOnly) {
    $taskSdkZip = Join-Path $taskTools 'dotnet-sdk-10.0.401-win-x64.zip'
    if (-not (Test-Path -LiteralPath $taskSdkZip)) { Invoke-WebRequest -Uri 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip' -OutFile $taskSdkZip -UseBasicParsing }
    if ((Get-FileHash -LiteralPath $taskSdkZip -Algorithm SHA512).Hash -ne '24B670AD3D923BFCF47DF6C3B034152398B42F6DBC388E10D783AEE1CFB5E5817D399FC0AE2A12CFA822A55E61D34830CCB15C50EF6EFEE437AB874BB7C79430') { throw 'The SDK does not match the official release checksum.' }
    if (-not (Test-Path -LiteralPath (Join-Path $taskTools 'dotnet\dotnet.exe'))) { Expand-Archive -LiteralPath $taskSdkZip -DestinationPath (Join-Path $taskTools 'dotnet') -Force }
}
$taskWixZip = Join-Path $taskTools 'wix314-binaries.zip'
if (-not (Test-Path -LiteralPath $taskWixZip)) { Invoke-WebRequest -Uri 'https://github.com/wixtoolset/wix3/releases/download/wix3141rtm/wix314-binaries.zip' -OutFile $taskWixZip -UseBasicParsing }
if ((Get-FileHash -LiteralPath $taskWixZip -Algorithm SHA256).Hash -ne '6AC824E1642D6F7277D0ED7EA09411A508F6116BA6FAE0AA5F2C7DAA2FF43D31') { throw 'The WiX archive does not match the pinned upstream download.' }
if (-not (Test-Path -LiteralPath (Join-Path $taskTools 'wix\candle.exe'))) { Expand-Archive -LiteralPath $taskWixZip -DestinationPath (Join-Path $taskTools 'wix') -Force }
Write-Output 'Build tools are ready in obj\tools.'
