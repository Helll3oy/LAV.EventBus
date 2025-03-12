using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

using LAV.EventBus.Helpers;

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

    internal abstract class EventBusHandler : IEquatable<EventBusHandler>
    {
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
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled(cancellationToken);

            _handler((TEvent)eventData);
            return ValueTask.CompletedTask;
#else
            if (cancellationToken.IsCancellationRequested)
                return TaskHelpers.CreateCanceledValueTask(cancellationToken);

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

        public override async ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1 || NET6_0_OR_GREATER
            if (cancellationToken.IsCancellationRequested)
                await ValueTask.FromCanceled(cancellationToken);

            await _handler((TEvent)eventData);//.WaitAsync(cancellationToken);
#else
            if (cancellationToken.IsCancellationRequested)
                await TaskHelpers.CreateCanceledValueTask(cancellationToken);

            await _handler((TEvent)eventData);
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

        public override async ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
            if (cancellationToken.IsCancellationRequested)
                await ValueTask.FromCanceled(cancellationToken);

            await _handler((TEvent)eventData).WaitAsync(cancellationToken).ConfigureAwait(false);
#else
            if (cancellationToken.IsCancellationRequested)
                return TaskHelpers.CreateCanceledValueTask(cancellationToken);

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

        public override async ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
            await _handler((TEvent)eventData, cancellationToken).ConfigureAwait(false);
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

        public override async ValueTask HandleAsync(object eventData, CancellationToken cancellationToken = default)
        {
            await new ValueTask(_handler((TEvent)eventData, cancellationToken)).ConfigureAwait(false);
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