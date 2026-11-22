#!/usr/bin/env bash
# Makes a USB stick that boots a UEFI program on a real PC: erases the stick,
# formats it FAT32, and copies the program to where firmware looks for one on
# removable media, \EFI\BOOT\BOOTX64.EFI (x64) or \EFI\BOOT\BOOTAA64.EFI
# (arm64). Secure Boot must be off on the PC.
#
# Usage: scripts/uefi-usb.sh <disk> <program.efi>
#   macOS: the disk is /dev/diskN (see `diskutil list external`)
#   Linux: the disk is /dev/sdX (see `lsblk`); needs mkfs.vfat and sudo
#
# Everything on the disk is lost. The script only accepts an external disk
# and asks before erasing it.
set -euo pipefail

if [ $# -ne 2 ]; then
    echo "Usage: scripts/uefi-usb.sh <disk> <program.efi>" >&2
    exit 1
fi
disk=$1
program=$2
if [ ! -f "$program" ]; then
    echo "Error: no $program. Build it with ./build.sh --uefi" >&2
    exit 1
fi

# The PE header's machine field names the file the firmware looks for.
boot_name=$(python3 -c '
import struct, sys
data = open(sys.argv[1], "rb").read(4096)
pe = struct.unpack_from("<I", data, 0x3C)[0]
print({0x8664: "BOOTX64.EFI", 0xAA64: "BOOTAA64.EFI"}.get(struct.unpack_from("<H", data, pe + 4)[0], ""))' "$program")
if [ -z "$boot_name" ]; then
    echo "Error: $program is neither an x64 nor an arm64 UEFI program" >&2
    exit 1
fi

confirm() {
    echo "$1"
    printf 'Erase it? Type yes: '
    read -r answer
    [ "$answer" = yes ] || { echo "Nothing was changed."; exit 1; }
}

case "$(uname -s)" in
    Darwin)
        info=$(diskutil info "$disk") || { echo "Error: no disk $disk" >&2; exit 1; }
        if ! grep -Eq 'Device Location: +External|Removable Media: +Removable' <<< "$info" \
            || grep -Eq 'Internal: +Yes' <<< "$info"; then
            echo "Error: $disk isn't an external disk; refusing to erase it." >&2
            exit 1
        fi
        confirm "$(grep -E 'Device / Media Name|Disk Size' <<< "$info")"
        diskutil eraseDisk FAT32 ZEROUEFI MBRFormat "$disk"
        volume=/Volumes/ZEROUEFI
        mkdir -p "$volume/EFI/BOOT"
        cp "$program" "$volume/EFI/BOOT/$boot_name"
        diskutil eject "$disk"
        ;;
    Linux)
        name=$(basename "$disk")
        if [ "$(cat "/sys/block/$name/removable" 2>/dev/null)" != 1 ]; then
            echo "Error: $disk isn't a removable disk; refusing to erase it." >&2
            exit 1
        fi
        confirm "$(lsblk -d -o NAME,SIZE,MODEL "$disk")"
        for part in "$disk"?*; do sudo umount "$part" 2>/dev/null || true; done
        echo 'type=c, bootable' | sudo sfdisk --label dos "$disk"
        partition=${disk}1
        [ -b "$partition" ] || partition=${disk}p1
        sudo mkfs.vfat -F 32 -n ZEROUEFI "$partition"
        mount=$(mktemp -d)
        sudo mount "$partition" "$mount"
        sudo mkdir -p "$mount/EFI/BOOT"
        sudo cp "$program" "$mount/EFI/BOOT/$boot_name"
        sudo umount "$mount"
        rmdir "$mount"
        ;;
    *)
        echo "Error: macOS or Linux only" >&2
        exit 1
        ;;
esac
echo "Done: \\EFI\\BOOT\\$boot_name. Boot the PC from the stick, with Secure Boot off."
