$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $appRoot ('.deps-cache\capture-image-' + [Guid]::NewGuid().ToString('N'))
$process = Start-Process -FilePath (Join-Path $appRoot 'ReplayLite.exe') -ArgumentList '--capture-image-test', ('"' + $testRoot + '"') -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Capture image check timed out' }
$result = [IO.File]::ReadAllText((Join-Path $testRoot 'capture-image-test.txt'))
if ($process.ExitCode -ne 0 -or $result.Contains('FAIL:')) { throw $result }
Write-Output $result
