#!/usr/bin/env bash
# Tests the sound of the Commander X16 target: builds samples/SoundDemo.cs and PcmDemo.cs, runs each in the emulator with a window
# (the emulator makes no sound without one) and its -wav recording, and measures the pitch, the partials, the speaker
# and the volume of every sound in it (cx16/audio-check.py, which needs numpy). The window opens for a few seconds.
# Called by ./test.sh --cx16-audio.
set -euo pipefail
cd "$(dirname "$0")/.."
source ./env.sh

if ! emulator=$(find_x16_emulator); then
    echo "Error: no x16emu. Get it from https://github.com/X16Community/x16-emulator, or set X16_EMULATOR." >&2
    exit 1
fi
if ! python3 -c "import numpy" 2>/dev/null; then
    echo "Error: the audio check needs numpy (pip install numpy)." >&2
    exit 1
fi
work=$PWD/bin/cx16/run
mkdir -p "$work"
status=0
for demo in SoundDemo PcmDemo; do
    ./build.sh --cx16 $demo > /dev/null
    rm -f "$work/sound.wav"
    options=(-prg bin/cx16/$demo.prg -run -rtc -warp -fsroot bin/cx16 -wav "$work/sound.wav")
    [ -n "${X16_ROM:-}" ] && options+=(-rom "$X16_ROM")
    python3 -I cx16/watch.py "${X16_TIMEOUT:-120}" "$emulator" "${options[@]}" > /dev/null
    echo "$demo:"
    if [ $demo = PcmDemo ]; then
        python3 cx16/audio-check.py "$work/sound.wav" pcm || status=1
    else
        python3 cx16/audio-check.py "$work/sound.wav" || status=1
    fi
done
exit $status
