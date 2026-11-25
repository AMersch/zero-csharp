// Console.WriteLine exists in zero mode, but Console.Error doesn't, so this
// must not compile.
public static class Uses
{
    public static void Run() => System.Console.Error.WriteLine("hi");
}
