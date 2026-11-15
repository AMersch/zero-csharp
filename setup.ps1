# Checks that the bflat fork's Windows layout exists; mirrors setup.sh.
# UNTESTED on Windows: see README.
#
# Env: BFLAT_DIR (default ..\bflat\layouts\windows-x64)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$BflatDir = if ($env:BFLAT_DIR) { $env:BFLAT_DIR } else { "..\bflat\layouts\windows-x64" }
if (Test-Path (Join-Path $BflatDir "bflat.exe")) {
    Write-Host "bflat:  $BflatDir"
    Write-Host "Ready. Run .\build.ps1" -ForegroundColor Green
} else {
    Write-Host "Missing: bflat fork layout at $BflatDir" -ForegroundColor Yellow
    Write-Host "         Build it (README, 'Getting bflat') or set BFLAT_DIR."
}
