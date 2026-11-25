// Addresses: .NET's System.Net types for IP addresses, endpoints and name
// lookup, so code written against them compiles here. The sockets that use
// them are in lib/Sockets.cs.
//
//   IPAddress address = IPAddress.Parse("192.168.1.10");
//   var endpoint = new IPEndPoint(IPAddress.Loopback, 8080);   // 127.0.0.1:8080
//   IPAddress[] found = Dns.GetHostAddresses("example.com");
//
// What differs from .NET: Parse takes the usual forms only (four decimal
// numbers for IPv4; hexadecimal groups, "::" and a trailing IPv4 part for
// IPv6, with an optional numeric %scope), and a throw ends the program, so
// TryParse and Dns.TryGetHostAddresses are the ones to use on input that may
// be wrong. Dns asks the system (getaddrinfo), which waits for the answer
// without letting other fibers run.

using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace System.Net;

/// <summary>An address a socket can bind or connect to.</summary>
public abstract class EndPoint
{
    /// <summary>The kind of address.</summary>
    public virtual AddressFamily AddressFamily => AddressFamily.Unspecified;
}

/// <summary>An IPv4 or IPv6 address.</summary>
public class IPAddress
{
    readonly uint _v4;          // IPv4: a.b.c.d is a << 24 | b << 16 | c << 8 | d
    readonly ushort[] _v6;      // IPv6: the eight 16-bit groups; null for IPv4
    long _scopeId;

    /// <summary>0.0.0.0: every IPv4 address of this machine, to bind to.</summary>
    public static readonly IPAddress Any = new IPAddress(0u);
    /// <summary>127.0.0.1.</summary>
    public static readonly IPAddress Loopback = new IPAddress(0x7F000001u);
    /// <summary>255.255.255.255.</summary>
    public static readonly IPAddress Broadcast = new IPAddress(0xFFFFFFFFu);
    /// <summary>255.255.255.255, as .NET has it: no address.</summary>
    public static readonly IPAddress None = Broadcast;
    /// <summary>::, every IPv6 address of this machine, to bind to.</summary>
    public static readonly IPAddress IPv6Any = new IPAddress(new ushort[8], 0);
    /// <summary>::1.</summary>
    public static readonly IPAddress IPv6Loopback = new IPAddress(new ushort[] { 0, 0, 0, 0, 0, 0, 0, 1 }, 0);
    /// <summary>::, as .NET has it: no IPv6 address.</summary>
    public static readonly IPAddress IPv6None = IPv6Any;

    IPAddress(uint v4) => _v4 = v4;

    IPAddress(ushort[] groups, long scopeId)
    {
        _v6 = groups;
        _scopeId = scopeId;
    }

    /// <summary>
    /// An address from its bytes in network order: 4 for IPv4, 16 for IPv6.
    /// Any other length ends the program.
    /// </summary>
    public IPAddress(byte[] address) : this(new ReadOnlySpan<byte>(address ?? throw new ArgumentNullException(nameof(address)))) { }

    /// <summary>An IPv6 address from its 16 bytes, with a scope (the interface's number).</summary>
    public IPAddress(byte[] address, long scopeid) : this(new ReadOnlySpan<byte>(address ?? throw new ArgumentNullException(nameof(address))))
    {
        if (_v6 == null)
            throw new ArgumentException("IPAddress: a scope is for IPv6 addresses");
        ScopeId = scopeid;
    }

    /// <summary>An address from its bytes in network order: 4 for IPv4, 16 for IPv6.</summary>
    public IPAddress(ReadOnlySpan<byte> address)
    {
        if (address.Length == 4)
        {
            _v4 = (uint)address[0] << 24 | (uint)address[1] << 16 | (uint)address[2] << 8 | address[3];
        }
        else if (address.Length == 16)
        {
            _v6 = new ushort[8];
            for (int i = 0; i < 8; i++)
                _v6[i] = (ushort)(address[2 * i] << 8 | address[2 * i + 1]);
        }
        else
        {
            throw new ArgumentException("IPAddress: an address is 4 bytes (IPv4) or 16 (IPv6)");
        }
    }

    /// <summary>InterNetwork for IPv4, InterNetworkV6 for IPv6.</summary>
    public AddressFamily AddressFamily => _v6 == null ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6;

    /// <summary>The IPv6 scope: which interface a link-local address is on. Setting it on an IPv4 address ends the program.</summary>
    public long ScopeId
    {
        get
        {
            if (_v6 == null)
                throw new NotSupportedException("IPAddress: an IPv4 address has no scope");
            return _scopeId;
        }
        set
        {
            if (_v6 == null)
                throw new NotSupportedException("IPAddress: an IPv4 address has no scope");
            if (value < 0 || value > uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));
            _scopeId = value;
        }
    }

    /// <summary>Whether this is an IPv6 address holding an IPv4 one (::ffff:a.b.c.d).</summary>
    public bool IsIPv4MappedToIPv6
        => _v6 != null && _v6[0] == 0 && _v6[1] == 0 && _v6[2] == 0 && _v6[3] == 0 && _v6[4] == 0 && _v6[5] == 0xFFFF;

    /// <summary>Whether this is an IPv6 link-local address (fe80::/10).</summary>
    public bool IsIPv6LinkLocal => _v6 != null && (_v6[0] & 0xFFC0) == 0xFE80;

    /// <summary>Whether the address is a loopback one: 127.x.x.x, ::1, or ::ffff:127.x.x.x.</summary>
    public static bool IsLoopback(IPAddress address)
    {
        if (address == null)
            throw new ArgumentNullException(nameof(address));
        if (address._v6 == null)
            return address._v4 >> 24 == 127;
        if (address.IsIPv4MappedToIPv6)
            return address._v6[6] >> 8 == 127;
        ushort[] g = address._v6;
        return g[0] == 0 && g[1] == 0 && g[2] == 0 && g[3] == 0 && g[4] == 0 && g[5] == 0 && g[6] == 0 && g[7] == 1;
    }

    /// <summary>The address as IPv6: an IPv4 one becomes ::ffff:a.b.c.d.</summary>
    public IPAddress MapToIPv6()
    {
        if (_v6 != null)
            return this;
        return new IPAddress(new ushort[] { 0, 0, 0, 0, 0, 0xFFFF, (ushort)(_v4 >> 16), (ushort)_v4 }, 0);
    }

    /// <summary>The address as IPv4: an IPv6 one keeps its last 4 bytes.</summary>
    public IPAddress MapToIPv4()
    {
        if (_v6 == null)
            return this;
        return new IPAddress((uint)_v6[6] << 16 | _v6[7]);
    }

    /// <summary>The address's bytes in network order: 4 or 16.</summary>
    public byte[] GetAddressBytes()
    {
        byte[] bytes = new byte[_v6 == null ? 4 : 16];
        TryWriteBytes(bytes, out _);
        return bytes;
    }

    /// <summary>Writes the address's bytes in network order; false if they don't fit.</summary>
    public bool TryWriteBytes(Span<byte> destination, out int bytesWritten)
    {
        int length = _v6 == null ? 4 : 16;
        bytesWritten = 0;
        if (destination.Length < length)
            return false;
        if (_v6 == null)
        {
            destination[0] = (byte)(_v4 >> 24);
            destination[1] = (byte)(_v4 >> 16);
            destination[2] = (byte)(_v4 >> 8);
            destination[3] = (byte)_v4;
        }
        else
        {
            for (int i = 0; i < 8; i++)
            {
                destination[2 * i] = (byte)(_v6[i] >> 8);
                destination[2 * i + 1] = (byte)_v6[i];
            }
        }
        bytesWritten = length;
        return true;
    }

    /// <summary>Parses an address; text that isn't one ends the program (use TryParse for input).</summary>
    public static IPAddress Parse(string ipString)
    {
        if (ipString == null)
            throw new ArgumentNullException(nameof(ipString));
        return Parse(ipString.AsSpan());
    }

    /// <summary>Parses an address; text that isn't one ends the program (use TryParse for input).</summary>
    public static IPAddress Parse(ReadOnlySpan<char> ipSpan)
    {
        if (!TryParse(ipSpan, out IPAddress address))
            throw new FormatException("IPAddress: not a valid IP address");
        return address;
    }

    /// <summary>Parses an address: 192.168.0.1, ::1, fe80::1%2... False if the text isn't one.</summary>
    public static bool TryParse(string ipString, out IPAddress address)
    {
        if (ipString == null)
        {
            address = null;
            return false;
        }
        return TryParse(ipString.AsSpan(), out address);
    }

    /// <summary>Parses an address: 192.168.0.1, ::1, fe80::1%2... False if the text isn't one.</summary>
    public static bool TryParse(ReadOnlySpan<char> ipSpan, out IPAddress address)
    {
        address = null;
        if (ipSpan.IndexOf(':') < 0)
        {
            if (!TryParseV4(ipSpan, out uint v4))
                return false;
            address = new IPAddress(v4);
            return true;
        }
        long scope = 0;
        int percent = ipSpan.IndexOf('%');
        if (percent >= 0)
        {
            if (!TryParseDecimal(ipSpan.Slice(percent + 1), uint.MaxValue, out scope))
                return false;
            ipSpan = ipSpan.Slice(0, percent);
        }
        ushort[] groups = new ushort[8];
        if (!TryParseV6(ipSpan, groups))
            return false;
        address = new IPAddress(groups, scope);
        return true;
    }

    /// <summary>The address as text: 192.168.0.1; IPv6 in its shortest form (RFC 5952), as ::1 or fe80::1%2.</summary>
    public override string ToString()
    {
        Span<char> chars = stackalloc char[64];
        TryFormat(chars, out int written);
        return string.FromChars(chars.Slice(0, written));
    }

    /// <summary>Writes the address as ToString would; false if it doesn't fit.</summary>
    public bool TryFormat(Span<char> destination, out int charsWritten)
    {
        Span<char> chars = stackalloc char[64];
        int at = 0;
        if (_v6 == null)
        {
            at = WriteV4(chars, 0, _v4);
        }
        else if (IsIPv4MappedToIPv6)
        {
            "::ffff:".AsSpan().CopyTo(chars);
            at = WriteV4(chars, 7, (uint)_v6[6] << 16 | _v6[7]);
        }
        else
        {
            // The longest run of two or more zero groups, the first if tied,
            // becomes "::".
            int bestStart = -1, bestLength = 1;
            for (int i = 0; i < 8;)
            {
                if (_v6[i] != 0)
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < 8 && _v6[i] == 0)
                    i++;
                if (i - start > bestLength)
                {
                    bestStart = start;
                    bestLength = i - start;
                }
            }
            for (int i = 0; i < 8; i++)
            {
                if (i == bestStart)
                {
                    chars[at++] = ':';
                    chars[at++] = ':';
                    i += bestLength - 1;
                    continue;
                }
                if (at > 0 && chars[at - 1] != ':')
                    chars[at++] = ':';
                at = WriteHex(chars, at, _v6[i]);
            }
        }
        if (_v6 != null && _scopeId != 0)
        {
            chars[at++] = '%';
            at = WriteDecimal(chars, at, _scopeId);
        }
        charsWritten = 0;
        if (at > destination.Length)
            return false;
        chars.Slice(0, at).CopyTo(destination);
        charsWritten = at;
        return true;
    }

    /// <summary>Whether <paramref name="comparand"/> is the same address (and scope).</summary>
    public override bool Equals(object comparand)
    {
        if (comparand is not IPAddress other || (other._v6 == null) != (_v6 == null))
            return false;
        if (_v6 == null)
            return _v4 == other._v4;
        if (_scopeId != other._scopeId)
            return false;
        for (int i = 0; i < 8; i++)
            if (_v6[i] != other._v6[i])
                return false;
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (_v6 == null)
            return (int)_v4;
        int hash = (int)_scopeId;
        for (int i = 0; i < 8; i++)
            hash = hash * 31 + _v6[i];
        return hash;
    }

    /// <summary>A 16-bit number from this machine's byte order to the network's (big-endian).</summary>
    public static short HostToNetworkOrder(short host) => BitConverter.IsLittleEndian ? (short)((host & 0xFF) << 8 | (host >> 8 & 0xFF)) : host;
    /// <summary>A 32-bit number from this machine's byte order to the network's.</summary>
    public static int HostToNetworkOrder(int host) => BitConverter.IsLittleEndian ? (int)Swap((uint)host) : host;
    /// <summary>A 64-bit number from this machine's byte order to the network's.</summary>
    public static long HostToNetworkOrder(long host) => BitConverter.IsLittleEndian ? (long)((ulong)Swap((uint)host) << 32 | Swap((uint)(host >> 32))) : host;
    /// <summary>A 16-bit number from the network's byte order to this machine's.</summary>
    public static short NetworkToHostOrder(short network) => HostToNetworkOrder(network);
    /// <summary>A 32-bit number from the network's byte order to this machine's.</summary>
    public static int NetworkToHostOrder(int network) => HostToNetworkOrder(network);
    /// <summary>A 64-bit number from the network's byte order to this machine's.</summary>
    public static long NetworkToHostOrder(long network) => HostToNetworkOrder(network);

    static uint Swap(uint x) => x >> 24 | (x >> 8 & 0xFF00) | (x << 8 & 0xFF0000) | x << 24;

    // ---- parsing and formatting -----------------------------------------------

    // Four decimal numbers 0-255, separated by dots.
    static bool TryParseV4(ReadOnlySpan<char> text, out uint address)
    {
        address = 0;
        int parts = 0;
        int i = 0;
        while (true)
        {
            int start = i;
            uint value = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9' && i - start < 3)
                value = value * 10 + (uint)(text[i++] - '0');
            if (i == start || value > 255)
                return false;
            address = address << 8 | value;
            parts++;
            if (parts == 4)
                return i == text.Length;
            if (i >= text.Length || text[i] != '.')
                return false;
            i++;
        }
    }

    // Up to eight hexadecimal groups of 1-4 digits, separated by ':', with at
    // most one "::" standing for the zero groups left out, and optionally an
    // IPv4 address as the last two groups.
    static bool TryParseV6(ReadOnlySpan<char> text, ushort[] groups)
    {
        if (text.Length < 2)
            return false;
        int count = 0;     // groups written
        int gap = -1;      // where "::" was, in groups
        int i = 0;
        if (text[0] == ':')
        {
            if (text[1] != ':')
                return false;
            gap = 0;
            i = 2;
            if (i == text.Length)
                return true; // "::"
        }
        while (i < text.Length)
        {
            if (count == 8)
                return false;
            int start = i;
            int value = 0;
            while (i < text.Length && i - start < 4 && HexValue(text[i]) >= 0)
                value = value << 4 | HexValue(text[i++]);
            if (i < text.Length && text[i] == '.')
            {
                // The last 32 bits written as IPv4.
                if (count > 6 || !TryParseV4(text.Slice(start), out uint v4))
                    return false;
                groups[count++] = (ushort)(v4 >> 16);
                groups[count++] = (ushort)v4;
                i = text.Length;
                break;
            }
            if (i == start)
                return false;
            groups[count++] = (ushort)value;
            if (i == text.Length)
                break;
            if (text[i] != ':')
                return false;
            i++;
            if (i < text.Length && text[i] == ':')
            {
                if (gap >= 0)
                    return false;
                gap = count;
                i++;
                if (i == text.Length)
                    break;
            }
            else if (i == text.Length)
            {
                return false; // a lone trailing ':'
            }
        }
        if (gap < 0)
            return count == 8;
        if (count == 8)
            return false; // "::" has to stand for at least one group
        // Move the groups after the gap to the end.
        int after = count - gap;
        for (int k = 0; k < after; k++)
            groups[7 - k] = groups[count - 1 - k];
        for (int k = gap; k < 8 - after; k++)
            groups[k] = 0;
        return true;
    }

    static bool TryParseDecimal(ReadOnlySpan<char> text, long max, out long value)
    {
        value = 0;
        if (text.IsEmpty || text.Length > 10)
            return false;
        foreach (char c in text)
        {
            if (c < '0' || c > '9')
                return false;
            value = value * 10 + (c - '0');
        }
        return value <= max;
    }

    static int HexValue(char c)
        => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

    static int WriteV4(Span<char> chars, int at, uint v4)
    {
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            at = WriteDecimal(chars, at, v4 >> shift & 0xFF);
            if (shift > 0)
                chars[at++] = '.';
        }
        return at;
    }

    static int WriteDecimal(Span<char> chars, int at, long value)
    {
        Span<char> digits = stackalloc char[20];
        int n = 0;
        do
        {
            digits[n++] = (char)('0' + value % 10);
            value /= 10;
        } while (value > 0);
        while (n > 0)
            chars[at++] = digits[--n];
        return at;
    }

    static int WriteHex(Span<char> chars, int at, ushort value)
    {
        bool started = false;
        for (int shift = 12; shift >= 0; shift -= 4)
        {
            int digit = value >> shift & 0xF;
            if (digit == 0 && !started && shift > 0)
                continue;
            started = true;
            chars[at++] = (char)(digit < 10 ? '0' + digit : 'a' + digit - 10);
        }
        return at;
    }
}

/// <summary>An IP address and a port.</summary>
public class IPEndPoint : EndPoint
{
    /// <summary>The smallest port, 0 (bind to it to have the system choose one).</summary>
    public const int MinPort = 0;
    /// <summary>The largest port, 65535.</summary>
    public const int MaxPort = 65535;

    IPAddress _address;
    int _port;

    /// <summary>An endpoint for the address and port.</summary>
    public IPEndPoint(IPAddress address, int port)
    {
        Address = address;
        Port = port;
    }

    /// <summary>An endpoint for an IPv4 address given as .NET's long (network-order bytes, low first).</summary>
    public IPEndPoint(long address, int port)
        : this(new IPAddress(new byte[] { (byte)address, (byte)(address >> 8), (byte)(address >> 16), (byte)(address >> 24) }), port) { }

    /// <summary>The address.</summary>
    public IPAddress Address
    {
        get => _address;
        set => _address = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The port, 0 to 65535.</summary>
    public int Port
    {
        get => _port;
        set
        {
            if (value < MinPort || value > MaxPort)
                throw new ArgumentOutOfRangeException(nameof(value));
            _port = value;
        }
    }

    /// <inheritdoc/>
    public override AddressFamily AddressFamily => _address.AddressFamily;

    /// <summary>"192.168.0.1:80", or "[::1]:80" for IPv6.</summary>
    public override string ToString()
    {
        string port = _port.ToString();
        return _address.AddressFamily == AddressFamily.InterNetworkV6
            ? string.Concat("[", _address.ToString(), "]:", port)
            : string.Concat(_address.ToString(), ":", port);
    }

    /// <summary>Parses "address:port" or "[ipv6]:port"; false if the text isn't one.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out IPEndPoint result)
    {
        result = null;
        int colon = s.LastIndexOf(':');
        ReadOnlySpan<char> host = s;
        long port = 0;
        if (s.Length > 0 && s[0] == '[')
        {
            int close = s.IndexOf(']');
            if (close < 0)
                return false;
            host = s.Slice(1, close - 1);
            if (close + 1 < s.Length)
            {
                if (s[close + 1] != ':' || !ParsePort(s.Slice(close + 2), out port))
                    return false;
            }
            else if (close + 1 != s.Length)
                return false;
        }
        else if (colon >= 0 && s.IndexOf(':') == colon)
        {
            // One colon: IPv4 with a port. More: an IPv6 address alone.
            host = s.Slice(0, colon);
            if (!ParsePort(s.Slice(colon + 1), out port))
                return false;
        }
        if (!IPAddress.TryParse(host, out IPAddress address))
            return false;
        result = new IPEndPoint(address, (int)port);
        return true;
    }

    /// <summary>Parses "address:port" or "[ipv6]:port"; text that isn't one ends the program.</summary>
    public static IPEndPoint Parse(string s)
    {
        if (s == null)
            throw new ArgumentNullException(nameof(s));
        if (!TryParse(s.AsSpan(), out IPEndPoint result))
            throw new FormatException("IPEndPoint: not a valid endpoint");
        return result;
    }

    static bool ParsePort(ReadOnlySpan<char> text, out long port)
    {
        port = 0;
        if (text.IsEmpty || text.Length > 5)
            return false;
        foreach (char c in text)
        {
            if (c < '0' || c > '9')
                return false;
            port = port * 10 + (c - '0');
        }
        return port <= MaxPort;
    }

    /// <summary>Whether <paramref name="comparand"/> is the same address and port.</summary>
    public override bool Equals(object comparand)
        => comparand is IPEndPoint other && other._port == _port && other._address.Equals(_address);

    /// <inheritdoc/>
    public override int GetHashCode() => _address.GetHashCode() ^ _port;
}

/// <summary>Looking names up: a host name's addresses, and this machine's name.</summary>
public static class Dns
{
    /// <summary>
    /// The addresses of a host name (or the address itself, if it is one).
    /// A name that can't be found ends the program (SocketException); use
    /// TryGetHostAddresses where that can happen.
    /// </summary>
    public static IPAddress[] GetHostAddresses(string hostNameOrAddress)
    {
        if (!TryGetHostAddresses(hostNameOrAddress, out IPAddress[] addresses, out SocketError error))
            throw new SocketException((int)error);
        return addresses;
    }

    /// <summary>
    /// The addresses of a host name (or the address itself, if it is one), or
    /// why there are none: HostNotFound, TryAgain (the name servers didn't
    /// answer), NoData... The system's resolver waits for the answer without
    /// letting other fibers run.
    /// </summary>
    public static bool TryGetHostAddresses(string hostNameOrAddress, out IPAddress[] addresses, out SocketError error)
    {
        if (hostNameOrAddress == null)
            throw new ArgumentNullException(nameof(hostNameOrAddress));
        if (IPAddress.TryParse(hostNameOrAddress, out IPAddress literal))
        {
            addresses = new[] { literal };
            error = SocketError.Success;
            return true;
        }
        return SocketNative.Resolve(hostNameOrAddress, out addresses, out error);
    }

    /// <summary>This machine's host name.</summary>
    public static string GetHostName() => SocketNative.HostName();
}
