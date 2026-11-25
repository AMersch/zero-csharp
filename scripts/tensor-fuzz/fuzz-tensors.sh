#!/usr/bin/env bash
# Checks zerocore's TensorPrimitives against .NET's (the System.Numerics.Tensors
# package): builds zerocore/System/Numerics/Tensors/TensorPrimitives.cs, moved
# into namespace ZeroTest, into an ordinary .NET program with Program.cs, which
# runs both on random spans of every length up to 70 (covering the vector
# loops and their tails) and on the argument errors, and reports the largest
# difference per operation. On arm64 and AVX2 machines the vector paths run
# with .NET's own intrinsics.
#
# Usage:  scripts/tensor-fuzz/fuzz-tensors.sh [iterations] [seed]
#         (default 20000 iterations; seed 12345)
# Needs:  the .NET 10 SDK (or newer) and nuget.org for the package.
#
# The vector paths that run are the host's: NEON on arm64; AVX on an x64
# with AVX2 and FMA, SSE2 with DOTNET_EnableAVX=0. On an Apple silicon Mac,
# DOTNET=/path/to/x64/dotnet runs the x64 paths under Rosetta.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp "$here/Program.cs" "$work/"
perl -pe 'print "using System;\n" if $. == 1; s/^namespace System\.Numerics\.Tensors$/namespace ZeroTest/' \
    "$root/zerocore/System/Numerics/Tensors/TensorPrimitives.cs" > "$work/TensorPrimitives.cs"
cat > "$work/fuzz.csproj" <<'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <NoWarn>CS1718</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Numerics.Tensors" Version="10.0.0" />
  </ItemGroup>
</Project>
PROJ
"${DOTNET:-dotnet}" run --project "$work/fuzz.csproj" -c Release -- "${1:-20000}" "${2:-12345}"
