$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $appRoot 'ReplayLite.exe'
$version = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim()
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$version.0") { throw 'EXE version does not match VERSION' }
$resultFile = Join-Path $appRoot '.deps-cache\ui-test.txt'
$process = Start-Process -FilePath $exe -ArgumentList '--ui-test', ('"' + $resultFile + '"') -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'UI check timed out' }
if ($process.ExitCode -ne 0) { throw ([IO.File]::ReadAllText($resultFile)) }
Get-Content -LiteralPath $resultFile -Encoding utf8
Write-Output "EXE version $version.0 and UI checks passed"
