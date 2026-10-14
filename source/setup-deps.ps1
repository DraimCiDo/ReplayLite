param([switch]$Force)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$cacheRoot = Join-Path $appRoot '.deps-cache'
New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null

function Get-VerifiedArchive {
    param([string]$Uri, [string]$Path, [string]$Sha256)
    if (Test-Path -LiteralPath $Path) {
        if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Sha256) { return }
    }
    Invoke-WebRequest -Uri $Uri -OutFile $Path -UseBasicParsing
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "SHA256 mismatch: $Path"
    }
}

if ($Force -or -not (Test-Path -LiteralPath (Join-Path $appRoot 'NAudio.dll'))) {
    $naudioZip = Join-Path $cacheRoot 'naudio-1.10.0.zip'
    Get-VerifiedArchive -Uri 'https://api.nuget.org/v3-flatcontainer/naudio/1.10.0/naudio.1.10.0.nupkg' -Path $naudioZip -Sha256 'E4A80EED41DAD794695F9C114432DE4667D20BDFA80825AA94D159708CF29217'
    $naudioUnpack = Join-Path $cacheRoot 'naudio-1.10.0'
    Expand-Archive -LiteralPath $naudioZip -DestinationPath $naudioUnpack -Force
    Copy-Item -LiteralPath (Join-Path $naudioUnpack 'lib\net35\NAudio.dll') -Destination (Join-Path $appRoot 'NAudio.dll') -Force
}

$binaryRoot = Join-Path $appRoot 'bin'
if ($Force -or -not (Test-Path -LiteralPath (Join-Path $binaryRoot 'ffmpeg.exe')) -or -not (Test-Path -LiteralPath (Join-Path $binaryRoot 'ffprobe.exe'))) {
    $ffmpegZip = Join-Path $cacheRoot 'ffmpeg-9.0.2.zip'
    Get-VerifiedArchive -Uri 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip' -Path $ffmpegZip -Sha256 '60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA'
    $ffmpegUnpack = Join-Path $cacheRoot 'ffmpeg-9.0.2'
    Expand-Archive -LiteralPath $ffmpegZip -DestinationPath $ffmpegUnpack -Force
    $ffmpegSourceRoot = Join-Path $ffmpegUnpack 'ffmpeg-9.0.2-essentials_build'
    New-Item -ItemType Directory -Path $binaryRoot -Force | Out-Null
    foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
        Copy-Item -LiteralPath (Join-Path $ffmpegSourceRoot ('bin\' + $name)) -Destination (Join-Path $binaryRoot $name) -Force
    }
}
Write-Output 'Pinned dependencies ready: NAudio 1.10.0, FFmpeg 9.0.2'
