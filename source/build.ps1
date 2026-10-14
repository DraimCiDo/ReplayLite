$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$toolRoot = Join-Path $appRoot '.deps-cache'
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
$versionText = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim()
if ($versionText -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must use MAJOR.MINOR.PATCH format' }
$assemblyInfo = @"
using System.Reflection;
[assembly: AssemblyTitle("ReplayLite")]
[assembly: AssemblyProduct("ReplayLite")]
[assembly: AssemblyDescription("Windows replay buffer with NVIDIA NVENC")]
[assembly: AssemblyVersion("$versionText.0")]
[assembly: AssemblyFileVersion("$versionText.0")]
"@
[IO.File]::WriteAllText((Join-Path $toolRoot 'AssemblyInfo.cs'), $assemblyInfo, [Text.UTF8Encoding]::new($false))
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 "/out:$toolRoot\IconTool.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "$PSScriptRoot\Ui.cs" "$PSScriptRoot\IconTool.cs"
if ($LASTEXITCODE -ne 0) { throw "Icon compiler exit code: $LASTEXITCODE" }
& "$toolRoot\IconTool.exe" "$appRoot\assets"
if ($LASTEXITCODE -ne 0) { throw "Icon export exit code: $LASTEXITCODE" }
& $compiler /nologo /target:winexe /main:ReplayLite.Program /platform:x64 /optimize+ /codepage:65001 /warn:4 "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$appRoot\assets\ReplayLite.ico" "/out:$appRoot\ReplayLite.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$appRoot\NAudio.dll" "$PSScriptRoot\ReplayLite.cs" "$PSScriptRoot\SelfTest.cs" "$PSScriptRoot\Ui.cs" "$PSScriptRoot\MainForm.cs" "$toolRoot\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw "Compiler exit code: $LASTEXITCODE" }
Write-Output 'Build passed: ReplayLite.exe'
