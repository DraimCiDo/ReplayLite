param([string]$Version, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
if (-not $OutputPath) { $OutputPath = Join-Path $appRoot 'dist\RELEASE-NOTES.md' }
$changelog = [IO.File]::ReadAllText((Join-Path $appRoot 'CHANGELOG.md'))
$pattern = '(?ms)^## ' + [regex]::Escape($Version) + '(?:\s[^\r\n]*)?\r?\n(?<changes>.*?)(?=^## |\z)'
$match = [regex]::Match($changelog, $pattern)
if (-not $match.Success) { throw "No CHANGELOG section for $Version" }
$notes = $match.Groups['changes'].Value.Trim()
if (-not $notes.StartsWith('- ')) { throw 'Release notes must describe changes as a list' }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $notes + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
