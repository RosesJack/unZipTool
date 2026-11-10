# Build-time downloads only. The finished portable application never downloads dependencies.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
New-Item -ItemType Directory -Force "$root\build" | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$installer = Join-Path $root 'build\7z-setup.exe'
$unpacker = Join-Path $root 'build\7zr.exe'
if (-not (Test-Path $installer)) { Invoke-WebRequest 'https://www.7-zip.org/a/7z2606-x64.exe' -OutFile $installer -UseBasicParsing }
if ((Get-FileHash $installer -Algorithm SHA256).Hash -ne '9B697F65ECC2D9B0152F5EE96D72DDE522A7827690349D9307935DACF40F42EC') { throw '7-Zip installer SHA256 mismatch' }
if (-not (Test-Path $unpacker)) { Invoke-WebRequest 'https://www.7-zip.org/a/7zr.exe' -OutFile $unpacker -UseBasicParsing }
# Official public-domain unpacker, validated against the version used for this build.
if ((Get-FileHash $unpacker -Algorithm SHA256).Hash -ne '256FECA8E274E5DA655E2A284FABAFD9F554365EB164862089DACD4E8276D282') { throw '7zr SHA256 mismatch; update the pinned bootstrap version deliberately' }
& $unpacker x $installer "-o$root\build\7zip" -y
if ($LASTEXITCODE -ne 0) { throw 'Engine unpack failed' }
