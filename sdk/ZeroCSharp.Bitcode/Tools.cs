// The compilers a bitcode target needs that the SDK doesn't ship: downloaded
// on first use from their own releases, checked against the SHA-256 written
// here, and kept in a per-user cache that every project and SDK version with
// the same tool versions shares.
//
//   zero-bitcode tools ilc         prints NativeAOT-LLVM's folder (downloading it)
//   zero-bitcode tools llvm-mos    prints llvm-mos's folder
//   zero-bitcode tools arm         prints Arm Toolchain for Embedded's folder
//
// The cache is ZERO_TOOLS_CACHE if set; otherwise %LOCALAPPDATA%\ZeroCSharp\tools
// on Windows and $XDG_CACHE_HOME/zerocsharp/tools (~/.cache/zerocsharp/tools)
// elsewhere.
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ZeroBitcode;

static class Tools
{
    public static ToolFailure Fail(string message) => new("zero-bitcode", message);

    // NativeAOT-LLVM: the compiler from IL to LLVM bitcode. Versions as env.sh.
    public const string IlcVersion = "10.0.0-rc.1.26357.1";
    static readonly Dictionary<string, string> IlcSha256 = new()
    {
        ["linux-x64"] = "d5ecbf4efc3cd6756e9cbef17ab83764f9770c0a5f35de4fed787e5507f8ecc8",
        ["linux-arm64"] = "317d25f453b41e13b1e24dce4302bf12b1310f0ddd9c8e305c5e4c9456680ccd",
        ["win-x64"] = "9b0f21f9a711d071b13a55ea2d94a8ac126ab0bdc43d4d994e8f0462d6bffd79",
    };

    // llvm-mos: clang, a linker and the C library for the 6502.
    public const string LlvmMosVersion = "v23.4.0";
    static readonly Dictionary<string, (string File, string Sha256)> LlvmMos = new()
    {
        ["linux"] = ("llvm-mos-linux.tar.xz", "0b1dbc8a2b15db88a1a400a687ea24cf1a38548c5ba9fdd61a473e4580ba00c1"),
        ["macos"] = ("llvm-mos-macos.tar.xz", "089afeff92ad4a5321bea0118b678afa6c725206d32bd8e9691bbe65880f8b7c"),
        ["windows"] = ("llvm-mos-windows.7z", "1ccfee003fd0cbbe9311b285ebb8c46548f036d3a32ac9cf2881fe46e9cb0a52"),
    };

    // Arm Toolchain for Embedded: clang, lld, and picolibc built for every
    // Cortex-M. Versions as env.sh. A Mac gets Linux arm64's, run in the same
    // container as NativeAOT-LLVM: Arm's macOS build is a 750 MB disk image.
    public const string ArmVersion = "23.1.0";
    static readonly Dictionary<string, (string File, string Sha256)> ArmToolchain = new()
    {
        ["linux-x64"] = ("Linux-x86_64.tar.xz", "a7be511613af15151c93961ad39c8cf9ae6889a453c8b547f42f4051a416dc8e"),
        ["linux-arm64"] = ("Linux-AArch64.tar.xz", "df3b055d05659d8fda710d820e6c1ceff7bca3234c5067abe82a16e781d8de99"),
        ["win-x64"] = ("Windows-x86_64.zip", "f1aec4fd8ae5c74186c00f620a8c1eea9d03d4d51db2eba5bcd4e707b4c19fe7"),
    };

    public static string CacheRoot()
    {
        string? chosen = Environment.GetEnvironmentVariable("ZERO_TOOLS_CACHE");
        if (!string.IsNullOrEmpty(chosen))
            return Path.GetFullPath(chosen);
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroCSharp", "tools");
        string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        string cache = string.IsNullOrEmpty(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
            : xdg;
        return Path.Combine(cache, "zerocsharp", "tools");
    }

    /// <summary>The NativeAOT-LLVM build to run: the host's, or on a Mac,
    /// Linux arm64's, which runs in a container (Microsoft publishes none for
    /// Apple Silicon).</summary>
    public static string IlcRid()
    {
        bool arm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsMacOS())
            return arm64 ? "linux-arm64" : throw Fail("the C64 and microcontroller targets build on Apple Silicon Macs, not Intel ones");
        if (OperatingSystem.IsLinux())
            return arm64 ? "linux-arm64" : "linux-x64";
        if (OperatingSystem.IsWindows() && !arm64)
            return "win-x64";
        throw Fail($"NativeAOT-LLVM has no build for this machine ({RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture})");
    }

    /// <summary>NativeAOT-LLVM's folder (with tools/ilc in it), downloaded if needed.</summary>
    public static string Ilc()
    {
        string rid = IlcRid();
        string id = $"runtime.{rid}.microsoft.dotnet.ilcompiler.llvm";
        string folder = Path.Combine(CacheRoot(), $"ilc-{IlcVersion}-{rid}");
        Ensure(folder, $"NativeAOT-LLVM {IlcVersion} ({rid})",
            $"https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/flat2/{id}/{IlcVersion}/{id}.{IlcVersion}.nupkg",
            IlcSha256[rid], (archive, into) =>
            {
                ZipFile.ExtractToDirectory(archive, into);
                string ilc = Path.Combine(into, "tools", "ilc");
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(ilc, File.GetUnixFileMode(ilc) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            });
        return folder;
    }

    /// <summary>llvm-mos's folder (with bin/clang in it), downloaded if needed.</summary>
    public static string LlvmMosFolder()
    {
        string host;
        if (OperatingSystem.IsMacOS())
            host = "macos";
        else if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
            host = "linux";
        else if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
            host = "windows";
        else
            throw Fail($"llvm-mos has no build for this machine ({RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture})");
        var (file, sha256) = LlvmMos[host];
        string folder = Path.Combine(CacheRoot(), $"llvm-mos-{LlvmMosVersion}-{host}");
        Ensure(folder, $"llvm-mos {LlvmMosVersion} ({host})",
            $"https://github.com/llvm-mos/llvm-mos-sdk/releases/download/{LlvmMosVersion}/{file}",
            sha256, (archive, into) =>
            {
                // The archive holds one folder, llvm-mos.
                string unpacked = into + ".unpack";
                if (Directory.Exists(unpacked))
                    Directory.Delete(unpacked, recursive: true);
                Directory.CreateDirectory(unpacked);
                Unpack(archive, file, unpacked);
                Directory.Delete(into);
                Directory.Move(Path.Combine(unpacked, "llvm-mos"), into);
                Directory.Delete(unpacked, recursive: true);
            });
        return folder;
    }

    /// <summary>Arm Toolchain for Embedded's folder (with bin/clang in it),
    /// downloaded if needed: the host's build, or on a Mac Linux arm64's, run
    /// in a container (Arm.cs).</summary>
    public static string ArmFolder()
    {
        string rid = IlcRid(); // the same choice: a Mac gets Linux arm64's
        if (!ArmToolchain.TryGetValue(rid, out var download))
            throw Fail($"Arm Toolchain for Embedded has no build for this machine ({RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture})");
        string file = $"ATfE-{ArmVersion}-{download.File}";
        string folder = Path.Combine(CacheRoot(), $"atfe-{ArmVersion}-{rid}");
        Ensure(folder, $"Arm Toolchain for Embedded {ArmVersion} ({rid})",
            $"https://github.com/arm/arm-toolchain/releases/download/release-{ArmVersion}-ATfE/{file}",
            download.Sha256, (archive, into) =>
            {
                // The archive holds one folder, ATfE-<version>-<host>.
                string unpacked = into + ".unpack";
                if (Directory.Exists(unpacked))
                    Directory.Delete(unpacked, recursive: true);
                Directory.CreateDirectory(unpacked);
                if (file.EndsWith(".zip", StringComparison.Ordinal))
                    ZipFile.ExtractToDirectory(archive, unpacked);
                else
                    Unpack(archive, file, unpacked, executables: null);
                string[] top = Directory.GetDirectories(unpacked);
                if (top.Length != 1)
                    throw Fail($"{file} doesn't hold one folder, as expected");
                Directory.Delete(into);
                Directory.Move(top[0], into);
                Directory.Delete(unpacked, recursive: true);
                MakeExecutable(Path.Combine(into, "bin"));
            });
        return folder;
    }

    // .tar.xz (Linux, macOS) or .7z (Windows), by SharpCompress: .NET reads
    // neither, and a system's tar may lack xz (Debian's and Ubuntu's images
    // do) or 7z.
    static void Unpack(string archive, string name, string into, string? executables = "llvm-mos/bin")
    {
        var options = new SharpCompress.Common.ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true,
            // llvm-mos's bin/ has links (clang to clang-<version>, and others).
            SymbolicLinkHandler = (link, target) => File.CreateSymbolicLink(link, target),
        };
        var readerOptions = new SharpCompress.Readers.ReaderOptions();
        if (name.EndsWith(".7z", StringComparison.Ordinal))
        {
            // A 7z archive is read through its index, not as a stream.
            using var sevenZip = SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(archive, readerOptions);
            using var reader = sevenZip.ExtractAllEntries();
            SharpCompress.Readers.IReaderExtensions.WriteAllToDirectory(reader, into, options);
        }
        else
        {
            using var reader = SharpCompress.Readers.ReaderFactory.OpenReader(archive, readerOptions);
            SharpCompress.Readers.IReaderExtensions.WriteAllToDirectory(reader, into, options);
        }

        if (executables != null)
            MakeExecutable(Path.Combine(into, executables));
    }

    // An archive's permissions aren't restored: its programs need to run.
    static void MakeExecutable(string bin)
    {
        if (OperatingSystem.IsWindows())
            return;
        foreach (string program in Directory.EnumerateFiles(bin))
        {
            if (File.ResolveLinkTarget(program, returnFinalTarget: false) is null)
                File.SetUnixFileMode(program, File.GetUnixFileMode(program) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
    }

    // Downloads, checks and unpacks into `folder`, once: a finished folder
    // has a .complete file, and a lock keeps two builds from doing it at once.
    static void Ensure(string folder, string what, string url, string sha256, Action<string, string> unpack)
    {
        string done = folder + ".complete";
        if (File.Exists(done))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        using FileStream lockFile = Lock(folder + ".lock");
        if (File.Exists(done))
            return;

        string archive = folder + ".download";
        Console.Error.WriteLine($"zero-bitcode: downloading {what} into {Path.GetDirectoryName(folder)}, once...");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (Stream source = http.GetStreamAsync(url).GetAwaiter().GetResult())
            using (FileStream target = File.Create(archive))
                source.CopyTo(target);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            throw Fail($"couldn't download {what} from {url}: {e.Message}. The first build for this target needs the internet.");
        }

        string actual;
        using (FileStream stream = File.OpenRead(archive))
            actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (actual != sha256)
        {
            File.Delete(archive);
            throw Fail($"{url} isn't the file expected (SHA-256 {actual}, expected {sha256})");
        }

        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        unpack(archive, folder);
        File.Delete(archive);
        File.WriteAllText(done, url + "\n");
    }

    static FileStream Lock(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (attempt < 3600)
            {
                Thread.Sleep(1000);     // another build is downloading
            }
        }
    }

    /// <summary>Runs a program, failing with its output if it fails.</summary>
    public static void Run(string program, IEnumerable<string> arguments, string doing)
    {
        var info = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw Fail($"couldn't start {program}");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw Fail($"{doing} failed ({program} exited with {process.ExitCode}):\n{output.Result}{errors}");
    }

    public static void Command(string[] args)
    {
        string folder = args switch
        {
            ["ilc"] => Ilc(),
            ["llvm-mos"] => LlvmMosFolder(),
            ["arm"] => ArmFolder(),
            _ => throw Fail("usage: zero-bitcode tools ilc|llvm-mos|arm"),
        };
        Console.WriteLine(folder);
    }
}
