// dotnet run of an iOS simulator program: starts in the project's folder
// (marker.txt is there), gets its arguments, and its exit code comes back.
public static class Program
{
    public static int Main(string[] args)
    {
        Out.Line(File.Exists("marker.txt") ? "in the project folder" : "elsewhere");
        foreach (string arg in args)
            Out.Line(arg);
        return 3;
    }
}
