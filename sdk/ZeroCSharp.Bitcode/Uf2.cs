// zero-bitcode elf2uf2 IN.elf OUT.uf2: a program for the Raspberry Pi Pico 2
// as a UF2 file, the format its boot ROM takes when the board shows up as a
// USB drive (hold BOOTSEL while plugging it in, then copy the file to it).
//
// UF2 (https://github.com/microsoft/uf2) is a list of 512-byte blocks, each
// carrying 256 bytes for one flash address. The bytes are the ELF file's
// loadable segments at their load addresses (code, read-only data and the
// initial values of the writable data, all in flash); what only exists in
// RAM (.bss, the stacks) has no bytes in the file and isn't written.
namespace ZeroBitcode;

static class Uf2
{
    const uint Magic0 = 0x0A324655, Magic1 = 0x9E5D5157, MagicEnd = 0x0AB16F30;
    const uint FlagFamilyId = 0x00002000;
    // The family the RP2350's boot ROM takes for an Arm program in secure state.
    const uint FamilyRp2350ArmSecure = 0xe48bff59;
    const uint FlashStart = 0x10000000, FlashEnd = 0x12000000;
    const int Payload = 256;

    static ToolFailure Fail(string message) => new("elf2uf2", message);

    public static void Run(string input, string output)
    {
        byte[] elf = File.ReadAllBytes(input);
        if (elf.Length < 52 || elf[0] != 0x7f || elf[1] != 'E' || elf[2] != 'L' || elf[3] != 'F'
            || elf[4] != 1 || elf[5] != 1)
            throw Fail($"{input} isn't a 32-bit little-endian ELF file");
        uint U32(int at) => BitConverter.ToUInt32(elf, at);
        ushort U16(int at) => BitConverter.ToUInt16(elf, at);

        // The flash image, page by page (256 bytes each, as UF2 carries them).
        var pages = new SortedDictionary<uint, byte[]>();
        int headers = (int)U32(28), size = U16(42), count = U16(44);
        for (int i = 0; i < count; i++)
        {
            int h = headers + i * size;
            const uint PtLoad = 1;
            uint type = U32(h), offset = U32(h + 4), physical = U32(h + 12), fileSize = U32(h + 16);
            if (type != PtLoad || fileSize == 0)
                continue;
            if (physical < FlashStart || physical + fileSize > FlashEnd)
                throw Fail($"a segment at 0x{physical:x8} isn't in the Pico 2's flash");
            for (uint at = 0; at < fileSize; at++)
            {
                uint address = physical + at;
                uint page = address & ~(uint)(Payload - 1);
                if (!pages.TryGetValue(page, out byte[]? bytes))
                    pages[page] = bytes = new byte[Payload];
                bytes[address - page] = elf[offset + at];
            }
        }
        if (pages.Count == 0)
            throw Fail($"{input} has nothing to put in flash");

        using var file = File.Create(output);
        var block = new byte[512];
        uint number = 0;
        foreach (var (page, bytes) in pages)
        {
            Array.Clear(block);
            void Put(int at, uint value) => BitConverter.TryWriteBytes(block.AsSpan(at), value);
            Put(0, Magic0);
            Put(4, Magic1);
            Put(8, FlagFamilyId);
            Put(12, page);
            Put(16, Payload);
            Put(20, number++);
            Put(24, (uint)pages.Count);
            Put(28, FamilyRp2350ArmSecure);
            bytes.CopyTo(block, 32);
            Put(508, MagicEnd);
            file.Write(block);
        }
    }
}
