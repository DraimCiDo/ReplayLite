param([Parameter(Mandatory=$true)][string]$InputDirectory, [Parameter(Mandatory=$true)][string]$TargetDirectory, [Parameter(Mandatory=$true)][string[]]$Names)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$version = [IO.File]::ReadAllText((Join-Path $appRoot 'VERSION')).Trim() + '.0'
$inputs = @()
foreach ($name in $Names) {
    if ($name -notin @('ReplayLite.exe','UpdateHelper.exe','Uninstall.exe','ReplayLite-Setup.exe')) { throw 'Unexpected signing target' }
    $matches = @(Get-ChildItem -LiteralPath $InputDirectory -Recurse -File | Where-Object { $_.Name -eq $name })
    if ($matches.Count -ne 1) { throw "Expected exactly one signed $name" }
    $file = $matches[0].FullName
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or -not $signature.TimeStamperCertificate) { throw "Trusted timestamped Authenticode signature required: $name" }
    if ($signature.SignerCertificate.Subject -eq $signature.SignerCertificate.Issuer) { throw "Self-signed certificate rejected: $name" }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($file).FileVersion.Trim() -ne $version) { throw "Signed file version mismatch: $name" }
    $inputs += @{ Source=$file; Target=(Join-Path $TargetDirectory $name) }
}
# Validate every result before replacing any local build output. Trust comes from the configured signing policy.
foreach ($inputFile in $inputs) { Copy-Item -LiteralPath $inputFile.Source -Destination $inputFile.Target -Force }
Write-Output ('Trusted signatures verified: ' + ($Names -join ', '))
