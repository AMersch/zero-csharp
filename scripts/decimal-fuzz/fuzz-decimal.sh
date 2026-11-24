#!/usr/bin/env bash
# Checks zerocore's decimal against .NET's: builds zerocore/System/Decimal.cs,
# renamed ZeroTest.ZDecimal, into an ordinary .NET program with Program.cs,
# which runs every operation on random and edge values with both and compares
# the bits (or the exception type).
#
# Usage:  scripts/decimal-fuzz/fuzz-decimal.sh [iterations] [seed]
#         (default 200000 iterations, about 4 million checks; seed 12345)
# Needs:  the .NET 10 SDK (or newer).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp "$here/Program.cs" "$work/"
# The type under another name, so it doesn't clash with .NET's decimal.
perl -pe 'print "using System;\n" if $. == 1; s/^namespace System$/namespace ZeroTest/; s/\bDecimal\b/ZDecimal/g; s/\(decimal d, /(ZDecimal d, /' \
    "$root/zerocore/System/Decimal.cs" > "$work/ZDecimal.cs"
cat > "$work/Number.cs" <<'EOF'
namespace ZeroTest
{
    // zerocore's helper, the .NET way.
    internal static unsafe class Number
    {
        internal static string FromChars(char* chars, int length) => new string(chars, 0, length);
    }
}
EOF
cat > "$work/fuzz.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF
dotnet run --project "$work/fuzz.csproj" -c Release -- "${1:-200000}" "${2:-12345}"
