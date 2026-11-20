#!/usr/bin/env python3
"""Reads and writes files on FAT16 hard disk images, for ../run.sh --dos.

An emulated DOS machine gets its files on a disk image. This does the little
that takes, without mounting anything or needing mtools:

  fat16.py make IMAGE FILE[=NAME]...   create a 16 MB disk with these files
  fat16.py read IMAGE NAME             write a file's content to stdout
                                       (NAME may be a path: DIR/SUBDIR/FILE)
  fat16.py replace IMAGE NAME FILE     overwrite a file's content in place

8.3 names only, and `make` and `replace` work in the root directory only.
`replace` needs the new content to fit in the clusters the file already has;
it is how setup.sh swaps FreeDOS's startup file for one that runs the program
instead of the installer.
"""
import struct
import sys

SECTOR = 512
HEADS, SECTORS_PER_TRACK = 16, 63   # the geometry emulators assume for small disks


def fail(message):
    sys.exit(f"fat16: {message}")


def short_name(name):
    base, _, ext = name.upper().partition(".")
    if not 1 <= len(base) <= 8 or len(ext) > 3 or "." in ext:
        fail(f"'{name}' is not an 8.3 file name")
    return base.ljust(8).encode("ascii") + ext.ljust(3).encode("ascii")


# ---- an existing volume --------------------------------------------------------


class Volume:
    def __init__(self, path, writable=False):
        self.f = open(path, "r+b" if writable else "rb")
        self.start = 0
        first = self.sector(0)
        # A hard disk image starts with a partition table; use its first FAT
        # partition. (An image without one starts with the volume itself.)
        kind = first[446 + 4]
        if first[510:512] == b"\x55\xAA" and kind in (0x01, 0x04, 0x06, 0x0E) and first[54:59] != b"FAT16":
            self.start = struct.unpack_from("<I", first, 446 + 8)[0]
        boot = self.sector(0)
        (bytes_per_sector, self.cluster_sectors, reserved, fats, self.root_entries,
         total16, _, fat_sectors) = struct.unpack_from("<HBHBHHBH", boot, 11)
        total = total16 or struct.unpack_from("<I", boot, 32)[0]
        if bytes_per_sector != SECTOR or fat_sectors == 0:
            fail("not a FAT16 volume")
        self.fat_start = reserved
        self.fat_sectors, self.fats = fat_sectors, fats
        self.root_start = reserved + fats * fat_sectors
        self.data_start = self.root_start + (self.root_entries * 32 + SECTOR - 1) // SECTOR
        clusters = (total - self.data_start) // self.cluster_sectors
        if not 4085 <= clusters < 65525:
            fail("not a FAT16 volume (FAT12 and FAT32 aren't supported)")
        self.cluster_bytes = self.cluster_sectors * SECTOR

    def sector(self, number, count=1):
        self.f.seek((self.start + number) * SECTOR)
        return self.f.read(count * SECTOR)

    def write_at(self, number, data):
        self.f.seek((self.start + number) * SECTOR)
        self.f.write(data)

    # Looks a name up in a directory's bytes. Returns (index of the entry,
    # first cluster, size) or None.
    @staticmethod
    def lookup(directory, name, want_directory):
        wanted = short_name(name)
        for index in range(len(directory) // 32):
            entry = directory[index * 32:index * 32 + 32]
            if entry[0] == 0:
                break
            if entry[0] == 0xE5 or entry[11] & 0x0F == 0x0F or entry[11] & 0x08:
                continue  # deleted, a long-name part or a label
            if entry[:11] == wanted and bool(entry[11] & 0x10) == want_directory:
                cluster, size = struct.unpack_from("<HI", entry, 26)
                return index, cluster, size
        return None

    def cluster_data(self, cluster):
        return b"".join(self.sector(self.cluster_sector(c), self.cluster_sectors) for c in self.chain(cluster))

    # Returns (index of the directory entry, first cluster, size). The index
    # is only meaningful for a file in the root directory.
    def find(self, name):
        directory = self.sector(self.root_start, self.data_start - self.root_start)
        parts = name.replace("\\", "/").strip("/").split("/")
        for part in parts[:-1]:
            found = self.lookup(directory, part, True)
            if found is None:
                fail(f"no directory '{part}' on the way to '{name}'")
            directory = self.cluster_data(found[1])
        found = self.lookup(directory, parts[-1], False)
        if found is None:
            fail(f"no file '{name}'")
        return found

    def chain(self, cluster):
        fat = self.sector(self.fat_start, self.fat_sectors)
        clusters = []
        while 2 <= cluster < 0xFFF0:
            if len(clusters) > 65536:
                fail("the file's cluster chain loops")
            clusters.append(cluster)
            cluster = struct.unpack_from("<H", fat, cluster * 2)[0]
        return clusters

    def cluster_sector(self, cluster):
        return self.data_start + (cluster - 2) * self.cluster_sectors

    def read(self, name):
        _, cluster, size = self.find(name)
        return self.cluster_data(cluster)[:size]

    def replace(self, name, content):
        if "/" in name or "\\" in name:
            fail("replace only works in the root directory")
        index, cluster, _ = self.find(name)
        clusters = self.chain(cluster)
        room = len(clusters) * self.cluster_bytes
        if len(content) > room:
            fail(f"'{name}' has room for {room} bytes; the new content is {len(content)}")
        padded = content.ljust(room, b"\0")
        for i, c in enumerate(clusters):
            self.write_at(self.cluster_sector(c), padded[i * self.cluster_bytes:(i + 1) * self.cluster_bytes])
        # The size field: byte 28 of the file's 32-byte directory entry.
        position = (self.start + self.root_start) * SECTOR + index * 32 + 28
        self.f.seek(position)
        self.f.write(struct.pack("<I", len(content)))


# ---- a new disk ----------------------------------------------------------------


def chs(lba):
    cylinder, rest = divmod(lba, HEADS * SECTORS_PER_TRACK)
    head, sector = divmod(rest, SECTORS_PER_TRACK)
    return bytes([head, (sector + 1) | ((cylinder >> 8) << 6), cylinder & 0xFF])


def make(path, files):
    cylinders = 32                                   # 32 * 16 * 63 sectors: 15.75 MB
    disk_sectors = cylinders * HEADS * SECTORS_PER_TRACK
    start = SECTORS_PER_TRACK                        # the partition starts on track 1
    total = disk_sectors - start
    cluster_sectors, reserved, fats, root_entries = 4, 1, 2, 512
    root_sectors = root_entries * 32 // SECTOR
    fat_sectors = 1
    while True:                                      # the FAT must cover every cluster
        clusters = (total - reserved - root_sectors - fats * fat_sectors) // cluster_sectors
        needed = ((clusters + 2) * 2 + SECTOR - 1) // SECTOR
        if needed <= fat_sectors:
            break
        fat_sectors = needed
    cluster_bytes = cluster_sectors * SECTOR

    boot = bytearray(SECTOR)
    boot[0:3] = b"\xEB\x3C\x90"
    boot[3:11] = b"MSDOS5.0"
    struct.pack_into("<HBHBHHBHHHII", boot, 11, SECTOR, cluster_sectors, reserved, fats, root_entries,
                     total, 0xF8, fat_sectors, SECTORS_PER_TRACK, HEADS, start, 0)
    boot[36] = 0x80
    boot[38] = 0x29
    boot[39:43] = b"\x5A\x45\x52\x4F"
    boot[43:54] = b"PROGRAM    "
    boot[54:62] = b"FAT16   "
    boot[510:512] = b"\x55\xAA"

    fat = bytearray(fat_sectors * SECTOR)
    struct.pack_into("<HH", fat, 0, 0xFFF8, 0xFFFF)
    root = bytearray(root_sectors * SECTOR)
    data = bytearray()
    next_cluster = 2
    if len(files) > root_entries:
        fail("too many files")
    for index, (source, name) in enumerate(files):
        with open(source, "rb") as f:
            content = f.read()
        count = (len(content) + cluster_bytes - 1) // cluster_bytes
        if next_cluster + count > clusters + 2:
            fail("the files don't fit on the disk")
        first = next_cluster if count else 0
        for i in range(count):
            link = next_cluster + 1 if i < count - 1 else 0xFFFF
            struct.pack_into("<H", fat, next_cluster * 2, link)
            next_cluster += 1
        data += content.ljust(count * cluster_bytes, b"\0")
        entry = bytearray(32)
        entry[0:11] = short_name(name)
        entry[11] = 0x20
        struct.pack_into("<HHHI", entry, 22, 0, 0x21, first, len(content))  # dated 1980-01-01
        root[index * 32:index * 32 + 32] = entry

    mbr = bytearray(SECTOR)
    mbr[446:462] = (b"\x00" + chs(start) + b"\x04" + chs(disk_sectors - 1)
                    + struct.pack("<II", start, total))
    mbr[510:512] = b"\x55\xAA"

    with open(path, "wb") as f:
        f.write(mbr)
        f.seek(start * SECTOR)
        f.write(boot)
        f.write(fat * fats)
        f.write(root)
        f.write(data)
        f.truncate(disk_sectors * SECTOR)


if __name__ == "__main__":
    args = sys.argv[1:]
    if len(args) >= 2 and args[0] == "make":
        pairs = []
        for item in args[2:]:
            source, _, name = item.partition("=")
            pairs.append((source, name or source.rsplit("/", 1)[-1]))
        make(args[1], pairs)
    elif len(args) == 3 and args[0] == "read":
        sys.stdout.buffer.write(Volume(args[1]).read(args[2]))
    elif len(args) == 4 and args[0] == "replace":
        with open(args[3], "rb") as source:
            Volume(args[1], writable=True).replace(args[2], source.read())
    else:
        sys.exit(__doc__)
