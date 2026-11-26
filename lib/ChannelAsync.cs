// Channel<T>'s async side, after .NET's ChannelReader/ChannelWriter:
//
//   await foreach (int value in channel.ReceiveAllAsync())   // until closed
//       ...
//   while (await channel.WaitToReceiveAsync())               // the same, by hand
//       while (channel.TryReceive(out int value))
//           ...
//   int one = await channel.ReceiveAsync();                  // ends the program if closed
//   await channel.SendAsync(42);                             // waits while full
//
// An async method waiting on a channel costs no fiber: it's a task in the
// channel's lists, completed by the Send or Receive that serves it. Fibers
// and async methods can share a channel; when both wait, the fibers are
// served first. Waits take a CancellationToken; a canceled one leaves the
// lists, and awaiting it ends the program as .NET's OperationCanceledException
// would (see lib/Cancellation.cs).
//
// Not on WebAssembly, which has no fibers.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class Channel<T>
{
    /// <summary>
    /// Receives the next value, waiting (without a fiber) while the channel is
    /// empty. Awaiting it ends the program if the channel is closed and empty
    /// (as .NET's ReadAsync throws): ReceiveAllAsync and WaitToReceiveAsync
    /// stop at the end instead.
    /// </summary>
    public ValueTask<T> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<T>(cancellationToken);
        if (TryReceive(out T value))
            return new ValueTask<T>(value);
        if (_closed)
            throw new InvalidOperationException("The channel has been closed.");
        return new ValueTask<T>(Waiters().AddReceiver(cancellationToken));
    }

    /// <summary>
    /// Sends <paramref name="value"/>, waiting (without a fiber) while the
    /// channel is full. Sending on a closed channel, or closing it while a
    /// send waits, ends the program, as Send does.
    /// </summary>
    public ValueTask SendAsync(T value, CancellationToken cancellationToken = default)
    {
        if (_closed)
            Environment.FailFast("Channel.SendAsync: the channel is closed");
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);
        bool receiverWaits;
        unsafe { receiverWaits = _receivers != null; } // SAFETY: a plain read
        if (receiverWaits || (_async != null && _async.HasReceiver) || _count < Capacity)
        {
            Send(value); // doesn't wait
            return default;
        }
        ChannelWaiters<T> waiters = Waiters();
        Task sent = waiters.AddSender(value, cancellationToken);
        waiters.Available(); // capacity 0: a value waits to be taken
        return new ValueTask(sent);
    }

    /// <summary>
    /// Waits (without a fiber) until a value can be received: true then, false
    /// once the channel is closed and empty. TryReceive takes the value.
    /// </summary>
    public ValueTask<bool> WaitToReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<bool>(cancellationToken);
        bool senderWaits;
        unsafe { senderWaits = _senders != null; } // SAFETY: a plain read
        if (_count > 0 || senderWaits || (_async != null && _async.HasSender))
            return new ValueTask<bool>(true);
        if (_closed)
            return new ValueTask<bool>(false);
        return new ValueTask<bool>(Waiters().AddWaiter(cancellationToken));
    }

    /// <summary>Every value, for `await foreach`, until the channel is closed and empty.</summary>
    public IAsyncEnumerable<T> ReceiveAllAsync(CancellationToken cancellationToken = default) => new ReceiveAll(this, cancellationToken);

    ChannelWaiters<T> Waiters()
    {
        if (_async is ChannelWaiters<T> waiters)
            return waiters;
        waiters = new ChannelWaiters<T>();
        _async = waiters;
        return waiters;
    }

    sealed class ReceiveAll(Channel<T> channel, CancellationToken token) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        public T Current { get; private set; } = default!;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.CanBeCanceled)
                token = cancellationToken;
            return this;
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            while (true)
            {
                if (channel.TryReceive(out T value))
                {
                    Current = value;
                    return true;
                }
                if (!await channel.WaitToReceiveAsync(token))
                    return false;
            }
        }

        public ValueTask DisposeAsync() => default;
    }
}

// The async methods waiting on one channel, each a task in a list, in order.
sealed class ChannelWaiters<T> : Channel<T>.AsyncWaiters
{
    Receiver? _receivers, _receiversTail;
    Sender? _senders, _sendersTail;
    Waiter? _waiters;

    sealed class Receiver : Task<T>
    {
        internal Receiver? Next;
        internal CancellationTokenRegistration Registration;
    }

    sealed class Sender : Task
    {
        internal Sender? Next;
        internal T Value = default!;
        internal CancellationTokenRegistration Registration;
    }

    sealed class Waiter : Task<bool>
    {
        internal Waiter? Next;
        internal CancellationTokenRegistration Registration;
    }

    internal override bool HasSender => _senders != null;
    internal override bool HasReceiver => _receivers != null;

    internal Task<T> AddReceiver(CancellationToken token)
    {
        var r = new Receiver();
        if (_receiversTail == null)
            _receivers = r;
        else
            _receiversTail.Next = r;
        _receiversTail = r;
        if (token.CanBeCanceled)
            r.Registration = token.Register(static s => { var (w, r) = ((ChannelWaiters<T>, Receiver))s!; w.Cancel(r); }, (this, r));
        return r;
    }

    internal Task AddSender(T value, CancellationToken token)
    {
        var s = new Sender { Value = value };
        if (_sendersTail == null)
            _senders = s;
        else
            _sendersTail.Next = s;
        _sendersTail = s;
        if (token.CanBeCanceled)
            s.Registration = token.Register(static o => { var (w, s) = ((ChannelWaiters<T>, Sender))o!; w.Cancel(s); }, (this, s));
        return s;
    }

    internal Task<bool> AddWaiter(CancellationToken token)
    {
        var w = new Waiter { Next = _waiters };
        _waiters = w;
        if (token.CanBeCanceled)
            w.Registration = token.Register(static o => { var (all, w) = ((ChannelWaiters<T>, Waiter))o!; all.Cancel(w); }, (this, w));
        return w;
    }

    internal override bool Give(T value)
    {
        Receiver? r = _receivers;
        if (r == null)
            return false;
        _receivers = r.Next;
        if (_receivers == null)
            _receiversTail = null;
        r.Registration.Dispose();
        r.TrySetResult(value);
        return true;
    }

    internal override bool Take(out T value)
    {
        Sender? s = _senders;
        if (s == null)
        {
            value = default!;
            return false;
        }
        _senders = s.Next;
        if (_senders == null)
            _sendersTail = null;
        value = s.Value;
        s.Value = default!;
        s.Registration.Dispose();
        s.Complete();
        return true;
    }

    internal override void Available()
    {
        Waiter? w = _waiters;
        _waiters = null;
        for (; w != null; w = w.Next)
        {
            w.Registration.Dispose();
            w.TrySetResult(true);
        }
    }

    internal override void Closed()
    {
        if (_receivers != null)
            Environment.FailFast("Channel.ReceiveAsync: the channel was closed while receiving (ReceiveAllAsync and WaitToReceiveAsync stop at the end instead)");
        if (_senders != null)
            Environment.FailFast("Channel.SendAsync: the channel was closed while sending");
        Waiter? w = _waiters;
        _waiters = null;
        for (; w != null; w = w.Next)
        {
            w.Registration.Dispose();
            w.TrySetResult(false);
        }
    }

    void Cancel(Receiver r)
    {
        Receiver? previous = null;
        for (Receiver? at = _receivers; at != null; previous = at, at = at.Next)
            if (at == r)
            {
                if (previous == null)
                    _receivers = r.Next;
                else
                    previous.Next = r.Next;
                if (_receiversTail == r)
                    _receiversTail = previous;
                r.Complete(true);
                return;
            }
    }

    void Cancel(Sender s)
    {
        Sender? previous = null;
        for (Sender? at = _senders; at != null; previous = at, at = at.Next)
            if (at == s)
            {
                if (previous == null)
                    _senders = s.Next;
                else
                    previous.Next = s.Next;
                if (_sendersTail == s)
                    _sendersTail = previous;
                s.Value = default!;
                s.Complete(true);
                return;
            }
    }

    void Cancel(Waiter w)
    {
        Waiter? previous = null;
        for (Waiter? at = _waiters; at != null; previous = at, at = at.Next)
            if (at == w)
            {
                if (previous == null)
                    _waiters = w.Next;
                else
                    previous.Next = w.Next;
                w.Complete(true);
                return;
            }
    }
}
