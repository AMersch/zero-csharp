#!/usr/bin/env bash
# Checks zerocore's Math additions, Complex, vectors, matrices, Quaternion and
# Plane against .NET's: builds those sources, moved into namespace ZeroTest
# (with Platform's C math functions mapped to .NET's), into an ordinary .NET
# program with Program.cs, which runs both on random and edge values and
# reports the largest difference per operation. This tests the algorithms;
# ./test.sh runs samples/Numerics.cs on each target's own math library.
#
# Usage:  scripts/numerics-fuzz/fuzz-numerics.sh [iterations] [seed]
#         (default 200000 iterations; seed 12345)
# Needs:  the .NET 10 SDK (or newer).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp "$here/Program.cs" "$work/"
for source in "$root/zerocore/System/Math.cs" "$root"/zerocore/System/Numerics/{Complex,Vector2,Vector3,Vector4,Matrix3x2,Matrix4x4,Quaternion,Plane}.cs; do
    # [Intrinsic] is internal to .NET's library; the namespace keeps the
    # types apart from .NET's own.
    perl -pe 'print "using System;\n" if $. == 1; s/^namespace System(\.Numerics)?$/namespace ZeroTest/; s/^\s*\[Intrinsic\]\s*$//' \
        "$source" > "$work/$(basename "$source")"
done
cat > "$work/Platform.cs" <<'SHIM'
namespace ZeroTest
{
    // zerocore's imports from the C math library, the .NET way.
    internal static class Platform
    {
        internal static double fabs(double x) => System.Math.Abs(x);
        internal static double sqrt(double x) => System.Math.Sqrt(x);
        internal static double floor(double x) => System.Math.Floor(x);
        internal static double ceil(double x) => System.Math.Ceiling(x);
        internal static double trunc(double x) => System.Math.Truncate(x);
        internal static double rint(double x) => System.Math.Round(x);
        internal static double exp(double x) => System.Math.Exp(x);
        internal static double log(double x) => System.Math.Log(x);
        internal static double log2(double x) => System.Math.Log2(x);
        internal static double log10(double x) => System.Math.Log10(x);
        internal static double sin(double x) => System.Math.Sin(x);
        internal static double cos(double x) => System.Math.Cos(x);
        internal static double tan(double x) => System.Math.Tan(x);
        internal static double pow(double x, double y) => System.Math.Pow(x, y);
        internal static double atan2(double y, double x) => System.Math.Atan2(y, x);
        internal static float fabsf(float x) => System.MathF.Abs(x);
        internal static float sqrtf(float x) => System.MathF.Sqrt(x);
        internal static float floorf(float x) => System.MathF.Floor(x);
        internal static float ceilf(float x) => System.MathF.Ceiling(x);
        internal static float truncf(float x) => System.MathF.Truncate(x);
        internal static float rintf(float x) => System.MathF.Round(x);
        internal static float expf(float x) => System.MathF.Exp(x);
        internal static float logf(float x) => System.MathF.Log(x);
        internal static float sinf(float x) => System.MathF.Sin(x);
        internal static float cosf(float x) => System.MathF.Cos(x);
        internal static float tanhf(float x) => System.MathF.Tanh(x);
        internal static float powf(float x, float y) => System.MathF.Pow(x, y);
    }
}
SHIM
cat > "$work/fuzz.csproj" <<'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <NoWarn>CS1718;CS0660;CS0661</NoWarn>
  </PropertyGroup>
</Project>
PROJ
dotnet run --project "$work/fuzz.csproj" -c Release -- "${1:-200000}" "${2:-12345}"
