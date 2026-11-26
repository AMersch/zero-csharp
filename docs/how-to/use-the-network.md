---
title: Use the network
index: 30
---

# Use the network

`lib/Net.cs` and `lib/Sockets.cs` have .NET's `System.Net` and `System.Net.Sockets`: `IPAddress`,
`IPEndPoint`, `Dns`, `Socket`, `TcpListener`, `TcpClient`, `UdpClient` and `NetworkStream`. Code written
against them compiles here. The sockets work with [fibers](use-fibers.html): a call that would wait parks
its fiber and lets the others run, so one thread can serve many connections with a fiber each. There are
no callbacks and no `async`. `samples/Network.cs` runs every example below, over loopback, so it needs no
network.

Networking works on Linux, macOS and Windows.

## Addresses

`IPAddress` parses and prints IPv4 and IPv6 addresses, and `IPEndPoint` pairs an address with a port:

<!-- sample: samples/Network.cs addresses -->
```csharp
// Addresses parse and print as in .NET; IPv6 prints in its
// shortest form.
IPAddress v4 = IPAddress.Parse("192.168.1.10");
IPAddress v6 = IPAddress.Parse("2001:0db8:0000:0000:0000:0000:0000:0001");
var endpoint = new IPEndPoint(IPAddress.IPv6Loopback, 8080);
// v6 prints as 2001:db8::1, endpoint as [::1]:8080
```

IPv6 prints in its shortest form (RFC 5952), as .NET's does. `Parse` takes four decimal numbers for
IPv4, and for IPv6 hexadecimal groups, `::`, a trailing IPv4 part and a numeric `%scope`. Text that
isn't an address makes `Parse` end the program, as a `FormatException` would, so use `TryParse` on
input. `IPAddress.Any` and `IPAddress.Loopback` have their IPv6 twins (`IPv6Any`, `IPv6Loopback`), and
`MapToIPv6` and `MapToIPv4` convert between the two.

## A server

`TcpListener` listens on an address and port, and `AcceptTcpClient` waits for the next connection. Give
each connection a fiber, and the server handles them all at once on one thread:

<!-- sample: samples/Network.cs echo -->
```csharp
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
```

Port 0 lets the system choose a free port; `LocalEndpoint` says which one it chose. Each connection's
fiber reads and writes through a `NetworkStream`, a `Stream` like any other (see
[Use streams](use-streams.html)):

<!-- sample: samples/Network.cs serve -->
```csharp
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
```

`Read` returns 0 when the other end has shut its sending down (`Shutdown(SocketShutdown.Send)`) or
closed the connection. A `Write` bigger than the system's buffers waits for the other end to read, and
the other fibers run meanwhile. The sample sends 2 MB from one fiber while another reads the echo on
the same connection.

## When something goes wrong

A `throw` ends the program unless a [`Catch.Run`](recover-from-errors.html) is waiting for it on
the same fiber. So failures that are part of normal network life come back as values, and nothing a
client does can end a server:

- A broken connection ends only its own stream. When the other end resets the connection, or its
  socket is closed under it, `NetworkStream.Read` returns 0, as at the end of the stream, and `Write`
  drops what it's given. `NetworkStream.Error` says what happened: `ConnectionReset`,
  `ConnectionAborted`, or `OperationAborted` when the socket was closed. The connection's fiber
  finishes its loop, and the others carry on.
- `TryAcceptTcpClient`, `TryAccept`, `TryConnect` and `Dns.TryGetHostAddresses` return the reason
  instead of ending the program. A server that runs out of sockets (`TooManyOpenSockets`) can wait and
  try again, as the echo server above does.
- `Receive` and `Send` with `out SocketError` do the same on a bare `Socket`. A failed `Send` also
  returns how many bytes went out before the failure.

<!-- sample: samples/Network.cs reset -->
```csharp
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
```

The methods without a `Try` or `out SocketError` form throw on failure, a `SocketException` as in
.NET, which ends the program if nothing catches it. That covers `Connect`, `AcceptTcpClient`,
`Dns.GetHostAddresses` and `IPAddress.Parse`, so use them where a failure is a bug rather than a
network event, or inside a `Catch.Run`:

<!-- sample: samples/Network.cs refused -->
```csharp
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
```

Inside a [`Catch.Run`](recover-from-errors.html), the forms that throw come back as an exception, and
the socket the callback opened and left open is closed:

<!-- sample: samples/Network.cs catch -->
```csharp
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
```

Code of your own that serves a connection may throw too, on a request it can't make sense of. Give
each connection's fiber a `Catch.Run` of its own: a throw comes back only on its own fiber, so one
around `Fiber.Run()` would catch nothing thrown inside the fibers.

<!-- sample: samples/Network.cs serve-catch -->
```csharp
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
```

Writing to a connection the other end has closed returns an error, not the SIGPIPE signal that would
otherwise end a Unix program.

There are no timeouts yet: a fiber waiting on a peer that has gone silent waits until something
changes. To give up on it, close its socket from another fiber, such as one that sleeps for the time
you allow. The waiting call wakes up and fails with `OperationAborted`, or its stream's `Read`
returns 0. Closing wakes the waiter before the system can give the socket's number to a new socket, so
the waiter never sees another connection's data. A `throw` outside any `Catch.Run` still ends the
whole program, every fiber included, and so does a bug such as an index out of range. To survive
that, run the program under something that restarts it, such as systemd or a container's restart
policy.

## UDP

`UdpClient` sends and receives datagrams. `Receive` into a span allocates nothing and says where the
datagram came from:

<!-- sample: samples/Network.cs udp -->
```csharp
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
```

`Receive(ref IPEndPoint)` returns a new array as in .NET; the span form doesn't allocate. A datagram
bigger than the buffer is cut to fit.

## Names

`Dns.GetHostAddresses("example.com")` asks the system's resolver (`getaddrinfo`). That call can take
seconds and can't let fibers run by itself. So in a fiber it runs on a thread of its own, and the fiber
sleeps a millisecond at a time, letting the others run, until the answer comes. Outside a fiber it's a
plain call. `TryGetHostAddresses` returns the reason (`HostNotFound`, `TryAgain`, `NoData`) instead of
ending the program. `Socket.Connect(host, port)` and `new TcpClient(host, port)` look the name up and
try each address in turn.

## How it works

Every socket is non-blocking underneath. When a call would wait, the socket and what it waits for (to
read, or to write) go on the fiber, and the fiber parks. When no fiber is ready to run, `Fiber.Run`
waits in `poll` until a socket is ready or the earliest sleeper's time comes. While fibers are busy, it
also checks the sockets every 64 switches without waiting.

The C library's socket functions are called with Linux's numbers and struct layouts. macOS's differ, and
`lib/Sockets.cs` knows both. On Windows, `win/runtime.c` answers the same calls through Winsock.

## What isn't here

- **TLS (HTTPS):** it needs a crypto library.
- **HTTP:** `HttpClient` and a server.
- **Timeouts:** `ReceiveTimeout` and `SendTimeout`.
- **`Select` and the async methods:** fibers take their place.
- **Other systems:** WebAssembly (WASI preview 1 can't open sockets), DOS, UEFI and microcontrollers have
  no sockets. A program that uses them there fails to link.

See also: [Run green threads](use-fibers.html), and the [`Socket`](../reference/socket.html) and
[`IPAddress`](../reference/ipaddress.html) reference.
