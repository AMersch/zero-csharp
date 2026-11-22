#!/usr/bin/env bash
# Checks that a release tag names the version the packages are packed with
# (sdk/version.props), so sdk-v0.1.2 can't publish packages that say 0.1.1;
# and that bflat comes from the fork's master (BFLAT_REF, set in sdk.yml): a
# branch commit can vanish, and the AGPL packages must point to their source.
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
if [ -n "${BFLAT_REF:-}" ] && [ "$BFLAT_REF" != master ]; then
    echo "Error: bflat comes from the fork's $BFLAT_REF branch (BFLAT_REF in sdk.yml); a release needs master." >&2
    exit 1
fi
echo "$tag matches sdk/version.props."
