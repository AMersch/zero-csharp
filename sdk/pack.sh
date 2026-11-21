#!/usr/bin/env bash
# Packs the Zero C# packages into sdk/feed/.
#
# Usage: sdk/pack.sh
set -euo pipefail
cd "$(dirname "$0")/.."

mkdir -p sdk/feed
./build.sh --sdk-payload sdk/ZeroCSharp.Sdk/payload
rm -f sdk/feed/ZeroCSharp.Sdk.*.nupkg
dotnet pack sdk/ZeroCSharp.Sdk/ZeroCSharp.Sdk.csproj -o sdk/feed -v:q -nologo
