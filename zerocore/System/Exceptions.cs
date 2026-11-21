// Exceptions in zero mode are panics, like Rust's panic = "abort": `throw`
// prints the exception's message to stderr and ends the program. Nothing
// unwinds, so on that path no `catch` block runs and neither do `finally`
// blocks or `using` disposals. RhpThrowEx (Runtime/RuntimeExports.cs) does it.
//
// The C# compiler also needs these types for code it generates:
// - each C# 14 `extension(...)` block gets a hidden type whose method bodies
//   are `throw new NotSupportedException()` (never called);
// - a `switch` on a C# 15 union throws InvalidOperationException for a value
//   that matches no case, such as `default(Result)`.
//
// Without reflection there's no type name to print, so each type here passes
// its own name as the message when none is given.

namespace System
{
    public class Exception
    {
        private readonly string _message;

        public Exception() : this("Exception") { }

        public Exception(string message)
        {
            _message = message;
        }

        public virtual string Message => _message;
    }

    public class InvalidOperationException : Exception
    {
        public InvalidOperationException() : base("InvalidOperationException") { }
        public InvalidOperationException(string message) : base(message) { }
    }

    public class NotSupportedException : Exception
    {
        public NotSupportedException() : base("NotSupportedException") { }
        public NotSupportedException(string message) : base(message) { }
    }

    public class NotImplementedException : Exception
    {
        public NotImplementedException() : base("NotImplementedException") { }
        public NotImplementedException(string message) : base(message) { }
    }

    public class ArgumentException : Exception
    {
        public ArgumentException() : base("ArgumentException") { }
        public ArgumentException(string message) : base(message) { }
    }

    public class ArgumentNullException : ArgumentException
    {
        public ArgumentNullException() : base("ArgumentNullException") { }
        public ArgumentNullException(string paramName) : base("ArgumentNullException: " + paramName) { }
    }

    public class ArgumentOutOfRangeException : ArgumentException
    {
        public ArgumentOutOfRangeException() : base("ArgumentOutOfRangeException") { }
        public ArgumentOutOfRangeException(string paramName) : base("ArgumentOutOfRangeException: " + paramName) { }
    }
}
