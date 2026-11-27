// Code in banks, for the Commander X16.
//
// A program's pointers are 16 bits, and the fixed RAM it is loaded into is
// 38 KB. Some programs have more code than that. The X16 has banked RAM: an
// 8 KB window at $A000, and a register at $00 that says which bank is in it.
// This moves the bodies of the largest functions into banks:
//
//   - A function f chosen for bank 3 is renamed f__bank, marked noinline and
//     put in the section .bank3. A new function f, with the same signature, is
//     left in fixed RAM: it saves the bank register, selects bank 3, calls
//     f__bank, and puts the bank back. Everything that refers to f (calls,
//     function pointers, vtables) goes through it, and the call graph stays
//     visible to llvm-mos, whose allocation of "static stack" locals depends
//     on it.
//   - The linker places every .bankN at $A000 (an overlay, so each has its own
//     load address), and the PRG holds only the fixed part. The banks go in a
//     separate file, NAME.BNK, which cx16/runtime.c loads at startup into banks
//     1, 2, ...: a KERNAL LOAD into $A000 carries on into the next bank.
//   - Banks for data (lib/Banks.cs) start after the code banks.
//
// The functions are chosen by size, largest first, only when the program
// doesn't fit without: see C64.Link. That covers the program's own functions,
// cx16/math.c's, and the floating point of the compiler's library (libcrt's
// members are LLVM IR: some 25 KB for a program that computes with doubles).
//
// Fitting isn't enough: what the code leaves is the heap. After the first link
// that works, more functions are moved while less than X16Heap bytes are left,
// and the link that left the most is the one that is kept. A program that fits
// without banks but would have next to no heap gets banks for the same reason.

using System.Text;
using System.Text.RegularExpressions;

namespace ZeroBitcode;

static partial class C64
{
    // ---- the X16: code that doesn't fit its fixed RAM goes in banks ----------------------
    //
    // C64.Link calls these when a cx16 program is too big. See the top of this
    // file for what is done to the program.

    /// <summary>Where moving functions into banks has got to, between the link's retries.</summary>
    sealed class X16Banks
    {
        public List<(string Name, int Size)> All = [];          // every function, largest first
        public List<(string Name, int Size)> Candidates = [];   // the ones worth moving, and in the order to move them
        public int Next, Saved, Goal, Count;
        public Dictionary<string, int> Assignment = [];     // function to bank, in the last try
        public int Headroom = 512;      // left empty in each bank: the sizes measured aren't quite the final ones
        public string Plain = "";

        // Once a link has worked: the one that left the most heap so far, and how the tries for more have gone.
        public int BestFree = -1, BestCount, Tries, Worse;
    }

    /// <summary>What Link has, for the X16 code to link again with.</summary>
    sealed record X16Context(string Clang, string[] Mos, List<string> Whole, List<string> Separate, List<string> Extra,
        string Runtime, string SaveBasic, string Output, string OutDir, string Name, string LlvmMos);

    // The largest function that is moved into a bank: a bank less some room, as the
    // measured sizes aren't quite the final ones.
    const int X16Largest = Banking.BankSize - 1024;

    // Beyond what is over: the heap and the stack have what the code leaves, and
    // SmallBuilders and SmallDoubleFormats failed (out of memory) with 2 KB.
    const int X16Slack = 4096;

    // What is aimed for: the bytes of heap (with 1 KB for the stack on top of them).
    // A program that allocates a lot asks for more with ZERO_CX16_HEAP (the SDK:
    // the property ZeroCx16Heap); more code then goes into banks, if there is more to move.
    static readonly int X16Wanted = int.TryParse(Environment.GetEnvironmentVariable("ZERO_CX16_HEAP"), out int bytes) && bytes > 0 ? bytes : 0;
    static int X16Heap => X16Wanted > 0 ? X16Wanted : 4096;

    // A program that fits with less than this can't allocate: it gets banks although it fits.
    const int X16NoHeap = 512;

    /// <summary>
    /// Links once with room for all of the program, to see how big its functions
    /// are. Null if even that doesn't work.
    /// </summary>
    static X16Banks? X16Measure(X16Context c)
    {
        X16AddSoftFloat(c);
        string plain = Path.Combine(c.OutDir, "plain");
        Directory.CreateDirectory(plain);
        foreach (string file in c.Whole.Concat(c.Separate.Select(f => f[..^2])).Where(f => f.EndsWith(".ll", StringComparison.Ordinal)))
            File.Copy(file, Path.Combine(plain, Path.GetFileName(file)), overwrite: true);

        string measuring = Path.Combine(c.OutDir, "measure.ld");
        File.WriteAllText(measuring, Banking.Script(0, room: true));
        var (code, _) = Capture(c.Clang, [.. c.Mos, .. X16Defines(c.Whole), "-T", measuring, .. c.Whole, .. c.Separate, .. c.Extra, c.Runtime, c.SaveBasic, "-o", c.Output]);
        if (code != 0)
            return null;
        var defined = Banking.Defined(Directory.GetFiles(plain, "*.ll"));
        var all = Banking.Sizes(c.LlvmMos, c.Output + ".elf").Where(f => defined.Contains(f.Name)).ToList();
        return new X16Banks
        {
            Plain = plain,
            All = all,
            Candidates = all.Where(f => f.Size >= 150 && f.Size <= X16Largest).ToList(),
        };
    }

    /// <summary>
    /// The floating point routines of the compiler's library, as IR next to the
    /// program's, so that they can go into banks too. They are what the linker
    /// would take from libcrt.a otherwise, and the ones not called are dropped
    /// from the program as they would not have been taken.
    /// </summary>
    static void X16AddSoftFloat(X16Context c)
    {
        string library = Path.Combine(c.LlvmMos, "mos-platform", "common", "lib", "libcrt.a");
        string members = Path.Combine(c.OutDir, "crt");
        Directory.CreateDirectory(members);
        string ar = Path.Combine(c.LlvmMos, "bin", OperatingSystem.IsWindows() ? "llvm-ar.exe" : "llvm-ar");
        Tools.Run(ar, ["x", $"--output={members}", library], "unpacking libcrt.a");
        foreach (string member in Directory.GetFiles(members, "*.c.obj").Order(StringComparer.Ordinal))
        {
            string routine = Path.GetFileName(member)[..^".c.obj".Length];
            if (!Regex.IsMatch(routine, "^(add|sub|mul|div|compare|extend|trunc|fix|fixuns|float|floatun)[a-z]*[sd]f[a-z0-9]*$", RegexOptions.CultureInvariant))
                continue;
            string ir = Path.Combine(c.OutDir, "crt_" + routine + ".ll");
            Tools.Run(c.Clang, [.. c.Mos, "-S", "-emit-llvm", "-x", "ir", member, "-o", ir], $"reading {routine} of libcrt.a");
            c.Whole.Add(ir);
        }
    }

    static string X16Tool(X16Context c, string name) => Path.Combine(c.LlvmMos, "bin", OperatingSystem.IsWindows() ? name + ".exe" : name);

    /// <summary>The bytes the linked program leaves for the heap (1 KB below the top is the stack's), or -1 if that can't be told.</summary>
    static int X16HeapLeft(X16Context c)
    {
        var (code, log) = Capture(X16Tool(c, "llvm-nm"), ["--radix=d", c.Output + ".elf"]);
        Match heap = Regex.Match(log, @"^\s*(\d+)\s+\w\s+__heap_start\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return code == 0 && heap.Success ? Math.Max(0x9F00 - 1024 - int.Parse(heap.Groups[1].Value), 0) : -1;
    }

    /// <summary>
    /// After a link that worked: keeps it if it left more heap than the ones
    /// before, and says whether to try again with more functions in banks. When
    /// not, the output is the best link's.
    /// </summary>
    static bool X16Improve(ref X16Banks? banks, X16Context c, List<string> options)
    {
        int free = X16HeapLeft(c);
        if (banks == null)
        {
            if (free < 0 || free >= Math.Max(X16NoHeap, X16Wanted))
                return false;
            File.Copy(c.Output, c.Output + ".best", overwrite: true);
            File.Copy(c.Output + ".elf", c.Output + ".best.elf", overwrite: true);
            banks = X16Measure(c);
            if (banks == null)
                return false;
            banks.BestFree = free;          // the link without banks, to go back to
        }
        if (free > banks.BestFree)
        {
            banks.BestFree = free;
            banks.BestCount = banks.Count;
            banks.Worse = 0;
            File.Copy(c.Output, c.Output + ".best", overwrite: true);
            File.Copy(c.Output + ".elf", c.Output + ".best.elf", overwrite: true);
        }
        else
        {
            banks.Worse++;
        }
        if (free >= 0 && free < X16Heap && banks.Worse < 3 && banks.Tries++ < 8 && banks.Next < banks.Candidates.Count)
        {
            banks.Goal = banks.Saved + (X16Heap - free) + 512;
            while (banks.Saved < banks.Goal && banks.Next < banks.Candidates.Count)
                banks.Saved += banks.Candidates[banks.Next++].Size - Banking.ThunkSize;
            X16Apply(banks, c, options, $"{free} bytes of heap, trying for more");
            return true;
        }
        X16KeepBest(banks, c);
        return false;
    }

    /// <summary>Puts the best link back as the output. False if no link has worked yet.</summary>
    static bool X16KeepBest(X16Banks? banks, X16Context c)
    {
        if (banks == null || banks.BestFree < 0)
            return false;
        File.Move(c.Output + ".best", c.Output, overwrite: true);
        File.Move(c.Output + ".best.elf", c.Output + ".elf", overwrite: true);
        banks.Count = banks.BestCount;
        return true;
    }

    /// <summary>Moves the functions chosen so far, and sets the options for the next link.</summary>
    static void X16Apply(X16Banks banks, X16Context c, List<string> options, string why)
    {
        var (assignment, count) = Banking.Pack(banks.Candidates.Take(banks.Next), Banking.BankSize - banks.Headroom);
        Console.Error.WriteLine($"[cx16]   {c.Name}: {why}; {assignment.Count} functions in {count} code bank{(count == 1 ? "" : "s")}");
        Banking.Apply(banks.Plain, c.OutDir, assignment);
        c.Whole.Clear();
        c.Whole.AddRange(Directory.GetFiles(c.OutDir, "*.ll").Order(StringComparer.Ordinal));
        c.Separate.Clear();
        string script = Path.Combine(c.OutDir, "banked.ld");
        File.WriteAllText(script, Banking.Script(count));
        banks.Count = count;
        banks.Assignment = assignment;
        options.Clear();
        options.AddRange(["-T", script]);
        if (count > 0)      // none is left when what was chosen turned out too large for a bank
            options.AddRange([$"-DCX16_CODE_BANKS={count}", $"-DCX16_BANK_FILE=\"{Banking.FileName(c.Name)}\""]);
        options.AddRange(X16Defines(c.Whole));
    }

    /// <summary>
    /// The program is too big (lld's log says by how much, and where):
    /// moves more of its largest functions into banks, and sets the options
    /// for the next try. False if there is nothing more to move. Every try
    /// differs from the one before, so that the retries end.
    /// </summary>
    static bool X16MoveToBanks(ref X16Banks? banks, X16Context c, string log, List<string> options)
    {
        // lld's "section '.text' will not fit in region 'ram': overflowed by 766 bytes".
        Match found = Regex.Match(log, @"region '(\w+)': overflowed by (\d+)");
        int excess = int.Parse(found.Groups[2].Value);
        bool bankFull = found.Groups[1].Value == "win";
        if (banks == null)
        {
            banks = X16Measure(c);
            if (banks == null)
                return false;
            banks.Goal = excess + X16Slack;
        }
        else if (bankFull)
        {
            Match section = Regex.Match(log, @"section '\.bank(\d+)' will not fit in region 'win'");
            int full = section.Success ? int.Parse(section.Groups[1].Value) : 0;
            var inIt = banks.Assignment.Where(f => f.Value == full).Select(f => f.Key).ToList();
            if (inIt.Count == 1)
            {
                // Alone in its bank and still too large for it: more room can't help. It
                // stays in fixed RAM, and if the program doesn't fit then, the next link says so.
                int at = banks.Candidates.FindIndex(f => f.Name == inIt[0]);
                banks.Saved -= banks.Candidates[at].Size - Banking.ThunkSize;
                banks.Candidates.RemoveAt(at);
                banks.Next--;
                Console.Error.WriteLine($"[cx16]   {c.Name}: {inIt[0]} is too large for a bank; it stays in fixed RAM");
            }
            else
            {
                // A bank came out bigger than the sizes said: the same functions, more room.
                banks.Headroom += excess + 256;
            }
        }
        else
        {
            // Less was saved than the sizes promised (moved code isn't inlined, thunks
            // are added): what is over now, and the room for the heap again.
            banks.Goal = banks.Saved + excess + X16Slack;
        }

        // Until what is saved reaches the goal, or the functions run out (the room
        // for the heap is a wish: what is over is what has to go), and then it has
        // to be something that wasn't moved yet.
        int before = banks.Next;
        while (banks.Saved < banks.Goal && banks.Next < banks.Candidates.Count)
            banks.Saved += banks.Candidates[banks.Next++].Size - Banking.ThunkSize;
        if (banks.Next == before && !bankFull)
            return false;
        X16Apply(banks, c, options, $"{excess} bytes too big");
        return true;
    }

    /// <summary>
    /// What the runtime has to be compiled with for this program: the file functions
    /// (and the directory's, for lib/Disk.cs) and the math functions (cx16/math.c, 5 KB) only
    /// if the program calls them.
    /// </summary>
    static IEnumerable<string> X16Defines(IEnumerable<string> irFiles)
    {
        if (irFiles.Any(f => Regex.IsMatch(Files.ReadText(f), @"@cell\.(fopen|fread|fwrite|fclose|fseeko|ftello|remove|cx16_dir_open|cx16_dos|__xstat64|__lxstat64|mkdir|rmdir|rename|opendir|getcwd|chdir)\b", RegexOptions.CultureInvariant)))
            yield return "-DCX16_FILES";
        if (irFiles.Any(f => Regex.IsMatch(Files.ReadText(f), @"@cell\.(cx16_dir_open|opendir)\b", RegexOptions.CultureInvariant)))
            yield return "-DCX16_DIR";
        if (irFiles.Any(f => Regex.IsMatch(Files.ReadText(f), @"@cell\.(exp|log|log2|log10|sin|cos|tan|pow|atan2|expf|logf|sinf|cosf|tanhf|powf|sqrt|fabs|floor|ceil|trunc|rint|sqrtf|fabsf|floorf|ceilf|truncf|rintf)\b", RegexOptions.CultureInvariant)))
            yield return "-DCX16_MATH";
    }

    /// <summary>
    /// Before the first link: the options this program's runtime needs. And, for
    /// measuring and testing, ZERO_CX16_BANK=name,name moves every function whose
    /// name contains one of those into banks, whether the program needs it or
    /// not; nothing happens when that isn't set.
    /// </summary>
    static void X16Prepare(ref X16Banks? banks, X16Context c, List<string> options)
    {
        options.AddRange(X16Defines(c.Whole));
        if (options.Contains("-DCX16_MATH"))
        {
            // The math functions (cx16/math.c) are compiled to IR here and linked with the
            // program's own, so that the largest of them can go into banks like the program's
            // functions: they are slow enough for a bank switch to be nothing, and big.
            string source = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(c.Runtime))!, "math.c");
            string ir = Path.Combine(c.OutDir, "xmath.ll");
            Tools.Run(c.Clang, [.. c.Mos, "-S", "-emit-llvm", source, "-o", ir], "compiling math.c");
            c.Whole.Add(ir);
        }
        string? wanted = Environment.GetEnvironmentVariable("ZERO_CX16_BANK");
        if (string.IsNullOrEmpty(wanted))
            return;
        string[] names = wanted.Split(',', StringSplitOptions.RemoveEmptyEntries);
        banks = X16Measure(c) ?? throw Fail($"{c.Name}: can't measure the program's functions");
        var forced = banks.All.Where(f => names.Any(n => f.Name.Contains(n, StringComparison.Ordinal))).ToList();
        if (forced.Count == 0)
            throw Fail($"ZERO_CX16_BANK={wanted}: no function of {c.Name} has such a name");
        banks.Candidates = forced.Concat(banks.Candidates.Except(forced)).ToList();
        banks.Next = forced.Count;
        banks.Saved = forced.Sum(f => f.Size - Banking.ThunkSize);
        X16Apply(banks, c, options, "ZERO_CX16_BANK");
    }

    /// <summary>
    /// What to say about the functions that are too large to go into a bank, one line
    /// each: they take fixed RAM for good, and nothing else would tell. Nothing before
    /// the functions were measured, that is for a program that fits as it is.
    /// </summary>
    static IEnumerable<string> X16TooLarge(X16Banks? banks) => banks == null ? [] : banks.All
        .Where(f => f.Size > X16Largest)
        .Select(f => $"{f.Name} is {f.Size} bytes, too large for a bank ({X16Largest} at most): it stays in fixed RAM. "
            + "Split it, with [MethodImpl(MethodImplOptions.NoInlining)] on the parts, and they can move.");

    /// <summary>The link worked: writes the banks' file next to the program, or removes a stale one if there are none.</summary>
    static void X16Finish(X16Banks? banks, string llvmMos, string outDir, string output, string name)
    {
        foreach (string line in X16TooLarge(banks))
            Console.Error.WriteLine($"[cx16]   {name}: {line}");
        string file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, Banking.FileName(name));
        if (banks == null || banks.Count == 0)
        {
            File.Delete(file);
        }
        else
        {
            Banking.Extract(llvmMos, output + ".elf", banks.Count, outDir, file);
        }
        if (banks == null)
            return;

        // What the heap and the stack have is what the code leaves: say if it isn't much.
        if (banks.BestFree >= 0 && banks.BestFree < Math.Max(3072, X16Wanted))
            Console.Error.WriteLine($"[cx16]   {name}: only {banks.BestFree} bytes are left for the heap; a program that allocates a lot will run out of memory");
    }

    static class Banking
    {
        public const int BankSize = 0x2000;

        /// <summary>What a thunk costs in fixed RAM, about.</summary>
        public const int ThunkSize = 40;

        const RegexOptions Options = RegexOptions.CultureInvariant;
        static readonly Regex Define = new(@"^define\s+(?<pre>.*?)@(?<name>""[^""]+""|[\w.$]+)\((?<rest>.*)$", Options);
        static readonly Regex Attributes = new(@"^attributes (#\d+) = \{(.*)\}", Options);
        static readonly Regex GroupRef = new(@"#\d+", Options);
        static readonly Regex CallingConvention = new(@"\b(fastcc|coldcc|ghccc|swiftcc|cc \d+)\b", Options);

        /// <summary>The functions defined in the IR files, by name.</summary>
        public static HashSet<string> Defined(IEnumerable<string> files)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                foreach (string line in Files.ReadText(file).Split('\n'))
                {
                    Match m = Define.Match(line);
                    if (m.Success && line.EndsWith(" {", StringComparison.Ordinal))
                        names.Add(m.Groups["name"].Value.Trim('"'));
                }
            }
            return names;
        }

        /// <summary>Code symbols and their sizes in bytes, from a linked ELF file, largest first.</summary>
        public static List<(string Name, int Size)> Sizes(string llvmMos, string elf)
        {
            string nm = Path.Combine(llvmMos, "bin", OperatingSystem.IsWindows() ? "llvm-nm.exe" : "llvm-nm");
            var (code, log) = Capture(nm, ["--print-size", "--radix=d", elf]);
            if (code != 0)
                throw Fail($"llvm-nm failed:\n{log}");
            var sizes = new List<(string, int)>();
            foreach (Match m in Regex.Matches(log, @"^\s*\d+\s+(\d+)\s+[Tt]\s+(\S.*?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant))
                sizes.Add((m.Groups[2].Value, int.Parse(m.Groups[1].Value)));
            return sizes.OrderByDescending(s => s.Item2).ThenBy(s => s.Item1, StringComparer.Ordinal).ToList();
        }

        /// <summary>Puts functions into banks of a given capacity, first fit, largest first. Banks are numbered from 1.</summary>
        public static (Dictionary<string, int> Banks, int Count) Pack(IEnumerable<(string Name, int Size)> functions, int capacity)
        {
            var free = new List<int>();
            var assignment = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (name, size) in functions.OrderByDescending(f => f.Size).ThenBy(f => f.Name, StringComparer.Ordinal))
            {
                int bank = free.FindIndex(room => room >= size);
                if (bank < 0)
                {
                    free.Add(capacity);
                    bank = free.Count - 1;
                }
                free[bank] -= size;
                assignment[name] = bank + 1;
            }
            return (assignment, free.Count);
        }

        /// <summary>Writes the IR files of plainDir to outDir, with the chosen functions (name to bank) moved.</summary>
        public static void Apply(string plainDir, string outDir, Dictionary<string, int> banks)
        {
            foreach (string path in Directory.GetFiles(plainDir, "*.ll"))
            {
                string text = Files.ReadText(path);
                string[] lines = text.Split('\n');

                // Attribute groups that rule a function out: it can't be both
                // "always inline" and kept out of line.
                var inlined = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in lines)
                {
                    Match a = Attributes.Match(line);
                    if (a.Success && a.Groups[2].Value.Contains("alwaysinline", StringComparison.Ordinal))
                        inlined.Add(a.Groups[1].Value);
                }

                var result = new List<string>();
                List<string>? thunk = null;
                bool any = false;
                foreach (string line in lines)
                {
                    if (thunk != null)
                    {
                        result.Add(line);
                        if (line == "}")
                        {
                            result.Add("");
                            result.AddRange(thunk);
                            thunk = null;
                        }
                        continue;
                    }
                    Match m = Define.Match(line);
                    if (m.Success && line.EndsWith(" {", StringComparison.Ordinal)
                        && banks.TryGetValue(m.Groups["name"].Value.Trim('"'), out int bank)
                        && Move(line, m, bank, inlined) is var (body, generated))
                    {
                        result.Add(body);
                        thunk = generated;
                        any = true;
                        continue;
                    }
                    result.Add(line);
                }
                if (any)
                {
                    result.Add("");
                    result.Add("@__rambank = external global i8");
                }
                Files.WriteText(Path.Combine(outDir, Path.GetFileName(path)), string.Join("\n", result));
            }
        }

        /// <summary>The renamed definition's first line and the thunk, or null for a function that can't move.</summary>
        static (string Body, List<string> Thunk)? Move(string line, Match m, int bank, HashSet<string> inlined)
        {
            string pre = m.Groups["pre"].Value, name = m.Groups["name"].Value, rest = m.Groups["rest"].Value;
            int end = ParametersEnd(rest);
            if (end < 0)
                return null;
            string parameters = rest[..end];
            string after = rest[(end + 1)..];                    // " #3 minsize optsize {"
            after = after[..^2];
            if (Regex.IsMatch(after, @"\bsection\b", Options) || Regex.IsMatch(after, @"\balwaysinline\b", Options)
                || GroupRef.Matches(after).Any(g => inlined.Contains(g.Value)))
                return null;

            string quoted = name.StartsWith('"') ? "\"" + name.Trim('"') + "__bank\"" : name + "__bank";
            string noinline = Regex.IsMatch(after, @"\bnoinline\b", Options) ? "" : " noinline";
            // Metadata attachments ("!guid !22") come after the attributes and the section.
            int metadata = after.IndexOf(" !", StringComparison.Ordinal);
            string attributes = metadata < 0 ? after : after[..metadata], attached = metadata < 0 ? "" : after[metadata..];
            string body = $"define {pre}@{quoted}({parameters}){attributes}{noinline} section \".bank{bank}\"{attached} {{";

            var arguments = new List<string>();
            foreach (string parameter in SplitParameters(parameters))
            {
                string[] tokens = parameter.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2 || !tokens[^1].StartsWith('%'))
                    return null;
                arguments.Add($"{tokens[0]} {tokens[^1]}");
            }
            string[] preTokens = pre.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            string result = preTokens[^1];
            Match cc = CallingConvention.Match(pre);
            string convention = cc.Success ? cc.Value + " " : "";
            bool isVoid = result == "void";

            var thunk = new List<string>
            {
                $"define {pre}@{name}({parameters}) noinline minsize optsize {{",
                "entry:",
                "  %mos.bank = load volatile i8, ptr @__rambank, align 1",
                $"  store volatile i8 {bank}, ptr @__rambank, align 1",
                $"  {(isVoid ? "" : "%mos.result = ")}call {convention}{result} @{quoted}({string.Join(", ", arguments)})",
                "  store volatile i8 %mos.bank, ptr @__rambank, align 1",
                isVoid ? "  ret void" : $"  ret {result} %mos.result",
                "}",
            };
            return (body, thunk);
        }

        /// <summary>Where the parameter list ends: the index of its closing bracket.</summary>
        static int ParametersEnd(string rest)
        {
            int depth = 0;
            for (int i = 0; i < rest.Length; i++)
            {
                if (rest[i] == '(')
                    depth++;
                else if (rest[i] == ')' && depth-- == 0)
                    return i;
            }
            return -1;
        }

        static List<string> SplitParameters(string text)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            int depth = 0;
            foreach (char c in text)
            {
                if (c is '(' or '{' or '<' or '[')
                    depth++;
                else if (c is ')' or '}' or '>' or ']')
                    depth--;
                if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            if (current.ToString().Trim().Length > 0)
                parts.Add(current.ToString().Trim());
            return parts;
        }

        // ---- linking ----------------------------------------------------------------------

        /// <summary>
        /// llvm-mos's X16 linker script, with the banks added: each .bankN at $A000
        /// (an overlay, loaded one after another far above the machine's memory,
        /// where the PRG file doesn't reach). With banks = 0 and room = true it only
        /// gives the program far more fixed RAM than there is, to measure it.
        /// </summary>
        public static string Script(int banks, bool room = false)
        {
            var s = new StringBuilder();
            s.AppendLine("__basic_zp_start = 0x0002;");
            s.AppendLine("__basic_zp_end = 0x0080;");
            s.AppendLine("MEMORY {");
            s.AppendLine($"    ram (rw) : ORIGIN = 0x0801, LENGTH = {(room ? "0x70000" : "0x96ff")}");
            s.AppendLine("    win (rx) : ORIGIN = 0xA000, LENGTH = 0x2000");
            s.AppendLine("    img (r)  : ORIGIN = 0x100000, LENGTH = 0x100000");
            s.AppendLine("}");
            s.AppendLine("INCLUDE commodore.ld");
            if (banks > 0)
            {
                s.AppendLine("SECTIONS {");
                s.AppendLine("    OVERLAY 0xA000 : NOCROSSREFS AT(0x100000) {");
                for (int i = 1; i <= banks; i++)
                    s.AppendLine($"        .bank{i} {{ *(.bank{i}) }}");
                s.AppendLine("    } > win");
                s.AppendLine("}");
            }
            s.AppendLine("__rambank = 0;");
            s.AppendLine("__stack = 0x9f00;");
            s.AppendLine("OUTPUT_FORMAT {");
            s.AppendLine("    SHORT(ORIGIN(ram))");
            s.AppendLine("    TRIM(ram)");
            s.AppendLine("}");
            return s.ToString();
        }

        /// <summary>The name of the file with the banks: the program's, at most 12 characters, in capitals (a CBM DOS name has 16 with its extension).</summary>
        public static string FileName(string program)
        {
            string name = new string(program.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
            return (name.Length > 12 ? name[..12] : name) + ".BNK";
        }

        /// <summary>Writes the banks of a linked ELF file as one file a KERNAL LOAD can read into $A000: that address, then 8 KB for each bank.</summary>
        public static void Extract(string llvmMos, string elf, int banks, string workDir, string output)
        {
            string objcopy = Path.Combine(llvmMos, "bin", OperatingSystem.IsWindows() ? "llvm-objcopy.exe" : "llvm-objcopy");
            var image = new List<byte> { 0x00, 0xA0 };
            for (int i = 1; i <= banks; i++)
            {
                string part = Path.Combine(workDir, $"bank{i}.bin");
                Tools.Run(objcopy, [$"--dump-section=.bank{i}={part}", elf, Path.Combine(workDir, "unused.elf")], $"extracting bank {i}");
                byte[] bytes = File.ReadAllBytes(part);
                if (bytes.Length > BankSize)
                    throw Fail($"bank {i} is {bytes.Length} bytes, more than {BankSize}");
                image.AddRange(bytes);
                image.AddRange(new byte[BankSize - bytes.Length]);
            }
            File.WriteAllBytes(output, image.ToArray());
        }
    }
}
