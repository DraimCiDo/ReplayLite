param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $appRoot 'dist' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$stage = Join-Path $appRoot ('.deps-cache\packages\' + [Guid]::NewGuid().ToString('N') + '\ReplayLite')
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in @('ReplayLite.exe','UpdateHelper.exe','Uninstall.exe','NAudio.dll','README.md','THIRD-PARTY.md','VALIDATION.md','UI-VALIDATION.md','VERSION','CHANGELOG.md')) {
    $path = Join-Path $appRoot $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing package input: $name" }
    Copy-Item -LiteralPath $path -Destination (Join-Path $stage $name)
}
foreach ($name in @('source','assets','licenses','docs','bin')) { Copy-Item -LiteralPath (Join-Path $appRoot $name) -Destination $stage -Recurse }
$sourceRoot = Join-Path $stage 'third-party-source'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
$archives = @(
    @{Name='FFmpeg-946fcce07b.zip'; Commit='946fcce07b6dcd0331c8cc609192aeff5e1924f8'; Repo='FFmpeg/FFmpeg'; Prefix='FFmpeg'; License='COPYING.GPLv3'; Marker='GNU GENERAL PUBLIC LICENSE'},
    @{Name='NAudio-1.10.0.zip'; Commit='f3c2a15379731239363fd0c26d6b5fe8162a5087'; Repo='naudio/NAudio'; Prefix='NAudio'; License='license.txt'; Marker='Microsoft Public License'}
)
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($archive in $archives) {
    # GitHub guarantees contents for a commit, not the byte layout of generated ZIPs.
    # These source archives are redistributed as data, never executed or used to build dependencies.
    $cached = Join-Path $appRoot ('.deps-cache\source-' + $archive.Commit + '.zip')
    $uri = 'https://codeload.github.com/' + $archive.Repo + '/zip/' + $archive.Commit
    if (-not (Test-Path -LiteralPath $cached)) { Invoke-WebRequest -Uri $uri -OutFile $cached -UseBasicParsing }
    $sourceZip = [IO.Compression.ZipFile]::OpenRead($cached)
    try {
        $prefix = $archive.Prefix + '-' + $archive.Commit + '/'
        $license = $sourceZip.GetEntry($prefix + $archive.License)
        if (-not $license -or @($sourceZip.Entries | Where-Object { -not $_.FullName.StartsWith($prefix, [StringComparison]::Ordinal) }).Count -ne 0) { throw ('Unexpected source archive structure: ' + $archive.Name) }
        $reader = [IO.StreamReader]::new($license.Open())
        try { if ($reader.ReadToEnd() -notmatch [regex]::Escape($archive.Marker)) { throw ('Source license mismatch: ' + $archive.Name) } } finally { $reader.Dispose() }
    } finally { $sourceZip.Dispose() }
    Copy-Item -LiteralPath $cached -Destination (Join-Path $sourceRoot $archive.Name)
}
[IO.File]::WriteAllText((Join-Path $stage 'portable.flag'), 'ReplayLite', [Text.UTF8Encoding]::new($false))
$zipPath = Join-Path $outputRoot 'ReplayLite-Portable.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $zipPath -CompressionLevel Optimal -Force
Copy-Item -LiteralPath (Join-Path $appRoot 'ReplayLite.exe') -Destination (Join-Path $outputRoot 'ReplayLite.exe') -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$outputRoot\ReplayLite-Setup.exe" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$appRoot\assets\ReplayLite.ico" "/resource:$zipPath,payload.zip" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll "$PSScriptRoot\Deployment.cs" "$PSScriptRoot\Setup.cs" "$PSScriptRoot\Ui.cs" "$appRoot\.deps-cache\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed' }
$hashLines = foreach ($name in @('ReplayLite-Portable.zip','ReplayLite-Setup.exe','ReplayLite.exe')) { (Get-FileHash -LiteralPath (Join-Path $outputRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name }
[IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'), $hashLines, [Text.UTF8Encoding]::new($false))
$version = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim()
$notes = @"
ReplayLite $version — запись последних минут экрана по Ctrl+Shift+F9.

После успешного сохранения использованные фрагменты буфера очищаются, индикаторы сбрасываются к новым данным. Запись продолжается без перезапуска, кадры во время экспорта не теряются. Ошибка сохранения не очищает буфер.

**Установщик:** ReplayLite-Setup.exe — выбор обычной установки для текущего пользователя или Portable.

**Portable ZIP:** ReplayLite-Portable.zip — распакуйте всю папку. Настройки находятся в data/, записи по умолчанию в Replays/.

**Обновления:** кнопка «Обновления» в приложении. Скачивание, проверка SHA-256, замена файлов и перезапуск; настройки и MP4 сохраняются. Перед обновлением текущий буфер останавливается. Отдельный EXE требует уже установленных зависимостей.

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
