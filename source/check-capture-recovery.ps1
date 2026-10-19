$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $appRoot ('.deps-cache\capture-recovery-' + [Guid]::NewGuid().ToString('N'))
$process = Start-Process -FilePath (Join-Path $appRoot 'ReplayLite.exe') -ArgumentList '--capture-recovery-test', ('"' + $testRoot + '"') -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Capture recovery check timed out' }
$result = [IO.File]::ReadAllText((Join-Path $testRoot 'capture-recovery-test.txt'))
if ($process.ExitCode -ne 0) { throw $result }
Write-Output $result
