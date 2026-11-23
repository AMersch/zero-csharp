# zerocore

A core library for C# programs compiled with [bflat](https://flattened.net/) that run without .NET's runtime: no garbage collector, no exceptions you can catch, no reflection. It takes the place of bflat's built-in `zerolib` (`--stdlib:zero`), under the MIT license, in about 2,400 lines of C# and under 100 lines of assembly.

A core library is the assembly that defines `object`, `int`, `string` and the other types C# itself is built on. It also has to contain the runtime code the native compiler expects to call: allocating objects, checking casts, calling interface methods, setting up static fields. With zerocore, that code is all a program gets linked with besides the C library.

## Using it

`../build.sh` does this for the samples. By hand, for Linux:

```bash
# 1. The library. bflat only accepts a custom core library under this name.
bflat build-il $(find zerocore -name '*.cs') -O --stdlib:none --langversion preview \
    -o System.Private.CoreLib.dll

# 2. Its assembly routines, for the target architecture (x64.S or arm64.S).
as zerocore/native/arm64.S -o zerocore-arm64.o

# 3. Your program.
bflat build Program.cs --stdlib:none -r System.Private.CoreLib.dll \
    --no-reflection -i libc --ldflags zerocore-arm64.o -o program
```

- `--stdlib:none` tells bflat not to add a library of its own.
- `--no-reflection` is required. bflat assumes a library named `System.Private.CoreLib` is .NET's and would otherwise look for its reflection support ("Failed to load type 'Internal.Metadata.NativeFormat.TypeDefinition'").
- `-i <library>` is needed for every library you `[DllImport]`, here `libc`. It binds the functions at link time. Without it bflat binds them when first called, which needs runtime support zerocore doesn't have ("Expected type 'Internal.Runtime.CompilerHelpers.InteropHelpers' not found").

For WebAssembly, see `build_wasm` in `../build.sh`: the library is built with `-d WASM`, and `../wasm/runtime.c` replaces the assembly file. DOS (`build_dos_*` there) uses the same build of the library, with `../dos/runtime.c`. UEFI (`build_uefi` there) uses the ordinary build with `--os uefi`, `native/win-x64.S` or `native/win-arm64.S`, and `../efi/` in place of the C library.

## What's in it

| File | What |
|---|---|
| `System/Primitives.cs`, `Object.cs`, `Array.cs`, `Attributes.cs`, `Collections.cs` | The types and attributes the C# compiler requires: primitives, `object`, `ValueType`, `Enum`, `Nullable<T>`, `Array`, delegates (`Action`, `Func`), `ValueTuple` (2 to 7 elements), `IDisposable`, `IEnumerable` |
| `Runtime/ObjectAllocator.cs` | Where each thread's objects come from: `calloc` by default, or a function a library sets for that thread (`../lib/Arena.cs`'s `AllocateObjectsHere` sends them to an arena); and a scan of static fields for debug checks |
| `System/Equality.cs`, `System/Type.cs`, `System/Number.cs`, `System/Text/StringBuilder.cs` | What records, anonymous types and `"n=" + 5` need: `object`'s `Equals`, `GetHashCode` and `ToString` (overridden by every primitive, `string`, structs and enums), `EqualityComparer<T>`, `IEquatable<T>`, `string.Format` (`{0}` holes), `StringBuilder`, and `Type` for `typeof` and `GetType()`, one per type, compared by identity |
| `System/GC.cs` | `GC.GetTotalAllocatedBytes()`: every byte allocated so far, so a test can check that a loop allocates nothing |
| `System/String.cs` | `string`: `Length`, indexer, `==`, `!=`, `Equals`, `IsNullOrEmpty`, `Concat` (so `a + b` and `switch` on strings work) |
| `System/Span.cs` | `Span<T>` and `ReadOnlySpan<T>`: indexing, `Slice`, `CopyTo`, `Fill`, `Clear`, `foreach`, `fixed` |
| `System/Exceptions.cs` | `Exception` and a few standard subclasses (`InvalidOperationException`, `ArgumentException`, …), for `throw` as a panic and for code the compiler generates |
| `System/Unions.cs` | `IUnion` and `UnionAttribute`, which C# 15 `union` declarations need |
| `System/Debug.cs` | `Debug.Assert` and `Debug.Fail`, for builds with `DEBUG` defined: print the condition, member, file and line, then `FailFast`. They use the caller-info attributes in `System/Attributes.cs` (`CallerMemberName`, `CallerLineNumber`, `CallerFilePath`, `CallerArgumentExpression`) |
| `System/Environment.cs` | `Environment.FailFast`, which is what every failure ends in; `Exit`, `GetEnvironmentVariable`, `ProcessorCount`, `TickCount64` and `NewLine` (`"\n"`) |
| `Runtime/RuntimeExports.cs` | Allocation (`calloc`, never freed), boxing and unboxing; `throw` (print the message, abort) |
| `Runtime/TypeCast.cs` | `is`, `as` and casts to classes and interfaces; type-checked stores into arrays |
| `Runtime/InterfaceDispatch.cs` | Calling an interface method on an object whose class isn't known at build time, with a small cache per call site |
| `System/Math.cs` | `Math` and `MathF`: the processor's instruction where there is one, the C math library otherwise |
| `System/Half.cs` | `Half` (IEEE half precision, as in .NET: conversions round to nearest even, `ToString` gives the shortest text that reads back) and `BitConverter`'s bit conversions for `Half`, `float` and `double` |
| `System/Numerics/BitOperations.cs` | `BitOperations`: `PopCount`, `LeadingZeroCount`, `TrailingZeroCount`, `Log2`, rotates, `IsPow2`. clz, rbit and cnt on arm64; bsr and bsf on x64, lzcnt, tzcnt and popcnt with `-m x86-64-v3`; portable code on WebAssembly and DOS |
| `System/Collections/BitArray.cs` | `BitArray`, with .NET's members (`And`, `Or`, `Xor`, `Not`, shifts, `HasAllSet`, `CopyTo`, ...) |
| `System/Threading.cs` | `Interlocked` (atomic instructions) and `Volatile` |
| `Runtime/Startup.cs` | What runs before `Main`: storage for static fields that hold references, `string[] args`. Also static constructors |
| `Runtime/MethodTable.cs` | The layout of the type descriptions the compiler emits |
| `Runtime/Unsafe.cs` | `Unsafe`, `RuntimeHelpers`, `MemoryMarshal` |
| `Runtime/Platform.cs` | Everything used from outside: `calloc`, `write`, `abort` |
| `Intrinsics/*.cs` | `Vector64/128/256<T>` and the NEON, AVX, AVX2 and FMA operations `../lib/Kernels.cs` uses; the bit instructions `BitOperations` uses |
| `native/arm64.S`, `native/x64.S` | Three routines per architecture that need a calling convention C# can't express: two for storing object references, one for interface calls |
| `native/mac-arm64.S` | `arm64.S` as a Mach-O object, for macOS |
| `native/win-x64.S` | The same for Windows x64's calling convention, plus the stack probe Windows code calls for frames larger than a page. With `../win/start.c` and `../win/runtime.c` (entry point, `write`, `read`) it is all a Windows build adds. UEFI x64 uses it too |
| `native/win-arm64.S` | `arm64.S` as a COFF object, for UEFI on arm64. Windows' arm64 calling convention agrees with Linux's for every register these routines use |

Most of the library is a contract with the compiler: it looks types, methods and fields up by name (`System.Runtime.TypeCast.CheckCastClass`, `String._stringLength`, ...) and fails with "Expected type ... not found" when one is missing. Those names can't be changed.

## What works

- Classes, structs, generics, virtual methods, interfaces (including default methods and static abstract members), delegates and lambdas
- Records (`record`, `record struct`) and anonymous types; `ToString()`, `Equals` and `GetHashCode` on every type; `"n=" + 5`; `typeof` and `GetType()` for comparing types
- `is`, `as` and casts to classes and interfaces; pattern matching on types; boxing
- Arrays of any type, `Span<T>`, `stackalloc`, function pointers, P/Invoke
- Strings: comparison, concatenation, `switch`; literals; UTF-8 literals (`"..."u8`)
- Static fields of every type, static constructors, array initializers (`new[] { 1, 2, 3 }`)
- `Math`, `MathF`, `Interlocked`, `Volatile`
- `Main()`, `Main(string[] args)` and top-level statements
- `try`/`finally` and `using` (but see exceptions below)
- Extension methods and C# 14 extension members (properties, static members, `ref` receivers)
- C# 15 unions (`union Result(Ok, Err);`) and `switch` on them; a struct case is boxed, and never freed
- `throw`, as a panic: it prints the exception's message to stderr and aborts

A failure that would throw in .NET (a failed cast, an index out of range, an arithmetic overflow in `checked` code) prints its name to stderr and aborts. A null dereference is a plain segmentation fault. Integer division by zero aborts with a message on arm64 and is a hardware trap on x64.

## What doesn't

- **Freeing memory.** Every `new`, boxing and string concatenation calls `calloc`, and nothing is ever freed. Use it for objects that live as long as the program, and an arena (`../lib/Arena.cs`) for the rest.
- **Catching exceptions.** `throw` compiles, and `catch` compiles, but nothing unwinds: `RhpThrowEx` prints `Unhandled exception: ` and the message, then aborts. No `catch` block ever runs, and neither do the `finally` blocks and `using` disposals between the `throw` and `Main`. `try`/`finally` and `using` work on the normal path: the `finally` block runs when the `try` block ends. Without reflection there's no type name to print, so the standard exception types use their name as the default message.
- **Variance.** Casting or dispatching through it (`IEnumerable<string>` as `IEnumerable<object>`, `string[]` as `object[]`) aborts with a message saying so instead of answering wrongly. Arrays don't implement `IList<T>` or `IEnumerable<T>`.
- **Interface calls cost a little more than virtual calls.** When the compiler can't tell which class is behind an interface, the call goes through a cache of the last four classes seen at that call site: about 1.6 ns on an Apple M-series core against 0.9 ns for a virtual call, and 3 to 4 ns when the site alternates between two to four classes. A site that sees more than four pays for a full search (about 5 ns) on each of the extras. In a hot loop, generics with a `struct` constraint or static abstract members are still faster: they're resolved at build time.
- **Most of the base class library.** No `Console` (use `../lib/Out.cs`), reflection (a `Type` only compares; a class without its own `ToString` prints `object`), collections, LINQ (see `../lib/Seq.cs`), async, `Thread`, or `Nullable<T>` boxing.
- **SIMD operations beyond the ones declared.** To add one, declare it in `Intrinsics/` with the name and signature it has in .NET; the compiler does the rest.
- **Windows on arm64 and Intel Macs.** Linux (x64, arm64), macOS on Apple Silicon, Windows x64, WebAssembly (WASI), 32-bit DOS on a 386 (see the main README; it uses the WebAssembly build of this library) and UEFI (x64, arm64, with no operating system) only.
- **A threading library.** There's no `Thread` or `lock`; `../lib/Threads.cs` has `Parallel.For`, on pthreads. Allocation, interface calls and `Interlocked` are safe to use from several threads (tested with eight on arm64). So are static constructors: the first thread to touch a type runs its constructor, and the others wait for it to finish.

## Where the code comes from, and the license

zerocore is MIT-licensed (see `LICENSE`). Its purpose is to let programs built with bflat avoid linking zerolib, which is AGPL-3.0.

It was written for this repository against two MIT-licensed sources: the .NET runtime's own NativeAOT libraries ([dotnet/runtime](https://github.com/dotnet/runtime), `src/coreclr/nativeaot`, mainly `Test.CoreLib` and `Runtime.Base`) for the data layouts and helper signatures, and the compiler's error messages for what else it requires. `System/SpanHelpers.cs` is adapted from dotnet/runtime directly and says so.

One caveat, stated plainly: this is not a clean-room implementation. It was written with AI assistance in a session that had earlier read and modified zerolib's source. Care was taken to write each part from the MIT sources and the compiler's contract instead, and the required names are the compiler's, not zerolib's, but nobody has compared the two line by line. If you plan to ship a commercial product on it, have someone qualified review it first. This isn't legal advice.

bflat itself (the compiler driver) remains AGPL-3.0. Using it to compile a program doesn't put its code in the output; zerolib was the part that ended up in every binary.
