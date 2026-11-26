#!/bin/sh
# Makes sure adb has an Android device to run programs on, and prints adb's
# path. A phone or emulator already connected is used (ANDROID_SERIAL picks
# one of several, as adb does). Otherwise the emulator starts ANDROID_AVD, or
# the first virtual device Android Studio made, without a window; it keeps
# running for later runs (`adb emu kill` stops it). Used by android/run.sh.
#
# Usage: sh android/boot.sh
sdk=${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}
if [ -z "$sdk" ]; then
    for dir in "$HOME/Library/Android/sdk" "$HOME/Android/Sdk"; do
        [ -d "$dir" ] && sdk=$dir && break
    done
fi
adb=$(command -v adb || echo "$sdk/platform-tools/adb")
if [ ! -x "$adb" ]; then
    echo "Error: no adb. Install Android Studio (or the Android SDK's platform-tools), or set ANDROID_HOME." >&2
    exit 1
fi

if [ "$("$adb" get-state 2>/dev/null)" != device ]; then
    emulator=$sdk/emulator/emulator
    avd=${ANDROID_AVD:-$("$emulator" -list-avds 2>/dev/null | head -1)}
    if [ ! -x "$emulator" ] || [ -z "$avd" ]; then
        echo "Error: no Android device. Connect a phone with USB debugging on, or make a virtual device in Android Studio (Device Manager)." >&2
        exit 1
    fi
    echo "Starting the Android emulator ($avd)..." >&2
    nohup "$emulator" -avd "$avd" -no-window -no-audio -no-boot-anim -no-snapshot-save > /dev/null 2>&1 &
    "$adb" wait-for-device
fi
# A device that is still booting has adb but can't run programs yet.
i=0
until [ "$("$adb" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ]; do
    i=$((i + 1))
    if [ "$i" -gt 180 ]; then
        echo "Error: the Android device didn't finish booting in 6 minutes." >&2
        exit 1
    fi
    sleep 2
done
echo "$adb"
