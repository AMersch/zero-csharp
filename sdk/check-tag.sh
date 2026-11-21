#!/usr/bin/env bash
# Checks that a release tag names the version the packages are packed with
# (sdk/version.props), so sdk-v0.1.2 can't publish packages that say 0.1.1.
#
# Usage:  sdk/check-tag.sh <tag>        e.g. sdk/check-tag.sh sdk-v0.1.1
set -euo pipefail
cd "$(dirname "$0")/.."
tag=${1:?tag}
version=$(sed -n 's|.*<ZeroSdkVersion>\(.*\)</ZeroSdkVersion>.*|\1|p' sdk/version.props)
if [ "$tag" != "sdk-v$version" ]; then
    echo "Error: the tag is $tag, but sdk/version.props says $version (tag it sdk-v$version)." >&2
    exit 1
fi
echo "$tag matches sdk/version.props."
