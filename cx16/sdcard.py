#!/usr/bin/env python3
"""An SD card image for x16emu's -sdcard, with files on it: sdcard.py SOURCE OUT.img [FILE...]

SOURCE is an empty card as the emulator's release has it (sdcard.img.zip, or the .img in it): a
partition table and one FAT32 partition. OUT.img is a copy of it with the FILEs put into its root
folder, under their own names. Only as much of FAT32 as that needs."""
import struct
import sys
import zipfile
from pathlib import Path

END = 0x0FFFFFFF


def add(card, files):
    start = struct.unpack_from("<I", card, 0x1BE + 8)[0] * 512
    sector, per_cluster, reserved, fats = struct.unpack_from("<HBHB", card, start + 11)
    per_fat, root = struct.unpack_from("<I", card, start + 36)[0], struct.unpack_from("<I", card, start + 44)[0]
    fat = start + reserved * sector
    data = fat + fats * per_fat * sector
    size = per_cluster * sector

    def entry(cluster):
        return struct.unpack_from("<I", card, fat + cluster * 4)[0] & END

    def chain(cluster, value):
        for i in range(fats):
            struct.pack_into("<I", card, fat + i * per_fat * sector + cluster * 4, value)

    def free():
        for cluster in range(2, per_fat * sector // 4):
            if entry(cluster) == 0:
                chain(cluster, END)
                return cluster
        sys.exit("Error: the card is full.")

    def at(cluster):
        return data + (cluster - 2) * size

    def slots(count):
        """count free directory entries in a row, in the root folder."""
        cluster, run = root, []
        while True:
            for offset in range(at(cluster), at(cluster) + size, 32):
                run = run + [offset] if card[offset] in (0, 0xE5) else []
                if len(run) == count:
                    return run
            following = entry(cluster)
            if following >= 0x0FFFFFF8:
                following = free()
                chain(cluster, following)
                card[at(following):at(following) + size] = bytes(size)
            cluster = following

    for number, path in enumerate(files, 1):
        content = Path(path).read_bytes()
        name = Path(path).name
        first = previous = 0
        for offset in range(0, len(content), size):
            cluster = free()
            if previous:
                chain(previous, cluster)
            first = first or cluster
            previous = cluster
            part = content[offset:offset + size]
            card[at(cluster):at(cluster) + size] = part + bytes(size - len(part))

        # A long name's entries, last part first, then the 8.3 entry they belong to.
        stem, _, extension = name.upper().rpartition(".")
        alias = ("".join(c for c in stem if c.isalnum())[:6] + "~" + str(number % 10)).ljust(8) + extension[:3].ljust(3)
        short = alias.encode("ascii")
        checksum = 0
        for byte in short:
            checksum = (((checksum & 1) << 7) + (checksum >> 1) + byte) & 0xFF
        units = [ord(c) for c in name] + [0]
        units += [0xFFFF] * (-len(units) % 13)
        parts = len(units) // 13
        offsets = slots(parts + 1)
        for i, offset in enumerate(offsets[:-1]):
            part = parts - i
            chunk = units[(part - 1) * 13:part * 13]
            record = bytearray(32)
            record[0] = part | (0x40 if i == 0 else 0)
            record[11], record[13] = 0x0F, checksum
            for k, place in enumerate((1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30)):
                struct.pack_into("<H", record, place, chunk[k])
            card[offset:offset + 32] = record
        record = bytearray(32)
        record[:11] = short
        record[11] = 0x20
        struct.pack_into("<H", record, 20, first >> 16)
        struct.pack_into("<H", record, 26, first & 0xFFFF)
        struct.pack_into("<I", record, 28, len(content))
        card[offsets[-1]:offsets[-1] + 32] = record

    # The count of free clusters is no longer right: say it isn't known.
    information = start + struct.unpack_from("<H", card, start + 48)[0] * sector
    if card[information:information + 4] == b"RRaA":
        struct.pack_into("<II", card, information + 488, 0xFFFFFFFF, 0xFFFFFFFF)


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    source, output, files = sys.argv[1], sys.argv[2], sys.argv[3:]
    if zipfile.is_zipfile(source):
        with zipfile.ZipFile(source) as archive:
            inside = next((n for n in archive.namelist() if n.lower().endswith(".img")), None)
            if inside is None:
                sys.exit("Error: no .img in " + source)
            card = bytearray(archive.read(inside))
    else:
        card = bytearray(Path(source).read_bytes())
    if files:
        add(card, files)
    Path(output).write_bytes(card)


if __name__ == "__main__":
    main()
