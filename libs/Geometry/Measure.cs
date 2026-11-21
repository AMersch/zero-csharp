// The library's functions. A library has no Main and allocates nothing of
// its own: memory that must outlive a call comes from the caller's arena.

using System;

namespace Geometry;

public static class Measure
{
    // docs:generic
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
    // docs:end

    // docs:arena
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
    // docs:end

    // docs:internal
    // Whether two areas are equal to within rounding. internal: programs
    // can't call it, so the library can change it freely.
    internal static bool Near(double a, double b)
        => Math.Abs(a - b) < 1e-9 * (1 + Math.Abs(a));

    public static bool SameArea<A, B>(in A a, in B b)
        where A : struct, IShape
        where B : struct, IShape
        => Near(a.Area, b.Area);
    // docs:end
}
