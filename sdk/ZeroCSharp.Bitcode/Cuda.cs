// A Linux program's GPU kernels for NVIDIA GPUs (ZeroGpu=Cuda), after
// zero-bitcode gpu-bitcode has compiled the program's IL to bitcode:
//
//   zero-bitcode cuda-kernels BITCODEDIR NAME ARCH OUTDIR [CLANG] [SM]
//       The bitcode as text (clang) -> the kernels for LLVM's NVPTX backend
//       (ll2ptx, Ptx.cs) -> PTX (clang, for SM, default sm_70), checked by
//       NVIDIA's ptxas when it's on the PATH -> OUTDIR/kernels.o, an ELF object
//       for ARCH (x64 or arm64) that holds the PTX as gpu_ptx, the
//       zero-terminated string gpu/cuda.c hands the driver.
//
//   zero-bitcode cuda-clang [CLANG]
//       The clang cuda-kernels would use, or why there's none.
//
// CLANG is a clang 18 or later (NativeAOT-LLVM's bitcode is LLVM 18's, which
// older ones can't read) with the NVPTX backend: LLVM's own builds and Linux
// distributions' have it, Apple's doesn't (Homebrew's llvm does). Without
// CLANG, the first of clang, clang-22 ... clang-18 and Homebrew's that fits.
using System.Diagnostics;
using System.Text;

namespace ZeroBitcode;

static class Cuda
{
    static ToolFailure Fail(string message) => new("zero-bitcode", message);

    public static void Kernels(string[] args)
    {
        if (args.Length is < 4 or > 6)
            throw Fail("usage: zero-bitcode cuda-kernels BITCODEDIR NAME ARCH OUTDIR [CLANG] [SM]");
        string bitcodeDir = Path.GetFullPath(args[0]);
        string name = args[1];
        ushort machine = args[2] switch
        {
            "x64" => 62,     // EM_X86_64
            "arm64" => 183,  // EM_AARCH64
            _ => throw Fail($"ARCH is x64 or arm64, not {args[2]}"),
        };
        string outDir = Path.GetFullPath(args[3]);
        string clang = FindClang(args.Length > 4 ? args[4] : "");
        string sm = args.Length > 5 && args[5] != "" ? args[5] : "sm_70";

        string llDir = Path.Combine(outDir, "ll");
        if (Directory.Exists(llDir))
            Directory.Delete(llDir, recursive: true);
        Directory.CreateDirectory(llDir);
        string[] bitcode = Directory.GetFiles(bitcodeDir, name + ".*.bc").Order(StringComparer.Ordinal).ToArray();
        if (bitcode.Length == 0)
            throw Fail($"no bitcode for {name} in {bitcodeDir} (zero-bitcode gpu-bitcode writes it)");
        var modules = new List<string>();
        foreach (string bc in bitcode)
        {
            string ll = Path.Combine(llDir, Path.GetFileNameWithoutExtension(bc) + ".ll");
            Run(clang, ["-Wno-override-module", "-S", "-emit-llvm", bc, "-o", ll]);
            modules.Add(ll);
        }

        string kernels = Path.Combine(outDir, "kernels");
        Ptx.Run(kernels + ".ll", name, modules.ToArray());
        Run(clang, ["--target=nvptx64-nvidia-cuda", "-march=" + sm, "-O3", "-Wno-override-module",
            "-S", kernels + ".ll", "-o", kernels + ".ptx"]);
        // ptxas, if there is one, finds what the driver would refuse at run time.
        if (Succeeds("ptxas", "--version"))
            Run("ptxas", ["-arch=" + sm, kernels + ".ptx", "-o", kernels + ".cubin"]);

        byte[] ptx = File.ReadAllBytes(kernels + ".ptx");
        File.WriteAllBytes(kernels + ".o", ElfObject(machine, "gpu_ptx", [.. ptx, 0]));
    }

    // zero-bitcode cuda-clang [CLANG]: the clang cuda-kernels would use, or
    // why there's none, before anything slow runs.
    public static void CheckClang(string[] args)
    {
        if (args.Length > 1)
            throw Fail("usage: zero-bitcode cuda-clang [CLANG]");
        Console.WriteLine(FindClang(args.Length > 0 ? args[0] : ""));
    }

    static string FindClang(string given)
    {
        if (given != "")
        {
            string? problem = Unfit(given);
            if (problem != null)
                throw Fail($"ZeroCudaClang {given}: {problem}. It needs to be clang 18 or later with the NVPTX backend, such as LLVM's own or Homebrew's llvm.");
            return given;
        }
        var candidates = new List<string> { "clang" };
        for (int version = 22; version >= 18; version--)
            candidates.Add($"clang-{version}");
        candidates.Add("/opt/homebrew/opt/llvm/bin/clang");
        candidates.Add("/usr/local/opt/llvm/bin/clang");
        var seen = new List<string>();
        foreach (string candidate in candidates)
        {
            string? problem = Unfit(candidate);
            if (problem == null)
                return candidate;
            if (problem != "not found")
                seen.Add($"{candidate}: {problem}");
        }
        throw Fail("ZeroGpu=Cuda needs clang 18 or later with the NVPTX backend (to compile for NVIDIA GPUs), and none was found"
            + (seen.Count > 0 ? " (" + string.Join("; ", seen) + ")" : "") + ". "
            + "Install LLVM (Linux: apt install clang-18 or later; macOS: brew install llvm, which Apple's clang doesn't replace; Windows: LLVM's installer), "
            + "or set ZeroCudaClang to its clang.");
    }

    // Why this clang won't do, or null if it will.
    static string? Unfit(string clang)
    {
        (int ExitCode, string Output) version, targets;
        try
        {
            version = Execute(clang, ["--version"]);
            targets = Execute(clang, ["--print-targets"]);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "not found";
        }
        if (version.ExitCode != 0 || targets.ExitCode != 0)
            return "doesn't run";
        var match = System.Text.RegularExpressions.Regex.Match(version.Output, @"clang version (\d+)");
        if (match.Success && int.Parse(match.Groups[1].Value) < 18)
            return $"version {match.Groups[1].Value}, older than 18";
        if (!targets.Output.Contains("nvptx64"))
            return "no NVPTX backend";
        return null;
    }

    // A relocatable ELF64 object (little-endian) with one global symbol: `data`
    // in .rodata. Sections: null, .rodata, .symtab, .strtab, .note.GNU-stack
    // (no executable stack), .shstrtab.
    internal static byte[] ElfObject(ushort machine, string symbol, byte[] data)
    {
        byte[] shstrtab = Encoding.ASCII.GetBytes("\0.rodata\0.symtab\0.strtab\0.note.GNU-stack\0.shstrtab\0");
        int Name(string section) => Encoding.ASCII.GetString(shstrtab).IndexOf("\0" + section + "\0", StringComparison.Ordinal) + 1;
        byte[] strtab = Encoding.ASCII.GetBytes("\0" + symbol + "\0");

        var symtab = new MemoryStream();
        var symbols = new BinaryWriter(symtab);
        symbols.Write(new byte[24]);           // symbol 0: none
        symbols.Write(1);                      // st_name
        symbols.Write((byte)0x11);             // st_info: STB_GLOBAL, STT_OBJECT
        symbols.Write((byte)0);                // st_other: default visibility
        symbols.Write((ushort)1);              // st_shndx: .rodata
        symbols.Write(0L);                     // st_value
        symbols.Write((long)data.Length);      // st_size

        var file = new MemoryStream();
        var w = new BinaryWriter(file);
        w.Write(new byte[64]);                 // the header, written last
        long Place(byte[] bytes, int align)
        {
            while (file.Length % align != 0)
                w.Write((byte)0);
            long offset = file.Length;
            w.Write(bytes);
            return offset;
        }
        long rodata = Place(data, 16);
        long symtabAt = Place(symtab.ToArray(), 8);
        long strtabAt = Place(strtab, 1);
        long shstrtabAt = Place(shstrtab, 1);
        while (file.Length % 8 != 0)
            w.Write((byte)0);
        long sections = file.Length;

        void Section(int name, uint type, ulong flags, long offset, long size, uint link, uint info, long align, long entsize)
        {
            w.Write(name); w.Write(type); w.Write(flags); w.Write(0UL);
            w.Write(offset); w.Write(size); w.Write(link); w.Write(info); w.Write(align); w.Write(entsize);
        }
        Section(0, 0, 0, 0, 0, 0, 0, 0, 0);
        Section(Name(".rodata"), 1, 2, rodata, data.Length, 0, 0, 16, 0);                 // PROGBITS, ALLOC
        Section(Name(".symtab"), 2, 0, symtabAt, symtab.Length, 3, 1, 8, 24);             // SYMTAB: names in 3, first global 1
        Section(Name(".strtab"), 3, 0, strtabAt, strtab.Length, 0, 0, 1, 0);              // STRTAB
        Section(Name(".note.GNU-stack"), 1, 0, shstrtabAt, 0, 0, 0, 1, 0);                // empty
        Section(Name(".shstrtab"), 3, 0, shstrtabAt, shstrtab.Length, 0, 0, 1, 0);

        file.Position = 0;
        w.Write([0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0]); // ELF64, little-endian, SysV
        w.Write((ushort)1);                    // e_type: ET_REL
        w.Write(machine);
        w.Write(1);                            // e_version
        w.Write(0L);                           // e_entry
        w.Write(0L);                           // e_phoff
        w.Write(sections);                     // e_shoff
        w.Write(0);                            // e_flags
        w.Write((ushort)64);                   // e_ehsize
        w.Write((ushort)0);                    // e_phentsize
        w.Write((ushort)0);                    // e_phnum
        w.Write((ushort)64);                   // e_shentsize
        w.Write((ushort)6);                    // e_shnum
        w.Write((ushort)5);                    // e_shstrndx
        return file.ToArray();
    }

    static bool Succeeds(string program, params string[] arguments)
    {
        try
        {
            return Execute(program, arguments).ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    static void Run(string program, string[] arguments)
    {
        var (exitCode, output) = Execute(program, arguments);
        if (exitCode != 0)
            throw Fail($"{program} {string.Join(' ', arguments)} failed:\n{output}");
        if (output.Length > 0)
            Console.Error.Write(output);
    }

    static (int ExitCode, string Output) Execute(string program, string[] arguments)
    {
        var info = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw Fail($"couldn't start {program}");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Result + errors);
    }
}
