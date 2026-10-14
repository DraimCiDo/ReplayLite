$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$appRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $appRoot '.deps-cache'
$tool = Join-Path $cache 'inno-7.1.0'
$compiler = Join-Path $tool 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) {
    if ((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne 'D06EBD38F38E3CEE60A3C50CC45BD449D77E0BC6A5CABC607EA9886808E4DE1A') { throw 'Unexpected Inno Setup compiler SHA256' }
    Write-Output $compiler
    return
}
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$download = Join-Path $cache 'innosetup-7.1.0-x64.exe'
$expected = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
if (-not (Test-Path -LiteralPath $download) -or (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) { throw 'Inno Setup download SHA256 mismatch' }
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Pyrsys B\.V\.') { throw 'Inno Setup publisher signature mismatch' }
# Official portable mode: no file associations, shortcuts, registry registration or PATH changes.
$process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/PORTABLE=1',('/DIR="' + $tool + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Installer toolchain extraction timed out' }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Installer toolchain extraction failed' }
if ((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne 'D06EBD38F38E3CEE60A3C50CC45BD449D77E0BC6A5CABC607EA9886808E4DE1A') { throw 'Extracted Inno Setup compiler SHA256 mismatch' }
Write-Output $compiler
