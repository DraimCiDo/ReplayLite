param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $appRoot 'dist' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$stage = Join-Path $appRoot ('.deps-cache\packages\' + [Guid]::NewGuid().ToString('N') + '\ReplayLite')
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in @('ReplayLite.exe','NAudio.dll','README.md','THIRD-PARTY.md','VALIDATION.md','UI-VALIDATION.md','VERSION','CHANGELOG.md')) {
    $path = Join-Path $appRoot $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing package input: $name" }
    Copy-Item -LiteralPath $path -Destination (Join-Path $stage $name)
}
foreach ($name in @('source','assets','licenses','docs','bin')) { Copy-Item -LiteralPath (Join-Path $appRoot $name) -Destination $stage -Recurse }
$sourceRoot = Join-Path $stage 'third-party-source'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
$archives = @(
    @{Name='FFmpeg-946fcce07b.zip'; Uri='https://codeload.github.com/FFmpeg/FFmpeg/zip/946fcce07b'; Hash='11E6ADB3AE54F116867A292B3402E36B80BE8810C32CA47CD2942A6BE840DFCD'},
    @{Name='NAudio-1.10.0.zip'; Uri='https://codeload.github.com/naudio/NAudio/zip/refs/tags/v1.10.0'; Hash='E88BD0272957DE1CCE651771F0FEDCC7A00FA3CD27D72A3923B5E4F3EB009769'}
)
foreach ($archive in $archives) {
    $cached = Join-Path $appRoot ('.deps-cache\' + $archive.Name)
    if (-not (Test-Path -LiteralPath $cached)) { Invoke-WebRequest -Uri $archive.Uri -OutFile $cached -UseBasicParsing }
    if ((Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash -ne $archive.Hash) { throw ('Source archive SHA256 mismatch: ' + $archive.Name) }
    Copy-Item -LiteralPath $cached -Destination (Join-Path $sourceRoot $archive.Name)
}
$zipPath = Join-Path $outputRoot 'ReplayLite-Windows.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $zipPath -CompressionLevel Optimal -Force
Copy-Item -LiteralPath (Join-Path $appRoot 'ReplayLite.exe') -Destination (Join-Path $outputRoot 'ReplayLite.exe') -Force
$hashLines = foreach ($name in @('ReplayLite-Windows.zip','ReplayLite.exe')) { (Get-FileHash -LiteralPath (Join-Path $outputRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name }
[IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'), $hashLines, [Text.UTF8Encoding]::new($false))
$version = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim()
$notes = @"
ReplayLite $version — запись последних минут экрана по Ctrl+Shift+F9.

**Рекомендуемая установка:** скачайте ReplayLite-Windows.zip, распакуйте всю папку и запустите ReplayLite.exe. ZIP содержит приложение, зависимости и сведения об их лицензиях. Отдельный EXE предназначен для обновления существующей папки.

Windows 10/11 x64 · NVIDIA NVENC · .NET Framework 4.8. EXE не подписан.

Сборка и проверка интерфейса выполнены GitHub Actions. Реальный захват через NVENC проверен локально на RTX 4060; в CI нет NVIDIA. Новые проверки игрового FPS не проводились.

[Возможности и скриншоты](https://github.com/DraimCiDo/ReplayLite#readme) · [История изменений](https://github.com/DraimCiDo/ReplayLite/blob/main/CHANGELOG.md) · [Проверка записи](https://github.com/DraimCiDo/ReplayLite/blob/main/VALIDATION.md)
"@
[IO.File]::WriteAllText((Join-Path $outputRoot 'RELEASE-NOTES.md'), $notes, [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($zip.Entries.FullName)
    foreach ($required in @('ReplayLite/ReplayLite.exe','ReplayLite/NAudio.dll','ReplayLite/bin/ffmpeg.exe','ReplayLite/bin/ffprobe.exe','ReplayLite/licenses/NAudio-Ms-PL.txt','ReplayLite/third-party-source/FFmpeg-946fcce07b.zip')) {
        if ($entries -notcontains $required) { throw "Package missing: $required" }
    }
    if ($entries -match '/(\.git|\.deps-cache)/|\.(mp4|ts)$') { throw 'Private or temporary files in package' }
} finally { $zip.Dispose() }
Write-Output "Package verified: $zipPath"
