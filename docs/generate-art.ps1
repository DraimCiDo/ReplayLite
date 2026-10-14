$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $appRoot '.deps-cache'
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 "/out:$toolRoot\DocArt.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "$appRoot\source\Ui.cs" "$PSScriptRoot\DocArt.cs"
if ($LASTEXITCODE -ne 0) { throw "Documentation art compiler exit code: $LASTEXITCODE" }
& "$toolRoot\DocArt.exe" "$appRoot\assets\docs"
if ($LASTEXITCODE -ne 0) { throw "Documentation art export exit code: $LASTEXITCODE" }
