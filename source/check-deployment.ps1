$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test = Join-Path $appRoot '.deps-cache\DeploymentTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 "/out:$test" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "$PSScriptRoot\Deployment.cs" "$PSScriptRoot\Updates.cs" "$PSScriptRoot\Ui.cs" "$PSScriptRoot\DeploymentTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Deployment test compiler failed' }
& $compiler /nologo /target:winexe /platform:x64 "/out:$appRoot\.deps-cache\Fixture-old.exe" "$PSScriptRoot\UpdateFixture.cs"
if ($LASTEXITCODE -ne 0) { throw 'Old fixture compiler failed' }
& $compiler /nologo /target:winexe /platform:x64 /define:NEXT "/out:$appRoot\.deps-cache\Fixture-next.exe" "$PSScriptRoot\UpdateFixture.cs"
if ($LASTEXITCODE -ne 0) { throw 'New fixture compiler failed' }
& $test "$appRoot\dist\ReplayLite-Setup.exe" "$appRoot\dist\ReplayLite-Portable.zip" "$appRoot\.deps-cache\Fixture-old.exe" "$appRoot\.deps-cache\Fixture-next.exe" "$appRoot\UpdateHelper.exe"
if ($LASTEXITCODE -ne 0) { throw 'Deployment tests failed' }
