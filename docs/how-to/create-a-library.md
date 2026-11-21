---
title: Create a reusable library
index: 14
---

# Create a reusable library

Code that several programs need goes in a library: a folder in `libs/`. `build.sh` compiles each
folder the way it compiles `lib/`, into an IL assembly that every program references. Each program's
build then compiles the parts it uses into its own binary, so there's nothing to ship next to the
program, and the same library works on every target. `libs/Geometry` is an example, and
`samples/Shapes.cs` uses it.

## Make the library

Make a folder in `libs/` and put `.cs` files in it. The folder's name is the library's name. A library
has no `Main`. Give it a namespace of its own, so its names don't collide with a program's:

<!-- sample: libs/Geometry/Shapes.cs types -->
```csharp
public interface IShape
{
    double Area { get; }
    double Perimeter { get; }
}

public record struct Point(double X, double Y);

public record struct Circle(double Radius) : IShape
{
    public double Area => Math.PI * Radius * Radius;
    public double Perimeter => 2 * Math.PI * Radius;
}

public record struct Rect(double Width, double Height) : IShape
{
    public double Area => Width * Height;
    public double Perimeter => 2 * (Width + Height);
}
```

A library can use everything a program can: zerocore, `lib/` (`Arena`, `List`, `Out` and the rest),
and the libraries before it in `libs/`.

## Use it from a program

Every program references every library, so a `using` is all a program needs:

<!-- sample: samples/Shapes.cs use -->
```csharp
Span<Rect> rooms = stackalloc Rect[]
{
    new Rect(4, 3), new Rect(2.5, 2), new Rect(5, 4),
};
Out.Line($"Floor area: {Measure.TotalArea<Rect>(rooms):F2}");

var table = new Circle(0.5);
Out.Line($"Table: area {table.Area:F2}, perimeter {table.Perimeter:F2}");

Arena arena = Arena.Create(1024, "shapes");
List<Point> corners = Measure.Corners(rooms[0], arena);
for (int i = 0; i < corners.Count; i++)
    Out.Line(corners.Get(i).ToString());
unsafe { arena.Destroy(); } // SAFETY: corners isn't used after this.
```

Build and run it as any other program. `build.sh` builds the library first:

```bash
./build.sh Shapes
./run.sh Shapes
```

```text
Floor area: 37.00
Table: area 0.79, perimeter 3.14
Point { X = 0, Y = 0 }
Point { X = 4, Y = 0 }
Point { X = 4, Y = 3 }
Point { X = 0, Y = 3 }
PASS Geometry
```

The same works with `--win`, `--mac`, `--wasm` and `--dos`: a library is compiled for no target in
particular, so one build of it serves them all.

## How it's built

`build.sh` runs bflat twice. First it compiles the library's sources to an IL assembly, against
zerocore and `lib/`:

```bash
bflat build-il libs/Geometry/*.cs -O --target Shared --stdlib:none \
    -r bin/System.Private.CoreLib.dll -r bin/BflatLib.dll \
    --langversion preview --compiler-feature updated-memory-safety-rules \
    -o bin/libs/Geometry.dll
```

Then each program's build references it with `-r bin/libs/Geometry.dll`. The assembly holds IL, not
machine code, and its metadata takes the place of a C header. The program's build compiles the
library's code together with the program's own, for the program's target and `-m` flags, and leaves
out whatever the program doesn't call. A program that uses one function of a large library gets that
function and what it calls.

`-O` matters: `build-il` doesn't optimize unless asked, and the program's build starts from the IL it
is given.

## Design the library for zero mode

A library runs inside programs with no garbage collector and no exceptions you can catch, so a few
habits make it easier to use.

**Make generic code take structs.** A generic method with a `struct` constraint is compiled again
for each type a program calls it with, so calls to the interface's members are direct calls, and
nothing is boxed:

<!-- sample: libs/Geometry/Measure.cs generic -->
```csharp
// Compiled again, for each T, into the program that calls it: no boxing,
// no interface calls.
public static double TotalArea<T>(ReadOnlySpan<T> shapes)
    where T : struct, IShape
{
    double total = 0;
    foreach (T shape in shapes)
        total += shape.Area;
    return total;
}
```

**Let the caller say where memory comes from.** Nothing is freed unless an arena frees it, and only
the caller knows how long a result must live. So a function whose result outlives the call takes an
`Arena` and allocates there, instead of calling `new` or keeping an arena of its own:

<!-- sample: libs/Geometry/Measure.cs arena -->
```csharp
// The corners of a rectangle with its lower left corner at the origin,
// counter-clockwise, in a list that lives in the caller's arena.
public static List<Point> Corners(Rect rect, Arena arena)
{
    List<Point> corners = List<Point>.Create(arena, 4);
    corners.Add(new Point(0, 0));
    corners.Add(new Point(rect.Width, 0));
    corners.Add(new Point(rect.Width, rect.Height));
    corners.Add(new Point(0, rect.Height));
    return corners;
}
```

**Return errors, don't throw them.** A `throw` in a library ends the program that called it, which
can't catch it. Return a `bool` with an `out` value, or a [union](../concepts/limits.html) of a result
and an error, and let the program decide.

**Keep helpers `internal`.** Programs can't see `internal` members, so the library can change them
without breaking anyone:

<!-- sample: libs/Geometry/Measure.cs internal -->
```csharp
// Whether two areas are equal to within rounding. internal: programs
// can't call it, so the library can change it freely.
internal static bool Near(double a, double b)
    => Math.Abs(a - b) < 1e-9 * (1 + Math.Abs(a));

public static bool SameArea<A, B>(in A a, in B b)
    where A : struct, IShape
    where B : struct, IShape
    => Near(a.Area, b.Area);
```

A library's static fields work as in a program: one copy per program, set up by static constructors
as usual. A program has no other copy of the library to share them with.

## Use one library from another

`build.sh` builds the folders in `libs/` in name order, and each can use the ones before it. A
`libs/Plots` could use `libs/Geometry`, but not the other way round. If two libraries need each
other, they are one library: merge the folders.

## Share a library

The simplest way to share a library is as source: copy its folder into another checkout's `libs/`.

The `.dll` can be shared too, but it refers to zerocore and `lib/` by name and expects the members it
was built against. Rebuild it whenever either changes, and build programs against the same zerocore
and `lib/` it was built with.

## What a library isn't

A library is compiled into each program that uses it. It isn't loaded at run time: there's no runtime
to load it into, and `build.sh` doesn't build native shared libraries (`.so`, `.dylib`, `.dll`) for C
programs to call.
