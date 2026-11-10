$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\PrecisionExtract\精准解压.exe'
$seven = Join-Path $root 'dist\PrecisionExtract\engine\7z.exe'
$work = Join-Path $root ('build\test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$work\inner","$work\outer","$work\other" | Out-Null
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Pack($type, $target, $source) {
    & $seven a "-t$type" $target $source '-y' | Out-Null
    Assert ($LASTEXITCODE -eq 0) "Packing failed: $target"
}
function Run($archive, $out, $report, $rule = '\.log$') {
    $arguments = '--headless "{0}" --output "{1}" --rule "{2}" --report "{3}"' -f $archive,$out,$rule,$report
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $p.WaitForExit(); $p.Refresh()
    return $p.ExitCode
}
function ReadResult($file) { [xml](Get-Content -LiteralPath $file -Raw) }
[IO.File]::WriteAllText("$work\inner\application.log", 'nested-content')
[IO.File]::WriteAllText("$work\inner\ignore.txt", 'do-not-extract')
[IO.File]::WriteAllText("$work\outer\application.log", 'outer-content')
[IO.File]::WriteAllText("$work\outer\配置.xml", 'unicode-content')
Pack 'tar' "$work\inner.tar" "$work\inner\*"
Pack 'gzip' "$work\outer\inner.tar.gz" "$work\inner.tar"
Pack 'zip' "$work\sample.zip" "$work\outer\*"
$tempRoot = Join-Path $env:TEMP 'PrecisionExtract'
$before = @(Get-ChildItem $tempRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
$code = Run "$work\sample.zip" "$work\output" "$work\result.xml"
Assert ($code -eq 0) "Nested extraction exit code: $code"
$r = ReadResult "$work\result.xml"
Assert (@($r.ExtractResult.Files.string).Count -eq 2) 'Nested files missing'
Assert ([int]$r.ExtractResult.Archives -eq 3) 'Expected ZIP -> GZIP -> TAR recursion'
$files = @(Get-ChildItem -LiteralPath $r.ExtractResult.Output -File)
Assert ($files.Count -eq 2) 'Unmatched file leaked'
Assert (@($files.Name | Select-Object -Unique).Count -eq 2) 'Duplicate filename overwritten'
Assert (($files | Get-Content -Raw) -contains 'nested-content') 'Nested content mismatch'
Assert (($files | Get-Content -Raw) -contains 'outer-content') 'Outer content mismatch'
Write-Host 'PASS nested ZIP/GZIP/TAR, selective extraction, duplicate names and content'

# Separate OS processes contend for the same output directory.
$jobs = @()
for ($i=0; $i -lt 8; $i++) {
 $arguments = '--headless "{0}" --output "{1}" --rule "\.log$" --report "{2}"' -f "$work\sample.zip","$work\concurrent","$work\concurrent-$i.xml"
 $jobs += Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
}
foreach ($p in $jobs) { $p.WaitForExit(); $p.Refresh(); Assert ($p.ExitCode -eq 0) "Concurrent exit: $($p.ExitCode)" }
$dirs = @()
for ($i=0; $i -lt 8; $i++) {
 $r = ReadResult "$work\concurrent-$i.xml"
 $dirs += $r.ExtractResult.Output
 Assert (@(Get-ChildItem -LiteralPath $r.ExtractResult.Output -File).Count -eq 2) 'Concurrent file loss'
}
Assert (@($dirs | Select-Object -Unique).Count -eq 8) 'Concurrent folder collision'
Assert (@(1..8 | Where-Object { "问题$_" -notin @($dirs | ForEach-Object { Split-Path $_ -Leaf }) }).Count -eq 0) 'Unexpected sequence allocation'
Write-Host 'PASS 8 simultaneous processes: unique directories, consecutive sequence and no file loss'

$code = Run "$work\sample.zip" "$work\empty" "$work\empty.xml" '^never-hit$'
Assert ($code -eq 3) 'No-match exit code'
Assert (-not (Test-Path "$work\empty")) 'No-match created output folder'
Write-Host 'PASS no match: no empty output folder'
$code = Run "$work\sample.zip" "$work\unicode" "$work\unicode.xml" '^配置\.xml$'
Assert ($code -eq 0) 'Unicode matching failed'
$r = ReadResult "$work\unicode.xml"
Assert ([IO.File]::ReadAllText([string]$r.ExtractResult.Files.string) -eq 'unicode-content') 'Unicode content mismatch'
Write-Host 'PASS Unicode filename matching'

[IO.File]::WriteAllText("$work\other\broken.zip", 'not-an-archive')
[IO.File]::WriteAllText("$work\other\error.log", 'valid-sibling')
Pack 'zip' "$work\broken-parent.zip" "$work\other\*"
$code = Run "$work\broken-parent.zip" "$work\broken-output" "$work\broken.xml"
Assert ($code -eq 2) 'Corrupt nested archive not reported'
$r = ReadResult "$work\broken.xml"
Assert (@($r.ExtractResult.Files.string).Count -eq 1) 'Valid sibling lost on corrupt nested archive'
Write-Host 'PASS corrupt nested archive reports error and keeps valid sibling'

$code = Run "$work\sample.zip" "$work\bad-regex" "$work\bad-regex.xml" '['
Assert ($code -eq 1) 'Invalid regex did not fail validation'
$after = @(Get-ChildItem $tempRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
Assert (@($after | Where-Object { $_ -notin $before }).Count -eq 0) 'Temporary task directories leaked'
Write-Host 'PASS invalid regex and temporary directory cleanup'

function DecodeUU($source, $target) {
 $bytes = [Collections.Generic.List[byte]]::new()
 foreach ($line in [IO.File]::ReadAllLines($source)) {
  if ($line.StartsWith('begin ') -or $line -eq 'end' -or $line.Length -eq 0) { continue }
  $count = ([int][char]$line[0] - 32) -band 63
  $decoded = [Collections.Generic.List[byte]]::new()
  for ($i=1; $i+3 -lt $line.Length; $i+=4) {
   $a = 0..3 | ForEach-Object { ([int][char]$line[$i+$_] - 32) -band 63 }
   $decoded.Add([byte](($a[0] -shl 2) -bor ($a[1] -shr 4)))
   $decoded.Add([byte]((($a[1] -band 15) -shl 4) -bor ($a[2] -shr 2)))
   $decoded.Add([byte]((($a[2] -band 3) -shl 6) -bor $a[3]))
  }
  for($i=0; $i -lt $count; $i++) { $bytes.Add($decoded[$i]) }
 }
 [IO.File]::WriteAllBytes($target, $bytes.ToArray())
}
DecodeUU "$PSScriptRoot\fixtures\rar-fixture.uu" "$work\sample.rar"
DecodeUU "$PSScriptRoot\fixtures\rar5-fixture.uu" "$work\sample-rar5.rar"
$code = Run "$work\sample.rar" "$work\rar" "$work\rar.xml" '\.txt$'
Assert ($code -eq 0) 'RAR extraction failed'
$r = ReadResult "$work\rar.xml"
Assert (@($r.ExtractResult.Files.string).Count -eq 2) 'RAR file count mismatch'
$code = Run "$work\sample-rar5.rar" "$work\rar5" "$work\rar5.xml" '\.bin$'
Assert ($code -eq 0) 'Compressed RAR5 extraction failed'
$r = ReadResult "$work\rar5.xml"
Assert ((Get-Item -LiteralPath $r.ExtractResult.Files.string).Length -eq 1200) 'RAR5 size mismatch'
Copy-Item "$work\sample-rar5.rar" "$work\outer\inside.rar"
Pack '7z' "$work\nested.7z" "$work\outer\*"
$code = Run "$work\nested.7z" "$work\nested-rar" "$work\nested-rar.xml" '\.bin$'
Assert ($code -eq 0) '7Z -> RAR5 extraction failed'
Write-Host 'PASS RAR, compressed RAR5 and nested 7Z -> RAR5'

& $seven a '-tzip' "$work\encrypted.zip" "$work\inner\application.log" '-psecret' '-y' | Out-Null
$code = Run "$work\encrypted.zip" "$work\encrypted-out" "$work\encrypted.xml"
Assert ($code -eq 2) 'Encrypted archive must fail visibly without hanging'
Assert (-not (Test-Path "$work\encrypted-out")) 'Encrypted archive left partial output'
Write-Host 'PASS encrypted archive fails without interactive password prompt'

# Leave a numbering gap; a new task must continue after the highest directory.
$day = Join-Path "$work\sequence" (Get-Date -Format 'yyyy-MM-dd')
New-Item -ItemType Directory -Force "$day\问题2" | Out-Null
$code = Run "$work\sample.zip" "$work\sequence" "$work\sequence.xml"
$r = ReadResult "$work\sequence.xml"
Assert ((Split-Path $r.ExtractResult.Output -Leaf) -eq '问题3') 'Sequence reused an earlier gap'
Write-Host 'PASS sequence continues after existing highest directory'
$checks = Join-Path (Split-Path $exe -Parent) 'CoreChecks.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 "/out:$checks" /reference:System.Core.dll "$root\src\Core.cs" "$PSScriptRoot\CoreChecks.cs"
Assert ($LASTEXITCODE -eq 0) 'Core checks compilation failed'
& $checks "$work\sample.zip" "$work\cancelled-output"
Assert ($LASTEXITCODE -eq 0) 'Cancellation checks failed'
Remove-Item -LiteralPath $checks
$appChecks = Join-Path (Split-Path $exe -Parent) 'AppChecks.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 "/out:$appChecks" "/reference:$exe" "$PSScriptRoot\AppChecks.cs"
Assert ($LASTEXITCODE -eq 0) 'App checks compilation failed'
& $appChecks
Assert ($LASTEXITCODE -eq 0) 'App completion checks failed'
Remove-Item -LiteralPath $appChecks
$lifecycleChecks = Join-Path (Split-Path $exe -Parent) 'TaskLifecycleChecks.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 "/out:$lifecycleChecks" "/reference:$exe" /reference:System.Windows.Forms.dll "$PSScriptRoot\TaskLifecycleChecks.cs"
Assert ($LASTEXITCODE -eq 0) 'Task lifecycle checks compilation failed'
Copy-Item -LiteralPath "$exe.config" -Destination "$lifecycleChecks.config"
& $lifecycleChecks "$work\sample.zip" "$work\lifecycle-output"
Assert ($LASTEXITCODE -eq 0) 'Task lifecycle checks failed'
Remove-Item -LiteralPath $lifecycleChecks,"$lifecycleChecks.config"
[xml]$config = Get-Content -LiteralPath "$exe.config" -Raw
Assert ($config.configuration.'System.Windows.Forms.ApplicationConfigurationSection'.add.Where({$_.key -eq 'DpiAwareness'}).value -eq 'PerMonitorV2') 'Missing DPI runtime configuration'
$after = @(Get-ChildItem $tempRoot -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
Assert (@($after | Where-Object { $_ -notin $before }).Count -eq 0) 'Final temporary task directories leaked'
Write-Host "All tests passed. Fixtures: $work"
