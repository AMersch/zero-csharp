# Android's loader and C library, for running Android programs on Linux

`./test.sh --android` runs the Android programs on a phone or emulator
through adb. With `ANDROID_RUN=container`, it runs them in an arm64 Linux
container instead, the way CI does, on its arm64 Linux runner. An Android
program needs only these files from Android to start there: the paths are
the ones it names (`/system/bin/linker64`) or its loader looks in.

| File | What it is |
|---|---|
| `system/bin/linker64` | Android's dynamic loader |
| `system/lib64/libc.so`, `libm.so`, `libdl.so` | Bionic, Android's C library |
| `system/etc/hosts` | `localhost`, for Bionic's own name lookup (on a phone, a system service answers instead) |
| `linkerconfig/ld.config.txt` | empty: without it the loader warns on every run |

The four binaries are unchanged copies from Android 15's arm64 emulator image
(`google/sdk_gphone64_arm64/emu64a:15/AE3A.240806.036/12592187:user/release-keys`),
taken from its runtime module (`/apex/com.android.runtime/`) with
`adb pull`:

```
0352ddba8142758a6b0b92e19a8a061dfabbf8a7cac5865bd34654fad8db9fe8  system/bin/linker64
e816c59b415d679a6f21011cd52fdad7340d73f188a6f7ed3e160ab3005d44a1  system/lib64/libc.so
4841d2c7d1959019ccbdb252a9b844bd874f7c9c53003189353b18cd69bef747  system/lib64/libdl.so
9cc799be57e74ba89f62cc264389d88db99f14f2444ff600fb2ea3804f1747c0  system/lib64/libm.so
```

They're built from the Android Open Source Project's bionic, under the
licenses in `NOTICE/` (bionic's own NOTICE files for `android15-release`:
BSD-style licenses and the Apache License 2.0).

To refresh them from another emulator or phone (arm64, with adb):

```
for f in bin/linker64 lib64/bionic/libc.so lib64/bionic/libm.so lib64/bionic/libdl.so; do
    adb pull "/apex/com.android.runtime/$f" .
done
```

then move them to `system/bin/` and `system/lib64/`, and update the build and
the hashes here.
