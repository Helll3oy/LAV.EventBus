using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

namespace LAV.EventBus
{
    public enum EventHandlerPriority
    {
        VeryHigh = byte.MinValue,
        High = 64,
        Medium = 128,
        Low = 196,
        VeryLow = byte.MaxValue
    }

    internal interface IHandlerWrapper
    {
        Task<bool> HandleAsync(object eventData, CancellationToken token = default);
    }

#if !(NET6_0 || NETSTANDARD2_1)
    public static class TaskExtensions
    {
        public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            // Create a Task that completes after the specified timeout
            var timeoutTask = Task.Delay(timeout, cancellationToken);

            // Use Task.WhenAny to wait for either the original task or the timeout task
            var completedTask = await Task.WhenAny(task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("The operation has timed out.");
            }

            // Ensure the original task is completed (in case it threw an exception)
            await task;
        }
    }
#endif

    internal abstract class EventBusHandler : IEquatable<EventBusHandler>
    {
#if !(NETCOREAPP2_1 || NETSTANDARD2_1)
        public static Task<T> CreateCanceledTask<T>(CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetCanceled(); // Mark the task as canceled
            return tcs.Task;
        }

        public static Task CreateCanceledTask(CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<byte>();
            tcs.SetCanceled(); // Mark the task as canceled
            return tcs.Task;
        }

        public static ValueTask<T> CreateCanceledValueTask<T>(CancellationToken cancellationToken = default)
        {
            // Create a canceled Task and wrap it in a ValueTask
            var canceledTask = CreateCanceledTask<T>(cancellationToken);
            return new ValueTask<T>(canceledTask);
        }

        public static ValueTask CreateCanceledValueTask(CancellationToken cancellationToken = default)
        {
            // Create a canceled Task and wrap it in a ValueTask
            var canceledTask = CreateCanceledTask(cancellationToken);
            return new ValueTask(canceledTask);
        }
#endif
        public int Priority { get; }
        public HandlerOptions Options { get; }
        public CircuitBreaker CircuitBreaker { get; }

        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

        public SubscriptionToken Token { get; private set;  }

        protected EventBusHandler(int priority, HandlerOptions options)
        {
            Priority = priority;
            Options = options;
            CircuitBreaker = options.CircuitBreaker != null
                ? new CircuitBreaker(options.CircuitBreaker.Value)
                : null;
        }

        public void SetToken(SubscriptionToken token)
        {
            Token = token;
        }

        protected EventBusHandler(EventHandlerPriority priority, HandlerOptions options) : this((int)priority, options) { }

        public abstract ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default);
        public abstract bool Equals(EventBusHandler other);
    }

    internal sealed class SyncHandlerWrapper<TEvent> : EventBusHandler
    {
        private readonly Action<TEvent> _handler;

        public SyncHandlerWrapper(Action<TEvent> handler) : 
            this(handler, EventHandlerPriority.VeryLow) { }
        public SyncHandlerWrapper(Action<TEvent> handler, EventHandlerPriority priority) : 
            this(handler, (int)priority, null) { }
        public SyncHandlerWrapper(Action<TEvent> handler, int priority, HandlerOptions options) : 
            base(priority, options)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }


        public override ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
#if NETCOREAPP2_1 || NETSTANDARD2_1
            if (ct.IsCancellationRequested)
                return ValueTask.FromCanceled(ct);

            _handler((TEvent)eventData);
            return ValueTask.CompletedTask;
#else 
            if (cancellationToken.IsCancellationRequested)
                return CreateCanceledValueTask(cancellationToken);

            _handler((TEvent)eventData);
            return default(ValueTask);
#endif
            //try
            //{
            //    _handler((TEvent)eventData);
            //    return Task.FromResult(true);
            //}
            //catch
            //{
            //    return Task.FromResult(false);
            //}
        }

        public override int GetHashCode() => HashCode.Combine(_handler, Priority);

        public override bool Equals(object obj)
        {
            return obj is SyncHandlerWrapper<TEvent> other &&
                   EqualityComparer<Action<TEvent>>.Default.Equals(_handler, other._handler) &&
                   Priority == other.Priority;
        }
        public override bool Equals(EventBusHandler other) => Equals((object)other);
    }

    internal sealed class AsyncHandlerWrapper<TEvent> : EventBusHandler
    {
        private readonly Func<TEvent, ValueTask> _handler;

        public AsyncHandlerWrapper(Func<TEvent, ValueTask> handler) : 
            this(handler, EventHandlerPriority.VeryLow) { }
        public AsyncHandlerWrapper(Func<TEvent, ValueTask> handler, EventHandlerPriority priority) : 
            this(handler, (int)priority, null) { }
        public AsyncHandlerWrapper(Func<TEvent, ValueTask> handler, int priority, HandlerOptions options) : 
            base(priority, options)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public override ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
#if NETCOREAPP2_1 || NETSTANDARD2_1
            if (ct.IsCancellationRequested)
                return ValueTask.FromCanceled(ct);

            return _handler((TEvent)eventData).WaitAsync(cancellationToken);
#else 
            if (cancellationToken.IsCancellationRequested)
                return CreateCanceledValueTask(cancellationToken);

            return _handler((TEvent)eventData);
#endif
        }

        public override bool Equals(object obj)
        {
            return obj is AsyncHandlerWrapper<TEvent> other &&
                   EqualityComparer<Func<TEvent, ValueTask>>.Default.Equals(_handler, other._handler) &&
                   Priority == other.Priority;
        }
        public override bool Equals(EventBusHandler other) => Equals((object)other);

        public override int GetHashCode() => HashCode.Combine(_handler, Priority);
    }

    internal sealed class AsyncHandlerTaskWrapper<TEvent> : EventBusHandler
    {
        private readonly Func<TEvent, Task> _handler;

        public AsyncHandlerTaskWrapper(Func<TEvent, Task> handler) : 
            this(handler, EventHandlerPriority.VeryLow) { }
        public AsyncHandlerTaskWrapper(Func<TEvent, Task> handler, EventHandlerPriority priority) : 
            this(handler, (int)priority, null) { }
        public AsyncHandlerTaskWrapper(Func<TEvent, Task> handler, int priority, HandlerOptions options) : 
            base(priority, options)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public override ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
#if NETCOREAPP2_1 || NETSTANDARD2_1
            if (ct.IsCancellationRequested)
                return ValueTask.FromCanceled(ct);

            return new ValueTask(_handler((TEvent)eventData).WaitAsync(cancellationToken));
#else 
            if (cancellationToken.IsCancellationRequested)
                return CreateCanceledValueTask(cancellationToken);

            return new ValueTask(_handler((TEvent)eventData));
#endif
        }

        public override bool Equals(object obj)
        {
            return obj is AsyncHandlerTaskWrapper<TEvent> other &&
                   EqualityComparer<Func<TEvent, Task>>.Default.Equals(_handler, other._handler) &&
                   Priority == other.Priority;
        }
        public override bool Equals(EventBusHandler other) => Equals((object)other);

        public override int GetHashCode() => HashCode.Combine(_handler, Priority);
    }

    internal sealed class CanceledAsyncHandlerWrapper<TEvent> : EventBusHandler
    {
        private readonly Func<TEvent, CancellationToken, ValueTask> _handler;

        public CanceledAsyncHandlerWrapper(Func<TEvent, CancellationToken, ValueTask> handler) : 
            this(handler, EventHandlerPriority.VeryLow) { }
        public CanceledAsyncHandlerWrapper(Func<TEvent, CancellationToken, ValueTask> handler, EventHandlerPriority priority) : 
            this(handler, (int)priority, null) { }
        public CanceledAsyncHandlerWrapper(Func<TEvent, CancellationToken, ValueTask> handler, int priority, HandlerOptions options) : 
            base(priority, options)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public override ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
            return _handler((TEvent)eventData, cancellationToken);
        }

        public override bool Equals(object obj)
        {
            return obj is CanceledAsyncHandlerWrapper<TEvent> other &&
                   EqualityComparer<Func<TEvent, CancellationToken, ValueTask>>.Default.Equals(_handler, other._handler) &&
                   Priority == other.Priority;
        }
        public override bool Equals(EventBusHandler other) => Equals((object)other);

        public override int GetHashCode() => HashCode.Combine(_handler, Priority);
    }

    internal sealed class CanceledAsyncHandlerTaskWrapper<TEvent> : EventBusHandler
    {
        private readonly Func<TEvent, CancellationToken, Task> _handler;

        public CanceledAsyncHandlerTaskWrapper(Func<TEvent, CancellationToken, Task> handler) : 
            this(handler, EventHandlerPriority.VeryLow) { }
        public CanceledAsyncHandlerTaskWrapper(Func<TEvent, CancellationToken, Task> handler, EventHandlerPriority priority) : 
            this(handler, (int)priority, null) { }
        public CanceledAsyncHandlerTaskWrapper(Func<TEvent, CancellationToken, Task> handler, int priority, HandlerOptions options) : 
            base(priority, options)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public override ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
            return new ValueTask(_handler((TEvent)eventData, cancellationToken));
        }

        public override bool Equals(object obj)
        {
            return obj is CanceledAsyncHandlerTaskWrapper<TEvent> other &&
                   EqualityComparer<Func<TEvent, CancellationToken, Task>>.Default.Equals(_handler, other._handler) &&
                   Priority == other.Priority;
        }
        public override bool Equals(EventBusHandler other) => Equals((object)other);

        public override int GetHashCode() => HashCode.Combine(_handler, Priority);
    }
}