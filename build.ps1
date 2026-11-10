$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist\PrecisionExtract'
New-Item -ItemType Directory -Force "$out\engine" | Out-Null
if (-not (Test-Path "$root\build\7zip\7z.dll")) { & "$root\bootstrap-engine.ps1" }
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/win32manifest:$root\src\app.manifest" "/out:$out\精准解压.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll "$root\src\Core.cs" "$root\src\App.cs" "$root\src\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath "$root\src\App.config" -Destination "$out\精准解压.exe.config" -Force
Copy-Item -LiteralPath "$root\build\7zip\7z.exe","$root\build\7zip\7z.dll" -Destination "$out\engine" -Force
Copy-Item -LiteralPath "$root\build\7zip\License.txt" -Destination "$out\engine\7-Zip-License.txt" -Force
Copy-Item -LiteralPath "$root\build\7zip\readme.txt" -Destination "$out\engine\7-Zip-Readme.txt" -Force
Copy-Item -LiteralPath "$root\README.md" -Destination $out -Force
Write-Host "Built: $out"
