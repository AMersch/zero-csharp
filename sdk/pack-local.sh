#!/usr/bin/env bash
# Packs the SDK and the templates of this checkout into sdk/feed/, for projects
# on this machine, under a version of their own so that NuGet doesn't take the
# published ones for them:
#   ZeroCSharp.Sdk          with this checkout's payload (./build.sh --sdk-payload)
#   ZeroCSharp.Templates    dotnet new zero and the others
#   ZeroCSharp.Bflat.<rid>  the published package of this machine, under the
#                           same version (for a checkout that doesn't change bflat)
# sdk/pack.sh packs what gets published: it needs bflat's full layouts for
# every host, which this doesn't.
#
# Usage:  sdk/pack-local.sh
# Env:    ZERO_LOCAL_VERSION  default: the version in sdk/version.props, with "-local"
#
# What build.sh needs is needed (BFLAT_DIR, Docker on a Mac), and a clang for
# x86 Windows objects (Xcode's does). Packing again under the same version
# removes that version from NuGet's package cache, so projects get the new one.
set -euo pipefail
cd "$(dirname "$0")/.."
base=$(sed -n 's|.*<ZeroSdkVersion>\(.*\)</ZeroSdkVersion>.*|\1|p' sdk/version.props)
version=${ZERO_LOCAL_VERSION:-$base-local}
case "$(uname -s)-$(uname -m)" in
    Darwin-arm64)   rid=osx-arm64 ;;
    Linux-x86_64)   rid=linux-x64 ;;
    Linux-aarch64)  rid=linux-arm64 ;;
    *) echo "Error: no local packages for $(uname -s) $(uname -m)." >&2; exit 1 ;;
esac

payload=sdk/ZeroCSharp.Sdk/payload
./build.sh --sdk-payload "$payload"

# A copy of the two packages' folders with the local version in version.props,
# which the SDK package carries along. The folder is as deep as sdk/, since the
# projects name files by relative paths (../../zerocore).
stage=obj-local
rm -rf "$stage"
mkdir -p "$stage" sdk/feed
cp -R sdk/ZeroCSharp.Sdk sdk/ZeroCSharp.Templates sdk/version.props sdk/Directory.Build.props "$stage/"
rm -rf "$stage"/ZeroCSharp.*/obj "$stage"/ZeroCSharp.*/bin
sed "s|<ZeroSdkVersion>.*</ZeroSdkVersion>|<ZeroSdkVersion>$version</ZeroSdkVersion>|" sdk/version.props > "$stage/version.props"
rm -f sdk/feed/*".$version.nupkg"

dotnet pack "$stage/ZeroCSharp.Sdk/ZeroCSharp.Sdk.csproj" -o sdk/feed -v:q -nologo
mkdir -p "$stage/ZeroCSharp.Templates/obj/zerollama"
{ cat sdk/ZeroCSharp.Templates/zerollama-header.txt; sed -n '/^using System;/,$p' samples/Llama.cs; } \
    > "$stage/ZeroCSharp.Templates/obj/zerollama/Program.cs"
dotnet pack "$stage/ZeroCSharp.Templates/ZeroCSharp.Templates.csproj" -o sdk/feed -v:q -nologo

published=sdk/feed/published-bflat.$rid.$base.zip
if [ ! -f "$published" ]; then
    id=zerocsharp.bflat.$rid
    echo "Downloading the published ZeroCSharp.Bflat.$rid $base..."
    curl -fsSL -o "$published.part" "https://api.nuget.org/v3-flatcontainer/$id/$base/$id.$base.nupkg"
    mv "$published.part" "$published"
fi
# The same files under the local version. The publisher's signature goes: it
# signs the package as it was.
python3 - "$published" "sdk/feed/ZeroCSharp.Bflat.$rid.$version.nupkg" "$base" "$version" <<'PY'
import sys
import zipfile

source, target, base, version = sys.argv[1:]
with zipfile.ZipFile(source) as old, zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as new:
    for entry in old.infolist():
        if entry.filename == ".signature.p7s":
            continue
        data = old.read(entry)
        if entry.filename.endswith(".nuspec"):
            data = data.replace(f"<version>{base}</version>".encode(), f"<version>{version}</version>".encode())
        new.writestr(entry, data)
PY

cache=${NUGET_PACKAGES:-$HOME/.nuget/packages}
for package in zerocsharp.sdk zerocsharp.templates "zerocsharp.bflat.$rid"; do
    rm -rf "${cache:?}/$package/$(printf %s "$version" | tr '[:upper:]' '[:lower:]')"
done

cat <<TEXT

Packed $version into $PWD/sdk/feed. To use it:
  dotnet new uninstall ZeroCSharp.Templates    # the published ones, if installed: both would clash
  dotnet new install "$PWD/sdk/feed/ZeroCSharp.Templates.$version.nupkg"
  dotnet new zero -o Hello && cd Hello
  dotnet new nugetconfig && dotnet nuget add source "$PWD/sdk/feed" -n zero-local --configfile nuget.config
  dotnet build -p:ZeroTarget=cx16
TEXT
