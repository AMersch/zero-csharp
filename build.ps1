# Builds the samples with the bflat fork on Windows; mirrors build.sh.
# UNTESTED on Windows: the samples P/Invoke into "libc", which may not
# resolve there. See README.
#
# Usage: .\build.ps1 [Sample ...]     default: all samples
# Env:   BFLAT_DIR (default ..\bflat\layouts\windows-x64)

param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Samples)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$Sources = [ordered]@{
    Tiny        = @("samples/Tiny.cs")
    Minimal     = @("samples/Minimal.cs")
    Arena       = @("samples/Arena.cs", "lib/Arena.cs", "lib/Out.cs")
    Collections = @("samples/Collections.cs", "lib/Arena.cs", "lib/List.cs", "lib/Dict.cs", "lib/Out.cs")
    VirtualTest = @("samples/VirtualTest.cs", "lib/Out.cs")
    Simd        = @("samples/Simd.cs", "lib/Arena.cs", "lib/Out.cs")
}
if (-not $Samples) { $Samples = @($Sources.Keys) }
foreach ($name in $Samples) {
    if (-not $Sources.Contains($name)) { throw "Unknown sample: $name (known: $($Sources.Keys -join ', '))" }
}

$BflatDir = if ($env:BFLAT_DIR) { $env:BFLAT_DIR } else { "..\bflat\layouts\windows-x64" }
$Bflat = Join-Path $BflatDir "bflat.exe"
if (-not (Test-Path $Bflat)) { throw "No bflat at $BflatDir. Build the fork's layouts (README) or set BFLAT_DIR." }

New-Item -ItemType Directory -Force -Path "bin" | Out-Null

foreach ($name in $Samples) {
    Write-Host "[bflat] $name -> bin/$name.exe"
    $flags = switch ($name) {
        "Tiny" { @("-Os") }
        "Simd" { @("-m", "x86-64-v3") }   # AVX2/FMA baseline; zero mode has no CPU detection
        default { @() }
    }
    & $Bflat build @($Sources[$name]) --stdlib:zero `
        --langversion preview --compiler-feature updated-memory-safety-rules `
        @flags -o "bin/$name.exe"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $name" }
}

Get-ChildItem bin\*.exe | Format-Table Name, @{ N = "Size (KB)"; E = { [math]::Round($_.Length / 1KB, 1) } }
