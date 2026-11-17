using System.Runtime;
using System.Runtime.CompilerServices;

namespace System
{
    public static unsafe class Environment
    {
        // Zero mode has no exceptions: anything that would throw ends the
        // process here, after printing the reason to stderr.
        public static void FailFast(string message)
        {
            if (message != null)
            {
                byte* buffer = stackalloc byte[256];
                int length = 0;
                for (int i = 0; i < message.Length && length < 255; i++)
                {
                    char c = message[i];
                    buffer[length++] = c < 0x80 ? (byte)c : (byte)'?';
                }
                buffer[length++] = (byte)'\n';
                Platform.write(2, buffer, (nuint)length);
            }
            Platform.abort();
            while (true) { }
        }
    }
}
