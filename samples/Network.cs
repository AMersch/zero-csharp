// Example: networking (lib/Net.cs, lib/Sockets.cs): addresses, and TCP and
// UDP sockets that work with fibers: a server fiber accepts connections and
// gives each a fiber of its own, all on one thread. Everything goes over
// loopback (127.0.0.1), so no network is needed.
// Exit code = number of failed checks.
// Build: ./build.sh Network

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace NetworkSample;

public sealed class Echo
{
    public TcpListener Listener;
    public int Port;
    public int Clients;     // how many connections the server takes
    public int Served;
    public int Correct;     // clients whose echo came back right
}

public sealed class Bulk
{
    public NetworkStream Stream;
    public int Size;
    public long Received;
    public bool Ordered = true;
}

public sealed class Failures
{
    public TcpListener Listener;
    public int Port;
    public SocketError Reset, Closed;
    public TcpClient Victim;    // the server's end of the connection closed from another fiber
    public bool Waiting;        // its reader is about to wait
    public bool EchoedAfter;
}

public sealed class Guarded
{
    public TcpListener Listener;
    public int Port;
    public int Served;
    public int Failed;      // connections whose serving threw
    public int Echoed;      // clients that got their echo
    public bool Dropped;    // the client whose connection failed saw it close
}

public sealed class Datagrams
{
    public UdpClient Server, Client;
    public int Port;
    public bool Echoed;
}

public sealed class Lookup
{
    public bool Found;
    public int OthersRan;
}

public static class Program
{
    static int s_failures;
    static int s_sectionChecks;
    static int s_sectionFailures;

    static void Section(string section)
    {
        Out.Line($"{(s_sectionFailures == 0 ? "PASS" : "FAIL")} {section} ({s_sectionChecks} checks)");
        s_sectionChecks = 0;
        s_sectionFailures = 0;
    }

    static void Check(string name, bool ok)
    {
        s_sectionChecks++;
        if (!ok)
        {
            s_failures++;
            s_sectionFailures++;
            Out.Line($"  failed: {name}");
        }
    }

    static void Same(string name, string actual, string expected)
    {
        Check(name, actual == expected);
        if (actual != expected)
            Out.Line($"    got \"{actual}\", expected \"{expected}\"");
    }

    static bool Same(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    // Reads until the stream ends, and says why it did: Success if the
    // other end finished, or how the connection broke.
    static SocketError DrainUntilBroken(TcpClient client, Failures waiting)
    {
        NetworkStream stream = client.GetStream();
        Span<byte> buffer = stackalloc byte[64];
        if (waiting != null)
            waiting.Waiting = true;
        while (stream.Read(buffer) > 0) { }
        return stream.Error;
    }

    // docs:serve
    // Each connection gets a fiber: it reads what the client sends and
    // writes it back, until the client shuts its sending down.
    static void Serve(TcpClient client)
    {
        using NetworkStream stream = client.GetStream();
        Span<byte> buffer = stackalloc byte[512];
        int n;
        while ((n = stream.Read(buffer)) > 0)
            stream.Write(buffer.Slice(0, n));
    }
    // docs:end

    // docs:serve-catch
    // The same with a Catch.Run of its own, which every fiber that should
    // survive a throw needs: a throw while serving (here, for a message
    // that starts with '!') ends this connection, not the program.
    static bool ServeGuarded(TcpClient client)
    {
        Exception error = Catch.Run(ref client, static (ref TcpClient c) =>
        {
            using NetworkStream stream = c.GetStream();
            Span<byte> buffer = stackalloc byte[512];
            int n;
            while ((n = stream.Read(buffer)) > 0)
            {
                if (buffer[0] == (byte)'!')
                    throw new IOException("a bad request");
                stream.Write(buffer.Slice(0, n));
            }
        });
        client.Dispose(); // runs either way: Catch.Run always returns
        return error == null;
    }
    // docs:end

    public static int Main()
    {
        bool small = Fiber.DefaultStackSize < 64 * 1024;

        // docs:addresses
        // Addresses parse and print as in .NET; IPv6 prints in its
        // shortest form.
        IPAddress v4 = IPAddress.Parse("192.168.1.10");
        IPAddress v6 = IPAddress.Parse("2001:0db8:0000:0000:0000:0000:0000:0001");
        var endpoint = new IPEndPoint(IPAddress.IPv6Loopback, 8080);
        // v6 prints as 2001:db8::1, endpoint as [::1]:8080
        // docs:end
        Same("v4", v4.ToString(), "192.168.1.10");
        Same("v6", v6.ToString(), "2001:db8::1");
        Same("endpoint", endpoint.ToString(), "[::1]:8080");
        Same("v4 endpoint", new IPEndPoint(IPAddress.Loopback, 80).ToString(), "127.0.0.1:80");
        Same("::", IPAddress.IPv6Any.ToString(), "::");
        Same("mapped", IPAddress.Loopback.MapToIPv6().ToString(), "::ffff:127.0.0.1");
        Same("embedded v4", IPAddress.Parse("::ffff:10.0.0.1").MapToIPv4().ToString(), "10.0.0.1");
        Same("longest zeros", IPAddress.Parse("1:0:0:2:0:0:0:3").ToString(), "1:0:0:2::3");
        Same("one zero isn't ::", IPAddress.Parse("1:2:3:4:5:6:0:8").ToString(), "1:2:3:4:5:6:0:8");
        Same("scope", IPAddress.Parse("fe80::1%3").ToString(), "fe80::1%3");
        Same("upper case", IPAddress.Parse("FE80::ABCD").ToString(), "fe80::abcd");
        Check("bytes", IPAddress.Parse("1.2.3.4").GetAddressBytes()[3] == 4);
        Check("equal", IPAddress.Parse("::1").Equals(IPAddress.IPv6Loopback));
        Check("loopback", IPAddress.IsLoopback(IPAddress.Parse("127.5.5.5")) && IPAddress.IsLoopback(IPAddress.IPv6Loopback));
        string[] wrong = ["", "1.2.3", "1.2.3.4.5", "256.1.1.1", "1.2.3.x", ":::", "1::2::3", "12345::", "1:2:3:4:5:6:7:8:9", "1:", "fe80::1%x"];
        int refused = 0;
        foreach (string text in wrong)
            if (!IPAddress.TryParse(text, out _))
                refused++;
        Check("bad addresses refused", refused == wrong.Length);
        Check("endpoint parse", IPEndPoint.TryParse("10.0.0.1:443", out IPEndPoint parsed)
            && parsed.Port == 443 && parsed.Address.ToString() == "10.0.0.1");
        Check("v6 endpoint parse", IPEndPoint.TryParse("[::1]:80", out parsed) && parsed.Port == 80);
        Section("Addresses");

        // docs:echo
        // A server fiber accepts connections and gives each a fiber; client
        // fibers connect, send, and read the echo. Every call that waits
        // lets the other fibers run.
        var echo = new Echo
        {
            Listener = new TcpListener(IPAddress.Loopback, 0), // any free port
            Clients = small ? 3 : 20,
        };
        echo.Listener.Start();
        echo.Port = ((IPEndPoint)echo.Listener.LocalEndpoint).Port;
        Fiber.Spawn(echo, static e =>
        {
            while (e.Served < e.Clients)
            {
                // A failure (too many sockets open, say) waits and tries
                // again, rather than ending the program.
                if (!e.Listener.TryAcceptTcpClient(out TcpClient client, out _))
                {
                    Fiber.Sleep(10);
                    continue;
                }
                e.Served++;
                Fiber.Spawn(client, static c => Serve(c));
            }
        });
        for (int id = 0; id < echo.Clients; id++)
            Fiber.Spawn((echo, id), static s =>
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, s.echo.Port);
                NetworkStream stream = client.GetStream();
                Span<byte> message = stackalloc byte[100];
                message.Fill((byte)s.id);
                stream.Write(message);
                client.Client.Shutdown(SocketShutdown.Send);
                Span<byte> back = stackalloc byte[100];
                stream.ReadExactly(back);
                if (Same(back, message) && stream.Read(back) == 0)
                    s.echo.Correct++;
            });
        Fiber.Run();
        echo.Listener.Stop();
        // docs:end
        Check("all served", echo.Served == echo.Clients);
        Check("all echoed", echo.Correct == echo.Clients);
        Section("Echo");

        // Two fibers on one connection: one sends 2 MB (256 KB on a small
        // machine) and must wait whenever the system's buffers are full,
        // while the other reads the echo back.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Fiber.Spawn(listener, static l => Serve(l.AcceptTcpClient()));
        var bulk = new Bulk { Size = small ? 256 * 1024 : 2 * 1024 * 1024 };
        var connection = new TcpClient();
        connection.Connect(IPAddress.Loopback, port);
        bulk.Stream = connection.GetStream();
        Fiber.Spawn(bulk, static b =>
        {
            Span<byte> chunk = stackalloc byte[256];
            for (int sent = 0; sent < b.Size; sent += chunk.Length)
            {
                for (int i = 0; i < chunk.Length; i++)
                    chunk[i] = (byte)((sent + i) % 251);
                b.Stream.Write(chunk);
            }
            b.Stream.Socket.Shutdown(SocketShutdown.Send);
        });
        Fiber.Spawn(bulk, static b =>
        {
            Span<byte> chunk = stackalloc byte[256];
            int n;
            while ((n = b.Stream.Read(chunk)) > 0)
            {
                for (int i = 0; i < n; i++)
                    if (chunk[i] != (byte)((b.Received + i) % 251))
                        b.Ordered = false;
                b.Received += n;
            }
        });
        Fiber.Run();
        connection.Dispose();
        listener.Stop();
        Check("every byte back", bulk.Received == bulk.Size);
        Check("in order", bulk.Ordered);
        Section("Backpressure");

        // A client that resets its connection, and a connection closed from
        // another fiber while its reader waits, end only their own streams:
        // the server goes on serving.
        var failures = new Failures { Listener = new TcpListener(IPAddress.Loopback, 0) };
        failures.Listener.Start();
        failures.Port = ((IPEndPoint)failures.Listener.LocalEndpoint).Port;
        Fiber.Spawn(failures, static f =>
        {
            for (int i = 0; i < 3; i++)
            {
                if (!f.Listener.TryAcceptTcpClient(out TcpClient client, out _))
                    return;
                if (i == 0)
                    Fiber.Spawn((f, client), static s =>
                    {
                        s.client.GetStream().Write("hello"u8);
                        s.f.Reset = DrainUntilBroken(s.client, null);
                    });
                else if (i == 1)
                {
                    f.Victim = client;
                    Fiber.Spawn((f, client), static s => s.f.Closed = DrainUntilBroken(s.client, s.f));
                }
                else
                    Fiber.Spawn(client, static c => Serve(c));
            }
        });
        Fiber.Spawn(failures, static f =>
        {
            // docs:reset
            // Closing with LingerOption(true, 0), or with data still unread,
            // resets the connection: the other end's Read returns 0, with
            // Error ConnectionReset.
            var rude = new TcpClient();
            rude.Connect(IPAddress.Loopback, f.Port);
            rude.GetStream().Write("x"u8);
            Span<byte> peek = stackalloc byte[1];
            rude.Client.Receive(peek, SocketFlags.Peek); // the greeting: left unread
            rude.Client.LingerState = new LingerOption(true, 0);
            rude.Dispose();
            // docs:end

            var quiet = new TcpClient();
            quiet.Connect(IPAddress.Loopback, f.Port);
            while (!f.Waiting)
                Fiber.Yield();
            f.Victim.Dispose(); // its reader, waiting in Read, wakes
            quiet.Dispose();

            using var polite = new TcpClient();
            polite.Connect(IPAddress.Loopback, f.Port);
            NetworkStream stream = polite.GetStream();
            stream.Write("after"u8);
            polite.Client.Shutdown(SocketShutdown.Send);
            Span<byte> back = stackalloc byte[5];
            stream.ReadExactly(back);
            f.EchoedAfter = Same(back, "after"u8);
        });
        Fiber.Run();
        failures.Listener.Stop();
        // Wine, which runs this sample's Windows build in CI, closes in order
        // even then (real Windows resets): there the stream may simply end.
        bool wine = Path.Separator == '\\' && failures.Reset == SocketError.Success;
        Check("reset: ConnectionReset", failures.Reset == SocketError.ConnectionReset || wine);
        Check("closed under a reader: OperationAborted", failures.Closed == SocketError.OperationAborted);
        Check("served after both", failures.EchoedAfter);

        // docs:refused
        // Outside a fiber a call simply waits. TryConnect says why it
        // couldn't connect, rather than ending the program.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop(); // nothing listens there now
        using var socket = new Socket(AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp);
        bool connected = socket.TryConnect(
            new IPEndPoint(IPAddress.Loopback, closedPort),
            out SocketError error);
        // connected is false, and error is SocketError.ConnectionRefused
        // docs:end
        Check("refused", !connected && error == SocketError.ConnectionRefused);
        Check("not connected", !socket.Connected);
        Section("Errors");

        // docs:catch
        // Connect throws a SocketException where TryConnect returns false.
        // Catch.Run gets it back, and closes the socket the callback opened
        // and left open.
        Socket attempt = null;
        Exception thrown = Catch.Run(() =>
        {
            attempt = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            attempt.Connect(new IPEndPoint(IPAddress.Loopback, closedPort));
        });
        // thrown is a SocketException, and attempt.Handle is -1
        // docs:end
        Check("Connect's throw comes back", thrown is SocketException refusal
            && refusal.SocketErrorCode == SocketError.ConnectionRefused);
        Check("and the socket it left open is closed", attempt.Handle == -1);

        // Kept, a socket opened inside stays open: for a pool, say.
        Socket pooled = null;
        thrown = Catch.Run(() =>
        {
            pooled = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Catch.Keep(pooled);
            throw new IOException("something else failed");
        });
        Check("a kept socket stays open after a throw", thrown is IOException && pooled.Handle != -1);
        pooled.Dispose();

        // A server whose connections each have a Catch.Run: one client sends
        // a request that makes its handler throw, and the others are served.
        var guarded = new Guarded { Listener = new TcpListener(IPAddress.Loopback, 0) };
        guarded.Listener.Start();
        guarded.Port = ((IPEndPoint)guarded.Listener.LocalEndpoint).Port;
        Fiber.Spawn(guarded, static g =>
        {
            while (g.Served < 3)
            {
                TcpClient client = g.Listener.AcceptTcpClient();
                g.Served++;
                Fiber.Spawn((g, client), static s =>
                {
                    if (!ServeGuarded(s.client))
                        s.g.Failed++;
                });
            }
        });
        for (int id = 0; id < 3; id++)
            Fiber.Spawn((guarded, id), static s =>
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, s.guarded.Port);
                NetworkStream stream = client.GetStream();
                Span<byte> message = stackalloc byte[10];
                message.Fill(s.id == 1 ? (byte)'!' : (byte)('a' + s.id));
                stream.Write(message);
                Span<byte> back = stackalloc byte[10];
                if (s.id == 1)
                {
                    s.guarded.Dropped = stream.Read(back) == 0; // closed, nothing echoed
                    return;
                }
                stream.ReadExactly(back);
                client.Client.Shutdown(SocketShutdown.Send);
                if (Same(back, message) && stream.Read(back) == 0)
                    s.guarded.Echoed++;
            });
        Fiber.Run();
        guarded.Listener.Stop();
        Check("the connection whose serving threw is closed", guarded.Failed == 1 && guarded.Dropped);
        Check("and the others are served", guarded.Echoed == 2);
        Section("Catching");

        // docs:udp
        // UDP: a datagram there and back, between two fibers.
        var udp = new Datagrams
        {
            Server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)),
            Client = new UdpClient(),
        };
        udp.Port = ((IPEndPoint)udp.Server.Client.LocalEndPoint).Port;
        Fiber.Spawn(udp, static u =>
        {
            Span<byte> datagram = stackalloc byte[64];
            int n = u.Server.Receive(datagram, out IPEndPoint from);
            u.Server.Send(datagram.Slice(0, n), from);
        });
        Fiber.Spawn(udp, static u =>
        {
            u.Client.Send("ping"u8, new IPEndPoint(IPAddress.Loopback, u.Port));
            IPEndPoint from = null;
            byte[] reply = u.Client.Receive(ref from);
            u.Echoed = Same(reply, "ping"u8) && from.Port == u.Port;
        });
        Fiber.Run();
        // docs:end
        udp.Server.Dispose();
        udp.Client.Dispose();
        Check("echoed", udp.Echoed);
        Section("UDP");

        // A name lookup in a fiber runs on a thread of its own, so the other
        // fibers run while it waits.
        var lookup = new Lookup();
        Fiber.Spawn(lookup, static l =>
        {
            if (Dns.TryGetHostAddresses("localhost", out IPAddress[] found, out _))
                foreach (IPAddress address in found)
                    l.Found |= IPAddress.IsLoopback(address);
        });
        Fiber.Spawn(lookup, static l => { l.OthersRan++; Fiber.Yield(); l.OthersRan++; });
        Fiber.Run();
        Check("localhost, in a fiber", lookup.Found);
        Check("others ran", lookup.OthersRan == 2);
        Check("localhost, outside", Dns.TryGetHostAddresses("localhost", out IPAddress[] direct, out _) && direct.Length > 0);
        Check("an address is its own answer", Dns.GetHostAddresses("10.1.2.3")[0].ToString() == "10.1.2.3");
        Check("host name", Dns.GetHostName().Length > 0);
        Section("Names");

        return s_failures;
    }
}
