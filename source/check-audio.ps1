$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $appRoot ('.deps-cache\audio-test-' + [Guid]::NewGuid().ToString('N'))
$process = Start-Process -FilePath (Join-Path $appRoot 'ReplayLite.exe') -ArgumentList '--audio-test', ('"' + $testRoot + '"') -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(40000)) { $process.Kill(); throw 'Audio recovery check timed out' }
$result = [IO.File]::ReadAllText((Join-Path $testRoot 'audio-test.txt'))
if ($process.ExitCode -ne 0) { throw $result }
Write-Output $result
