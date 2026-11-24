// A program for a Cortex-M microcontroller from IL, in two steps, as
// ./build.sh --pico2 and --r4 make one:
//
//   zero-bitcode arm-bitcode IN.dll OUTDIR CORE.dll [REF.dll...]
//       NativeAOT-LLVM, as for the C64 (C64.cs) but optimizing for speed.
//
//   zero-bitcode arm-link OBJDIR NAME RUNTIMEDIR BOARD QEMU.elf [BOARD.elf BOARD.uf2]
//       Arm Toolchain for Embedded: the bitcode in OBJDIR -> QEMU.elf, the
//       program for QEMU's stand-in board (arm/runtime.c and arm/semihost.c,
//       from RUNTIMEDIR); for the pico2, also BOARD.elf and BOARD.uf2, the
//       program for a real Raspberry Pi Pico 2 (arm/rp2350.c and rp2350.S).
//       BOARD: pico2 or r4.
//
// The bitcode needs no rewriting: a Cortex-M is 32-bit and little-endian,
// like WebAssembly. wasm2arm (Wasm2Arm.cs) writes its data as assembly. On a
// Mac the toolchain is Linux arm64's, run in a container like NativeAOT-LLVM
// (Arm publishes a macOS build, but as a 750 MB disk image).
using System.Text.RegularExpressions;

namespace ZeroBitcode;

static class ArmTarget
{
    static ToolFailure Fail(string message) => new("zero-bitcode", message);

    // What env.sh's arm_board says for each board: the processor, the QEMU
    // board's layout, and arm/runtime.c's and semihost.c's settings.
    sealed record Board(string Name, string What, string[] Target, string Layout, string[] Defines);

    static readonly Board[] Boards =
    [
        new("pico2", "Raspberry Pi Pico 2",
            ["--target=thumbv8m.main-none-eabihf", "-mcpu=cortex-m33", "-mfloat-abi=hard", "-mfpu=fpv5-sp-d16"],
            "an505.ld.in", ["-DSHADOW_STACK_SIZE=16384", "-DZERO_LED_REGISTER=0x50302000", "-DZERO_CPU_HZ=20000000"]),
        new("r4", "Arduino Uno R4",
            ["--target=thumbv7em-none-eabihf", "-mcpu=cortex-m4", "-mfloat-abi=hard", "-mfpu=fpv4-sp-d16"],
            "an386.ld.in", ["-DSHADOW_STACK_SIZE=4096", "-DZERO_LED_REGISTER=0x40028000", "-DZERO_CPU_HZ=25000000"]),
    ];

    public static void Bitcode(string[] args) => C64.Bitcode(args, "arm-bitcode", "-O");

    public static void Link(string[] args)
    {
        string? armFolder = null;
        var positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--arm" && i + 1 < args.Length)
                armFolder = args[++i];
            else
                positional.Add(args[i]);
        }
        if (positional.Count != 5 && positional.Count != 7)
            throw Fail("usage: zero-bitcode arm-link OBJDIR NAME RUNTIMEDIR BOARD QEMU.elf [BOARD.elf BOARD.uf2] [--arm DIR]");
        string obj = Path.GetFullPath(positional[0]), name = positional[1];
        string runtime = Path.GetFullPath(positional[2]);
        Board board = Boards.FirstOrDefault(b => b.Name == positional[3])
            ?? throw Fail($"unknown board '{positional[3]}' (pico2 or r4)");
        string qemuElf = Path.GetFullPath(positional[4]);
        string? boardElf = positional.Count == 7 ? Path.GetFullPath(positional[5]) : null;
        string? boardUf2 = positional.Count == 7 ? Path.GetFullPath(positional[6]) : null;
        if (boardElf != null && board.Name != "pico2")
            throw Fail($"only the pico2 has a build for the real board so far, not the {board.Name}");

        bool docker = OperatingSystem.IsMacOS();
        armFolder = Path.GetFullPath(armFolder ?? Tools.ArmFolder());
        if (docker)
            C64.CheckDocker();
        string clang = Path.Combine(armFolder, "bin", OperatingSystem.IsWindows() ? "clang.exe" : "clang");

        string work = Path.Combine(obj, "arm-" + board.Name);
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        var outputs = new[] { qemuElf, boardElf, boardUf2 }.Where(o => o != null).Select(o => Path.GetDirectoryName(o!)!).Distinct().ToList();
        foreach (string folder in outputs)
            Directory.CreateDirectory(folder);
        var mounts = new[] { armFolder, obj, runtime }.Concat(outputs).Distinct(StringComparer.Ordinal).ToList();

        // Runs clang in `work`: here, or on a Mac in a Linux container with
        // every folder it uses mounted at the same path.
        (int Code, string Log) Clang(IEnumerable<string> arguments)
        {
            if (!docker)
                return CaptureIn(work, clang, arguments);
            var command = new List<string> { "run", "--rm", "-w", work };
            foreach (string folder in mounts)
                command.AddRange(["-v", $"{folder}:{folder}"]);
            command.Add("ubuntu:24.04");
            command.Add(clang);
            command.AddRange(arguments);
            return C64.Capture("docker", command);
        }

        void Step(IEnumerable<string> arguments, string doing)
        {
            var (code, log) = Clang(arguments);
            if (code != 0)
                throw Fail($"{doing} failed:\n{log}");
        }

        // The data, as assembly.
        Wasm.Convert(Path.Combine(obj, name + ".bc"), Path.Combine(work, "data"), new Arm());
        string external = Path.Combine(obj, name + ".external.o");
        if (File.Exists(external))
            Wasm.Convert(external, Path.Combine(work, "external"), new Arm());
        // Typed wrappers in C, if any: named apart from the assembly, since
        // clang names each object after its source file.
        foreach (string c in Directory.GetFiles(work, "*.c"))
            File.Move(c, Path.Combine(work, Path.GetFileNameWithoutExtension(c) + "-c.c"));

        // The code: bitcode -> text, in one run (each <name>.<n>.ll in work).
        var bitcode = Directory.GetFiles(obj, name + ".*.bc")
            .Where(f => Regex.IsMatch(Path.GetFileName(f), @"\.\d+\.bc$"))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (bitcode.Count == 0)
            throw Fail($"no bitcode for {name} in {obj}");
        Step([.. board.Target, "-Wno-override-module", "-S", "-emit-llvm", .. bitcode], "reading the bitcode");

        // One thread, so the flag compiled code checks after every call
        // (RhpExceptionThrown) needs no thread-local storage, which bare
        // metal doesn't have: as for DOS (build.sh), it becomes an ordinary
        // variable. As a word: a string constant's symbol can contain it.
        var threadLocal = new Regex(@"^(@[A-Za-z0-9_.$]+ = external) thread_local ", RegexOptions.Multiline);
        foreach (string bc in bitcode)
        {
            string ll = Path.Combine(work, Path.GetFileNameWithoutExtension(bc) + ".ll");
            string text = threadLocal.Replace(Files.ReadText(ll), "$1 ");
            if (Regex.IsMatch(text, @"\bthread_local\b"))
                throw Fail($"thread-local variables left in {ll}");
            Files.WriteText(ll, text);
        }

        // Everything to objects, in one run.
        var sources = Directory.GetFiles(work, "*.ll").Concat(Directory.GetFiles(work, "*.s"))
            .Concat(Directory.GetFiles(work, "*.c")).Order(StringComparer.Ordinal).ToList();
        Step([.. board.Target, "-Os", "-Wno-override-module", "-ffreestanding", "-c", .. sources], "compiling");
        var objects = Directory.GetFiles(work, "*.o").Order(StringComparer.Ordinal).ToList();

        // picolibc's linker script places everything; the board's says where
        // its flash and RAM are.
        string Layout(string file)
        {
            string layout = Path.Combine(work, Path.GetFileNameWithoutExtension(file));
            Files.WriteText(layout, Files.ReadText(Path.Combine(runtime, file)).Replace("LIBC_LD_FILE", "picolibcpp.ld"));
            return layout;
        }

        void LinkProgram(string what, string startup, string[] libraries, string layout, string[] own, string[] flags, string output)
        {
            var (code, log) = Clang([.. board.Target, .. board.Defines, "-Os", "-nostartfiles", startup, .. libraries,
                "-T", layout, "-Wl,--gc-sections", .. flags, .. own, .. objects, "-o", output]);
            File.WriteAllText(output + ".log", log);
            if (code == 0)
            {
                File.Delete(output + ".log");
                return;
            }
            Match tooBig = Regex.Match(log, @"section '\.(?:data|bss|heap|stack)' will not fit in region 'ram': overflowed by (\d+) bytes");
            if (tooBig.Success)
                throw Fail($"{name} doesn't fit in the {board.What}'s RAM ({what}):\n  too big by {tooBig.Groups[1].Value} bytes");
            var missing = Regex.Matches(log, @"undefined symbol: ([A-Za-z0-9_]+)").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
                throw Fail($"{name} calls functions the microcontroller's runtime (arm/) doesn't have:\n  {string.Join(" ", missing)}");
            throw Fail($"linking {name} ({what}) failed:\n{log}");
        }

        // For QEMU: crt0-semihost gets the command line from the host, and
        // libsemihost does files there; semihost.c reads piped input from a
        // file (--wrap=read).
        LinkProgram("for QEMU", "-lcrt0-semihost", ["-lsemihost"], Layout(board.Layout),
            [Path.Combine(runtime, "runtime.c"), Path.Combine(runtime, "semihost.c")], ["-Wl,--wrap=read"], qemuElf);

        // For the real Pico 2: crt0-hosted (the plain crt0 never returns
        // from main), the chip's own startup, and the boot ROM's block.
        if (boardElf != null)
        {
            LinkProgram("for the board", "-lcrt0-hosted", [], Layout("rp2350.ld.in"),
                [Path.Combine(runtime, "runtime.c"), Path.Combine(runtime, "rp2350.c"), Path.Combine(runtime, "rp2350.S")], [], boardElf);
            Uf2.Run(boardElf, boardUf2!);
        }
    }

    static (int Code, string Log) CaptureIn(string folder, string program, IEnumerable<string> arguments)
    {
        string before = Environment.CurrentDirectory;
        Environment.CurrentDirectory = folder;
        try
        {
            return C64.Capture(program, arguments);
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }
    }
}
