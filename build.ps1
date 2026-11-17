# Builds the samples with the bflat fork on Windows; mirrors build.sh.
# UNTESTED on Windows: the samples P/Invoke into "libc", which may not
# resolve there. See README.
#
# Unlike build.sh, this still builds against bflat's built-in zerolib
# (--stdlib:zero): zerocore/ has no Windows port yet. So the binaries include
# zerolib's AGPL code, and the Casts sample (which needs zerocore) isn't built.
#
# Usage: .\build.ps1 [Sample ...]     default: all samples
# Env:   BFLAT_DIR (default ..\bflat\layouts\windows-x64)

param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Samples)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$AllSamples = @("Tiny", "Minimal", "Arena", "Collections", "VirtualTest", "Simd", "Kernels", "Strings")
if (-not $Samples) { $Samples = $AllSamples }
foreach ($name in $Samples) {
    if ($name -notin $AllSamples) { throw "Unknown sample: $name (known: $($AllSamples -join ', '))" }
}

$CSharpFlags = @("--langversion", "preview", "--compiler-feature", "updated-memory-safety-rules")

# lib/ is compiled once into an IL assembly that the samples reference with -r
# (see build.sh). Don't name it ZeroLib: that collides with zero mode's zerolib.
$Lib = "bin/BflatLib.dll"

$BflatDir = if ($env:BFLAT_DIR) { $env:BFLAT_DIR } else { "..\bflat\layouts\windows-x64" }
$Bflat = Join-Path $BflatDir "bflat.exe"
if (-not (Test-Path $Bflat)) { throw "No bflat at $BflatDir. Build the fork's layouts (README) or set BFLAT_DIR." }

New-Item -ItemType Directory -Force -Path "bin" | Out-Null

Write-Host "[bflat] lib/*.cs -> $Lib"
& $Bflat build-il @(Get-ChildItem lib\*.cs | ForEach-Object FullName) -O --stdlib:zero @CSharpFlags --target Shared -o $Lib
if ($LASTEXITCODE -ne 0) { throw "Build failed for $Lib" }

foreach ($name in $Samples) {
    Write-Host "[bflat] $name -> bin/$name.exe"
    $flags = switch ($name) {
        "Tiny" { @("-Os") }
        { $_ -in "Simd", "Kernels" } { @("-m", "x86-64-v3") }   # AVX2/FMA baseline; zero mode has no CPU detection
        default { @() }
    }
    & $Bflat build "samples/$name.cs" -r $Lib --stdlib:zero @CSharpFlags @flags -o "bin/$name.exe"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $name" }
}

Get-ChildItem bin\*.exe | Format-Table Name, @{ N = "Size (KB)"; E = { [math]::Round($_.Length / 1KB, 1) } }
