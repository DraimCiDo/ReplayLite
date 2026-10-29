$ErrorActionPreference='Stop'
$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$appRoot=Split-Path -Parent $PSScriptRoot
$root=Join-Path $appRoot ('.deps-cache/library-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$test=Start-Process -FilePath (Join-Path $appRoot 'ReplayLite.exe') -ArgumentList '--library-test',('"'+$root+'"') -WorkingDirectory $appRoot -WindowStyle Hidden -PassThru
if(!$test.WaitForExit(90000)){$test.Kill();throw 'Library test timed out'}
$report=Join-Path $root 'library-test.txt'
if(!(Test-Path -LiteralPath $report)){throw ('Library report is missing; exit='+$test.ExitCode)}
Get-Content -LiteralPath $report -Encoding UTF8
if($test.ExitCode -ne 0){throw ('Library tests failed: '+$test.ExitCode)}
