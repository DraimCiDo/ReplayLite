param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $appRoot 'dist' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$stage = Join-Path $appRoot ('.deps-cache\packages\' + [Guid]::NewGuid().ToString('N') + '\ReplayLite')
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in @('ReplayLite.exe','UpdateHelper.exe','Uninstall.exe','NAudio.dll','README.md','AGENTS.md','LICENSE','THIRD-PARTY.md','VALIDATION.md','UI-VALIDATION.md','VERSION','CHANGELOG.md')) {
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
Add-Type -AssemblyName System.IO.Compression
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
# Windows PowerShell Compress-Archive opens source files with exclusive sharing,
# which fails while a scanner holds a read handle to a newly copied source ZIP.
# ZipFileExtensions permits shared readers; keep the previous archive until creation succeeds.
$pendingZip = $zipPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    $packageZip = [IO.Compression.ZipFile]::Open($pendingZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
            # Windows PowerShell targets an older CLR profile where CreateFromDirectory
            # emits backslashes. Explicit entry names keep the existing portable ZIP layout.
            $entryName = 'ReplayLite/' + $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($packageZip, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $packageZip.Dispose() }
    if (Test-Path -LiteralPath $zipPath) { [IO.File]::Replace($pendingZip, $zipPath, [NullString]::Value) }
    else { [IO.File]::Move($pendingZip, $zipPath) }
} finally {
    if (Test-Path -LiteralPath $pendingZip) { [IO.File]::Delete($pendingZip) }
}
Copy-Item -LiteralPath (Join-Path $appRoot 'ReplayLite.exe') -Destination (Join-Path $outputRoot 'ReplayLite.exe') -Force
$compiler = & (Join-Path $PSScriptRoot 'setup-installer-toolchain.ps1')
$version = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim()
# The ZIP remains the update/portable format. The installer gets individual files,
# so 100 MB of compressed data no longer becomes the .NET assembly's code section.
[IO.File]::WriteAllText((Join-Path $stage 'installed.flag'), 'ReplayLite', [Text.UTF8Encoding]::new($false))
$manifest = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { $_.FullName.Substring($stage.Length + 1) }) + 'package-files.txt'
[IO.File]::WriteAllLines((Join-Path $stage 'package-files.txt'), $manifest, [Text.UTF8Encoding]::new($false))
& $compiler --quiet-progress --no-ide-signtools "--define=PackageRoot=$stage" "--define=OutputRoot=$outputRoot" "--define=AppVersion=$version" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed' }
& (Join-Path $PSScriptRoot 'package-checksums.ps1') -OutputDirectory $outputRoot
& (Join-Path $PSScriptRoot 'release-notes.ps1') -OutputPath (Join-Path $outputRoot 'RELEASE-NOTES.md')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($zip.Entries.FullName)
    foreach ($required in @('ReplayLite/ReplayLite.exe','ReplayLite/NAudio.dll','ReplayLite/bin/ffmpeg.exe','ReplayLite/bin/ffprobe.exe','ReplayLite/licenses/NAudio-Ms-PL.txt','ReplayLite/third-party-source/FFmpeg-946fcce07b.zip')) {
        if ($entries -notcontains $required) { throw "Package missing: $required" }
    }
    if ($entries -match '/(\.git|\.deps-cache)/|\.(mp4|mkv|ts)$') { throw 'Private or temporary files in package' }
} finally { $zip.Dispose() }
Write-Output "Package verified: $zipPath"
