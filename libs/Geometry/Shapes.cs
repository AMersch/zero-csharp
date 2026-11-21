// An example library: shapes and their measurements. build.sh compiles every
// .cs file in this folder into bin/libs/Geometry.dll, which every program
// references. samples/Shapes.cs uses it; docs/how-to/create-a-library.md
// explains it.

using System;

namespace Geometry;

// docs:types
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
// docs:end
