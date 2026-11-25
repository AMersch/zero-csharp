// A macOS program's GPU kernels (Metal), in two steps:
//
//   zero-bitcode gpu-bitcode IN.dll OUTDIR CORE.dll [REF.dll...]
//       NativeAOT-LLVM: IN.dll -> OUTDIR/<name>.<n>.bc, as c64-bitcode does
//       but optimized for speed and with IN.dll's [RuntimeExport] methods,
//       the kernels, compiled whether the program calls them or not.
//
//   zero-bitcode gpu-kernels BITCODEDIR NAME METAL.m OUTDIR
//       On a Mac with Xcode and its Metal Toolchain: the bitcode as text
//       (clang) -> the kernels as AIR (ll2air, Air.cs) -> a metallib (metal,
//       metallib) -> OUTDIR/kernels.o, which holds it for gpu/metal.m to load;
//       and METAL.m (Metal behind the C functions lib/Gpu.cs calls) ->
//       OUTDIR/metal.o. The program links both, with Metal and Foundation.
using System.Diagnostics;

namespace ZeroBitcode;

static class Gpu
{
    static ToolFailure Fail(string message) => new("zero-bitcode", message);

    public static void Bitcode(string[] args) => C64.Bitcode(args, "gpu-bitcode", "-O", entryPoints: true);

    public static void Kernels(string[] args)
    {
        if (args.Length != 4)
            throw Fail("usage: zero-bitcode gpu-kernels BITCODEDIR NAME METAL.m OUTDIR");
        string bitcodeDir = Path.GetFullPath(args[0]);
        string name = args[1];
        string metalSource = Path.GetFullPath(args[2]);
        string outDir = Path.GetFullPath(args[3]);
        if (!OperatingSystem.IsMacOS())
            throw Fail("GPU kernels build on a Mac: they're compiled by Apple's Metal compiler.");
        if (!Succeeds("xcrun", "-f", "metal") || !Succeeds("xcrun", "-f", "metallib"))
            throw Fail("no Metal compiler. Install Xcode, then its Metal Toolchain: xcodebuild -downloadComponent MetalToolchain");

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
            Run("xcrun", ["clang", "-Wno-override-module", "-S", "-emit-llvm", bc, "-o", ll]);
            modules.Add(ll);
        }

        string kernels = Path.Combine(outDir, "kernels");
        Air.Run(kernels + ".ll", name, modules.ToArray());
        Run("xcrun", ["metal", "-O2", "-Wno-override-module", "-c", kernels + ".ll", "-o", kernels + ".air"]);
        Run("xcrun", ["metallib", kernels + ".air", "-o", kernels + ".metallib"]);

        // The metallib, inside the program: gpu/metal.m loads it from these symbols.
        Files.WriteText(kernels + ".s", $"""
                .section __TEXT,__const
                .globl _gpu_metallib, _gpu_metallib_end
                .p2align 4
            _gpu_metallib:
                .incbin "{kernels}.metallib"
            _gpu_metallib_end:

            """);
        // The SDK's programs are for macOS 12 and later (ld64's -platform_version).
        Run("xcrun", ["clang", "-arch", "arm64", "-mmacosx-version-min=12.0", "-c", kernels + ".s", "-o", kernels + ".o"]);
        Run("xcrun", ["clang", "-arch", "arm64", "-mmacosx-version-min=12.0", "-fobjc-arc", "-O2", "-Wall", "-Wextra",
            "-c", metalSource, "-o", Path.Combine(outDir, "metal.o")]);
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
        // Warnings (metal's, clang's) belong in the build log.
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
