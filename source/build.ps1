$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /warn:4 "/win32manifest:$PSScriptRoot\app.manifest" "/out:$appRoot\ReplayLite.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$appRoot\NAudio.dll" "$PSScriptRoot\ReplayLite.cs" "$PSScriptRoot\SelfTest.cs"
if ($LASTEXITCODE -ne 0) { throw "Compiler exit code: $LASTEXITCODE" }
Write-Output 'Build passed: ReplayLite.exe'
