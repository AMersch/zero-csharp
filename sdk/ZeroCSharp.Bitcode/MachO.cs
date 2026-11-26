// zero-bitcode macho-platform OBJECT PLATFORM MINOS: marks a Mach-O object as
// built for another of Apple's platforms, in place.
//
// bflat compiles for Apple's arm64 only as macOS (--os macos), and its object
// says so in its LC_BUILD_VERSION load command. iOS runs the same arm64 code,
// but Apple's ld and ld64.lld both refuse to put an object built for macOS
// into a program for another platform ("has platform macOS, which is
// different from target platform iOS Simulator"). This rewrites the
// command's platform and minimum OS version; the SDK version it names stays.
using System.Buffers.Binary;

namespace ZeroBitcode;

static class MachO
{
    const uint Magic64 = 0xFEEDFACF;
    const uint LcBuildVersion = 0x32;
    const int HeaderSize = 32; // mach_header_64

    // Platform numbers from <mach-o/loader.h>, by the names ld's
    // -platform_version takes.
    static readonly Dictionary<string, uint> Platforms = new()
    {
        ["macos"] = 1,
        ["ios"] = 2,
        ["ios-simulator"] = 7,
    };

    public static void Platform(string[] args)
    {
        if (args.Length != 3)
            throw new ToolFailure("macho-platform", "usage: zero-bitcode macho-platform OBJECT macos|ios|ios-simulator MINOS");
        string path = args[0];
        if (!Platforms.TryGetValue(args[1], out uint platform))
            throw new ToolFailure("macho-platform", $"unknown platform '{args[1]}' (macos, ios or ios-simulator)");
        uint minos = Version(args[2]);

        byte[] data = File.ReadAllBytes(path);
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic64)
            throw new ToolFailure("macho-platform", $"{path}: not a 64-bit Mach-O object");
        uint commands = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
        int offset = HeaderSize;
        bool found = false;
        for (uint i = 0; i < commands; i++)
        {
            if (offset + 8 > data.Length)
                throw new ToolFailure("macho-platform", $"{path}: load commands run past the end of the file");
            uint command = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            if (size < 8 || offset + size > data.Length)
                throw new ToolFailure("macho-platform", $"{path}: a load command of size {size} at {offset}");
            if (command == LcBuildVersion)
            {
                // build_version_command: cmd, cmdsize, platform, minos, sdk, ntools.
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 8), platform);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 12), minos);
                found = true;
            }
            offset += (int)size;
        }
        if (!found)
            throw new ToolFailure("macho-platform", $"{path}: no LC_BUILD_VERSION load command");
        File.WriteAllBytes(path, data);
    }

    // "17.0" or "17.0.1" as xxxx.yy.zz in nibbles, the way Mach-O stores it.
    static uint Version(string text)
    {
        string[] parts = text.Split('.');
        if (parts.Length is < 1 or > 3 || !parts.All(p => uint.TryParse(p, out _)))
            throw new ToolFailure("macho-platform", $"'{text}' isn't a version such as 17.0");
        uint major = uint.Parse(parts[0]);
        uint minor = parts.Length > 1 ? uint.Parse(parts[1]) : 0;
        uint patch = parts.Length > 2 ? uint.Parse(parts[2]) : 0;
        if (major > 0xFFFF || minor > 0xFF || patch > 0xFF)
            throw new ToolFailure("macho-platform", $"'{text}' is out of range");
        return (major << 16) | (minor << 8) | patch;
    }
}
