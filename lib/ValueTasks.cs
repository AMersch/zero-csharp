// ValueTask, ValueTask<T> and async streams (IAsyncEnumerable), as in .NET:
// a ValueTask holds either a result, when there was no waiting, or the
// Task to wait for. So a call that finishes at once allocates nothing.
// `async ValueTask` methods work too (AsyncValueTaskMethodBuilder), and so
// does `await foreach` over an IAsyncEnumerable<T> (written by hand: no
// async iterators, `yield return` in an async method).
//
// Unlike .NET's, a ValueTask may be awaited more than once (it's backed by
// a plain Task), and there's no IValueTaskSource.
//
// Not on WebAssembly, which has no fibers.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace System.Threading.Tasks
{
    /// <summary>An operation that completed at once, or a Task to wait for.</summary>
    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
    public readonly struct ValueTask : IEquatable<ValueTask>
    {
        internal readonly Task? _task; // null: completed

        public ValueTask(Task task) { _task = task; }

        /// <summary>A ValueTask that has completed.</summary>
        public static ValueTask CompletedTask => default;

        public static ValueTask<TResult> FromResult<TResult>(TResult result) => new ValueTask<TResult>(result);

        public static ValueTask FromCanceled(CancellationToken cancellationToken) => new ValueTask(Task.FromCanceled(cancellationToken));

        public static ValueTask<TResult> FromCanceled<TResult>(CancellationToken cancellationToken) => new ValueTask<TResult>(Task.FromCanceled<TResult>(cancellationToken));

        public bool IsCompleted => _task == null || _task.IsCompleted;
        public bool IsCompletedSuccessfully => _task == null || _task.IsCompletedSuccessfully;
        public bool IsCanceled => _task != null && _task.IsCanceled;
        public bool IsFaulted => false;

        /// <summary>The Task: the one waited for, or Task.CompletedTask.</summary>
        public Task AsTask() => _task ?? Task.CompletedTask;

        public ValueTask Preserve() => this;

        public ValueTaskAwaiter GetAwaiter() => new ValueTaskAwaiter(_task);

        /// <summary>Returns something to await. The argument changes nothing.</summary>
        public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredValueTaskAwaitable(_task);

        public bool Equals(ValueTask other) => _task == other._task;
        public override bool Equals(object? obj) => obj is ValueTask other && Equals(other);
        public override int GetHashCode() => _task == null ? 0 : _task.GetHashCode();
        public static bool operator ==(ValueTask left, ValueTask right) => left.Equals(right);
        public static bool operator !=(ValueTask left, ValueTask right) => !left.Equals(right);
    }

    /// <summary>A result there at once, or a Task&lt;T&gt; to wait for.</summary>
    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder<>))]
    public readonly struct ValueTask<TResult> : IEquatable<ValueTask<TResult>>
    {
        internal readonly Task<TResult>? _task; // null: _result is it
        internal readonly TResult _result;

        public ValueTask(TResult result)
        {
            _task = null;
            _result = result;
        }

        public ValueTask(Task<TResult> task)
        {
            _task = task;
            _result = default!;
        }

        public bool IsCompleted => _task == null || _task.IsCompleted;
        public bool IsCompletedSuccessfully => _task == null || _task.IsCompletedSuccessfully;
        public bool IsCanceled => _task != null && _task.IsCanceled;
        public bool IsFaulted => false;

        /// <summary>The result, waiting for it first as Task.Result does. Ends the program if canceled.</summary>
        public TResult Result => _task == null ? _result : _task.Result;

        /// <summary>The Task: the one waited for, or a new completed one with the result.</summary>
        public Task<TResult> AsTask() => _task ?? Task.FromResult(_result);

        public ValueTask<TResult> Preserve() => this;

        public ValueTaskAwaiter<TResult> GetAwaiter() => new ValueTaskAwaiter<TResult>(this);

        /// <summary>Returns something to await. The argument changes nothing.</summary>
        public ConfiguredValueTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredValueTaskAwaitable<TResult>(this);

        public bool Equals(ValueTask<TResult> other) => _task == other._task && (_task != null || EqualityComparer<TResult>.Default.Equals(_result, other._result));
        public override bool Equals(object? obj) => obj is ValueTask<TResult> other && Equals(other);
        public override int GetHashCode() => _task != null ? _task.GetHashCode() : _result == null ? 0 : _result.GetHashCode();
        public static bool operator ==(ValueTask<TResult> left, ValueTask<TResult> right) => left.Equals(right);
        public static bool operator !=(ValueTask<TResult> left, ValueTask<TResult> right) => !left.Equals(right);
        public override string? ToString() => IsCompletedSuccessfully ? Result?.ToString() : "";
    }
}

namespace System.Runtime.CompilerServices
{
    // Its only field is the task, as IFiberAwaiter needs.
    public readonly struct ValueTaskAwaiter : ICriticalNotifyCompletion, IFiberAwaiter
    {
        internal readonly Task? m_task;

        internal ValueTaskAwaiter(Task? task) { m_task = task; }

        public bool IsCompleted => m_task == null || m_task.IsCompleted;
        public void GetResult() => m_task?.Wait();
        public void OnCompleted(Action continuation) => AsyncMethods.OnCompleted(m_task ?? Task.CompletedTask, continuation);
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
    }

    public readonly struct ValueTaskAwaiter<TResult> : ICriticalNotifyCompletion, IWaitTarget
    {
        readonly ValueTask<TResult> _value;

        internal ValueTaskAwaiter(ValueTask<TResult> value) { _value = value; }

        public bool IsCompleted => _value.IsCompleted;
        public TResult GetResult() => _value.Result;
        public void OnCompleted(Action continuation) => AsyncMethods.OnCompleted(_value.AsTask(), continuation);
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
        Task IWaitTarget.WaitTarget => _value._task!; // not completed, so there's a task
    }

    public readonly struct ConfiguredValueTaskAwaitable
    {
        readonly Task? _task;

        internal ConfiguredValueTaskAwaitable(Task? task) { _task = task; }

        public ValueTaskAwaiter GetAwaiter() => new ValueTaskAwaiter(_task);
    }

    public readonly struct ConfiguredValueTaskAwaitable<TResult>
    {
        readonly ValueTask<TResult> _value;

        internal ConfiguredValueTaskAwaitable(ValueTask<TResult> value) { _value = value; }

        public ValueTaskAwaiter<TResult> GetAwaiter() => new ValueTaskAwaiter<TResult>(_value);
    }

    /// <summary>Runs `async ValueTask` methods (the C# compiler calls it). One that doesn't wait allocates nothing.</summary>
    public struct AsyncValueTaskMethodBuilder
    {
        Task? _task;

        public static AsyncValueTaskMethodBuilder Create() => default;

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void SetStateMachine(IAsyncStateMachine stateMachine) { }

        public ValueTask Task => _task == null ? default : new ValueTask(_task);

        public void SetResult() => _task?.Complete();

        public void SetException(Exception exception) => Environment.FailFast(exception.Message);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => AsyncMethods.Suspend(_task ??= new Task(), ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => AsyncMethods.Suspend(_task ??= new Task(), ref awaiter, ref stateMachine);
    }

    /// <summary>Runs `async ValueTask&lt;T&gt;` methods (the C# compiler calls it). One that doesn't wait allocates nothing.</summary>
    public struct AsyncValueTaskMethodBuilder<TResult>
    {
        Task<TResult>? _task;
        TResult _result;

        public static AsyncValueTaskMethodBuilder<TResult> Create() => default;

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

        public void SetStateMachine(IAsyncStateMachine stateMachine) { }

        // Read once the method returns: it has either waited (a task) or set its result.
        public ValueTask<TResult> Task => _task == null ? new ValueTask<TResult>(_result) : new ValueTask<TResult>(_task);

        public void SetResult(TResult result)
        {
            if (_task == null)
                _result = result;
            else
                _task.TrySetResult(result);
        }

        public void SetException(Exception exception) => Environment.FailFast(exception.Message);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => AsyncMethods.Suspend(_task ??= new Task<TResult>(), ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => AsyncMethods.Suspend(_task ??= new Task<TResult>(), ref awaiter, ref stateMachine);
    }
}

namespace System
{
    /// <summary>Something to dispose by awaiting: `await using`.</summary>
    public interface IAsyncDisposable
    {
        ValueTask DisposeAsync();
    }
}

namespace System.Collections.Generic
{
    /// <summary>A sequence read with `await foreach`.</summary>
    public interface IAsyncEnumerable<out T>
    {
        IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
    }

    public interface IAsyncEnumerator<out T> : IAsyncDisposable
    {
        T Current { get; }
        ValueTask<bool> MoveNextAsync();
    }
}
