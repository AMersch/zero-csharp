---
title: Use the network
index: 30
---

# Use the network

`lib/Net.cs`, `lib/Sockets.cs` and `lib/SocketsAsync.cs` have .NET's `System.Net` and
`System.Net.Sockets`: `IPAddress`, `IPEndPoint`, `Dns`, `Socket`, `TcpListener`, `TcpClient`, `UdpClient`
and `NetworkStream`. Code written against them compiles here. There are two ways to wait for the network,
and they mix:

- **Fibers.** A call that would wait parks its [fiber](use-fibers.html) and lets the others run, so one
  thread can serve many connections with a fiber each.
- **Async methods.** `ReadAsync`, `AcceptTcpClientAsync` and the rest return a task to `await`, and
  waiting costs no fiber at all (see [Async methods](#async-methods)).

`samples/Network.cs` runs every example below, over loopback, so it needs no network.

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

Closing a socket from another fiber also gives up on it: the waiting call wakes up and fails with
`OperationAborted`, or its stream's `Read` returns 0. Closing wakes the waiter before the system can give
the socket's number to a new socket, so the waiter never sees another connection's data. A `throw`
outside any `Catch.Run` still ends the whole program, every fiber included, and so does a bug such
as an index out of range. To survive that, run the program under something that restarts it, such
as systemd or a container's restart policy.

## Timeouts

A peer that goes silent would keep its reader waiting for ever. `ReadTimeout` and `WriteTimeout` on a
`NetworkStream` (`ReceiveTimeout` and `SendTimeout` on a `Socket` or `TcpClient`) set how long, in
milliseconds, a call waits:

<!-- sample: samples/Network.cs timeouts -->
```csharp
// A read that waits longer than ReadTimeout returns 0, with Error
// TimedOut. The connection stays open, and the next read goes on.
var timed = new TcpListener(IPAddress.Loopback, 0);
timed.Start();
using var silent = new TcpClient();
silent.Connect(IPAddress.Loopback, ((IPEndPoint)timed.LocalEndpoint).Port);
using TcpClient other = timed.AcceptTcpClient();
NetworkStream patient = silent.GetStream();
patient.ReadTimeout = 50; // milliseconds
Span<byte> one = stackalloc byte[1];
int got = patient.Read(one);
// got is 0, and patient.Error is SocketError.TimedOut
```

As with a broken connection, a timed-out `Read` returns 0 and sets `Error`. Unlike one, it leaves the
connection as it was, so the next `Read` waits again. A timed-out `Send` returns how many bytes went
before it gave up. `Receive` and `Send` with `out SocketError` give `TimedOut`, and those without throw a
`SocketException`, as in .NET. In a fiber the other fibers run during the wait, and outside
one the thread waits.

Connecting has no timeout of its own, as in .NET: give `ConnectAsync` a `CancellationToken`.

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

## Async methods

Every waiting call has its .NET async twin: `ReadAsync` and `WriteAsync` on `NetworkStream`, and
`ReceiveAsync`, `SendAsync`, `AcceptAsync`, `ConnectAsync`, `ReceiveFromAsync` and `SendToAsync` on
`Socket`. `TcpListener` has `AcceptTcpClientAsync`, `TcpClient` has `ConnectAsync`, `UdpClient` has
`ReceiveAsync` and `SendAsync`, and `Dns` has `GetHostAddressesAsync`. They take `Memory<byte>` where
the blocking calls take a span, because an async method can't keep a span across an `await`. An array
converts to one, and `AsMemory` takes a piece of it. Here is the echo server again, with an async method
per connection instead of a fiber:

<!-- sample: samples/Network.cs async-serve -->
```csharp
// The async way: each connection is an async method rather than a
// fiber. A wait costs no fiber, so thousands of connections can wait
// at once.
static async Task ServeAsync(TcpClient client)
{
    using NetworkStream stream = client.GetStream();
    byte[] buffer = new byte[512];
    int n;
    while ((n = await stream.ReadAsync(buffer)) > 0)
        await stream.WriteAsync(buffer.AsMemory(0, n));
}
```

A call that can finish at once does, with no task made. One that has to wait costs no fiber: the socket
joins the list `Fiber.Run` polls, and when it's ready, `Fiber.Run` finishes the operation and completes
its task. A waiting connection then costs only its async method's state, a few hundred bytes, against a
fiber's stack. Each socket reuses one task for its receives and one for its sends, so once a loop of
`ReadAsync` and `WriteAsync` is going, it allocates nothing. The sample checks that. As in .NET, await
each `ValueTask` once.

A token cancels the wait. On a `Socket`, `TcpListener` or `TcpClient`, canceling cancels the task, as in
.NET, and awaiting a canceled task ends the program (see [Use async and await](use-fibers.html)). A
server can't afford that per connection, so `NetworkStream` treats a cancel the way it treats a timeout:
`ReadAsync` returns 0 and `Error` says why. Its `ReadTimeout` and `WriteTimeout` apply to the async
calls too, which .NET's don't:

<!-- sample: samples/Network.cs cancel -->
```csharp
// Canceled or timed out, NetworkStream's ReadAsync returns 0, and
// Error says why; the stream stays usable. So a timeout doesn't
// need an await of a canceled task, which would end the program.
byte[] buffer = new byte[16];
using (var cts = new CancellationTokenSource(30))
{
    int n = await a.ReadAsync(buffer, cts.Token);
    // n is 0, and a.Error is SocketError.OperationAborted
    Check("canceled read", n == 0 && a.Error == SocketError.OperationAborted);
}
a.ReadTimeout = 30; // milliseconds, for ReadAsync too
int timedOut = await a.ReadAsync(buffer);
// timedOut is 0, and a.Error is SocketError.TimedOut
```

Closing a socket wakes what waits on it. A `NetworkStream` call returns 0 with `Error`
`OperationAborted`. A `Socket`, `TcpListener` or `UdpClient` task is canceled, where .NET's would fault.
To stop an accept loop, stop the listener (or cancel its token), and await the accept as a plain `Task`
with `SuppressThrowing`:

<!-- sample: samples/Network.cs stop -->
```csharp
// Stopping the listener cancels the accept that waits on it. Awaited
// as a plain Task with SuppressThrowing, a canceled task doesn't end
// the program: the loop looks at IsCanceled instead.
Task<TcpClient> next = pair.AcceptTcpClientAsync();
pair.Stop();
await ((Task)next).ConfigureAwait(
    ConfigureAwaitOptions.SuppressThrowing);
bool stopped = next.IsCanceled; // true
```

Some other differences from .NET:

- **One at a time.** A socket takes one receive and one send at a time. Starting a second
  `ReceiveAsync` while one waits ends the program; .NET would queue it.
- **Failures.** On a `Socket` they end the program when they happen, as an unobserved fault would
  when awaited. `NetworkStream` returns 0 and sets `Error`, as its `Read` does.
- **Name lookups.** `Dns.GetHostAddressesAsync` runs the system's resolver on a thread of its own. If its
  token is canceled, the lookup itself carries on in the background.

## How it works

Every socket is non-blocking underneath. When a call would wait, the socket and what it waits for (to
read, or to write) go on the fiber, and the fiber parks. An async operation goes on the same list, in
place of a fiber. When no fiber is ready to run, `Fiber.Run` waits in `poll` until a socket is ready, the
earliest sleeper's time comes, or a timeout runs out. While fibers are busy, it also checks the sockets
every 64 switches without waiting.

The C library's socket functions are called with Linux's numbers and struct layouts. macOS's differ, and
`lib/Sockets.cs` knows both. On Windows, `win/runtime.c` answers the same calls through Winsock.

## What isn't here

- **TLS (HTTPS):** it needs a crypto library.
- **HTTP:** `HttpClient` and a server.
- **`Select` and `SocketAsyncEventArgs`:** fibers and the async methods take their place.
- **Other systems:** WebAssembly (WASI preview 1 can't open sockets), DOS, UEFI and microcontrollers have
  no sockets. A program that uses them there fails to link.

See also: [Run green threads](use-fibers.html), and the [`Socket`](../reference/socket.html) and
[`IPAddress`](../reference/ipaddress.html) reference.
