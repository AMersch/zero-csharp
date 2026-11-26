#!/bin/sh
# Picks an iOS simulator, boots it if it isn't, and prints its UDID: the one
# named (a name or UDID) if given, else one already booted, else the first
# available iPhone. It stays booted for later runs; `xcrun simctl shutdown all`
# stops it. Used by ios/run.sh, and by the SDK's dotnet run (a copy in its
# payload).
#
# Usage: sh ios/boot.sh [simulator]
device=${1:-}
if [ -z "$device" ]; then
    device=$(xcrun simctl list devices booted | grep -Eo '\([0-9A-F-]{36}\)' | head -1 | tr -d '()')
fi
if [ -z "$device" ]; then
    device=$(xcrun simctl list devices available | grep -E '^ +iPhone' | grep -Eo '\([0-9A-F-]{36}\)' | head -1 | tr -d '()')
fi
if [ -z "$device" ]; then
    echo "Error: no iPhone simulator. Install Xcode's iOS platform (Xcode > Settings > Components), or add a simulator in Xcode (Window > Devices and Simulators)." >&2
    exit 1
fi
# bootstatus -b boots it if needed, and waits until it can start programs.
xcrun simctl bootstatus "$device" -b > /dev/null || exit 1
echo "$device"
