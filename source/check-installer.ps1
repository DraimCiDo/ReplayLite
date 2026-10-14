$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$temp = Join-Path ([IO.Path]::GetTempPath()) ('ReplayLite-installer-test-' + [Guid]::NewGuid().ToString('N'))
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ReplayLite-DeploymentTest'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'ReplayLite-DeploymentTest.lnk'
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $shortcut)) { throw 'Installer test registration already exists; refusing to overwrite' }
New-Item -ItemType Directory -Path $temp -Force | Out-Null
function Assert-Check([bool]$Value, [string]$Name) { if (-not $Value) { throw $Name }; Write-Output "$($Name): PASS" }
function Run-Setup([string]$Exe, [string]$Mode, [string]$Root, [bool]$ExpectSuccess = $true) {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/TASKS=',('/MODE=' + $Mode),('/DIR="' + $Root + '"'),('/LOG="' + $Root + '.log"'))
    $process = Start-Process -FilePath $Exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Installer check timed out' }
    Assert-Check (($process.ExitCode -eq 0) -eq $ExpectSuccess) ('SETUP_EXIT_' + $Mode + '_' + $ExpectSuccess)
}
try {
    $setup = Join-Path $appRoot 'dist\ReplayLite-Setup.exe'
    $version = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim()
    Assert-Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).FileVersion.Trim() -eq "$version.0") 'SETUP_VERSION'
    # Read section headers only: payload must not live in a managed code section.
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($setup))
    try {
        $reader.BaseStream.Position = 0x3c; $pe = $reader.ReadInt32()
        $reader.BaseStream.Position = $pe + 6; $sections = $reader.ReadUInt16()
        $reader.BaseStream.Position = $pe + 20; $optionalSize = $reader.ReadUInt16()
        $reader.BaseStream.Position = $pe + 24; $magic = $reader.ReadUInt16()
        $reader.BaseStream.Position = $pe + 24 + $(if ($magic -eq 0x20b) { 112 } else { 96 }) + 14 * 8
        Assert-Check ($reader.ReadUInt32() -eq 0) 'SETUP_NATIVE_NO_CLR'
        for ($i = 0; $i -lt $sections; $i++) {
            $reader.BaseStream.Position = $pe + 24 + $optionalSize + 40 * $i
            $name = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).Trim([char]0)
            $size = $reader.ReadUInt32()
            if ($name -eq '.text' -or $name -eq 'CODE') { Assert-Check ($size -lt 4MB) 'SETUP_CODE_WITHOUT_PAYLOAD' }
        }
    } finally { $reader.Dispose() }
    $package = Join-Path $temp 'package'
    Expand-Archive -LiteralPath (Join-Path $appRoot 'dist\ReplayLite-Portable.zip') -DestinationPath $package
    $stage = Join-Path $package 'ReplayLite'
    $portable = Join-Path $temp 'portable'
    Run-Setup $setup 'portable' $portable
    Assert-Check ((Test-Path -LiteralPath (Join-Path $portable 'portable.flag')) -and -not (Test-Path -LiteralPath (Join-Path $portable 'installed.flag'))) 'REAL_INSTALLER_PORTABLE'
    # Compare every distributed file, including unchanged third-party binaries.
    foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $portable $relative)).Hash) { throw "Installed file mismatch: $relative" }
    }
    Write-Output 'INSTALLED_FILES_MATCH_PORTABLE_ZIP: PASS'
    [IO.File]::WriteAllText((Join-Path $portable 'keep.txt'), 'sentinel')
    Run-Setup $setup 'portable' $portable $false
    Assert-Check ([IO.File]::ReadAllText((Join-Path $portable 'keep.txt')) -eq 'sentinel') 'NONEMPTY_INSTALL_REJECTED'
    # Separate build uses a test registry key and shortcut name, never the user's registration.
    [IO.File]::WriteAllText((Join-Path $stage 'installed.flag'), 'ReplayLite', [Text.UTF8Encoding]::new($false))
    $manifest = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { $_.FullName.Substring($stage.Length + 1) }) + 'package-files.txt'
    [IO.File]::WriteAllLines((Join-Path $stage 'package-files.txt'), $manifest, [Text.UTF8Encoding]::new($false))
    $compiler = & (Join-Path $PSScriptRoot 'setup-installer-toolchain.ps1')
    & $compiler --quiet --no-ide-signtools "--define=PackageRoot=$stage" "--define=OutputRoot=$temp" "--define=AppVersion=$version" --define=TestBuild=1 (Join-Path $PSScriptRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Test installer compiler failed' }
    $full = Join-Path $temp 'full'
    Run-Setup (Join-Path $temp 'ReplayLite-Setup.exe') 'installed' $full
    Assert-Check ((Test-Path -LiteralPath (Join-Path $full 'installed.flag')) -and -not (Test-Path -LiteralPath (Join-Path $full 'portable.flag'))) 'REAL_INSTALLER_FULL'
    Assert-Check ((Get-ItemProperty -LiteralPath $registry).InstallLocation -eq $full) 'FULL_PER_USER_REGISTRATION'
    Assert-Check ((Test-Path -LiteralPath $shortcut) -and (Test-Path -LiteralPath (Join-Path $full 'package-files.txt'))) 'FULL_SHORTCUT_AND_UNINSTALL_MANIFEST'
    Assert-Check (-not (Get-Process -Name ReplayLite -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$temp\*" })) 'SILENT_INSTALL_NO_APP_LAUNCH'
    Write-Output 'INSTALLER_CHECKS_PASSED'
} finally {
    if (Test-Path -LiteralPath $registry) { Remove-Item -LiteralPath $registry -Recurse -Force }
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    $fullTemp = [IO.Path]::GetFullPath($temp)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\ReplayLite-installer-test-'
    if (-not $fullTemp.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe installer test cleanup path' }
    Remove-Item -LiteralPath $fullTemp -Recurse -Force
}
