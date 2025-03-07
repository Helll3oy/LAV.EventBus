using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
//using System.Diagnostics;
//using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IO;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace LAV.EventBus
{
    public sealed partial class EventBus
    {
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        //private readonly Meter _meter;
        //private readonly Counter<int> _eventsProcessedCounter;
        private readonly EventBusOptions _options;
        public EventBusOptions Options => _options;

        private sealed class EventHandlers
        {
            public readonly SortedDictionary<int, List<EventBusHandler>> Handlers = new();

            private readonly List<EventBusHandler> _parallelCachedSnapshot = new();
            public IReadOnlyList<EventBusHandler> ParallelCachedSnapshot
            {
                get
                {
                    Lock.EnterReadLock();
                    try
                    {
                        return _parallelCachedSnapshot;
                    }
                    finally
                    {
                        Lock.ExitReadLock();
                    }
                }
            }

            private readonly List<EventBusHandler> _sequentalCachedSnapshot = new();
            public IReadOnlyList<EventBusHandler> SequentalCachedSnapshot
            {
                get
                {
                    Lock.EnterReadLock();
                    try
                    {
                        return _sequentalCachedSnapshot;
                    }
                    finally
                    {
                        Lock.ExitReadLock();
                    }
                }
            }

            public readonly ReaderWriterLockSlim Lock = new();

            public void UpdateCachedSnapshots()
            {
                var flatten = Handlers
                    .OrderBy(kvp => kvp.Key)
                    .SelectMany(kvp => kvp.Value.OrderBy(o => o.Timestamp));

                _parallelCachedSnapshot.Clear();
                _sequentalCachedSnapshot.Clear();

                foreach (var item in flatten)
                {
                    if (item.Options?.Mode == ExecutionMode.Sequential)
                        _sequentalCachedSnapshot.Add(item);
                    else
                        _parallelCachedSnapshot.Add(item);
                }
            }
        }

        private sealed class EventTypeState
        {
            public ConcurrentQueue<EventInvocation> EventQueue = new();
            public ReaderWriterLockSlim Lock = new();
            public SortedDictionary<int, List<EventBusHandler>> Handlers = new();

            private List<EventBusHandler> _cachedHandlers = new();
            public IReadOnlyList<EventBusHandler> CachedHandlers
            {
                get
                {
                    Lock.EnterReadLock();
                    try
                    {
                        return _cachedHandlers;
                    }
                    finally
                    {
                        Lock.ExitReadLock();
                    }
                }
            }

            public void UpdateCachedHandlers()
            {
                _cachedHandlers = Handlers
                    .OrderBy(kvp => kvp.Key)
                    .SelectMany(kvp => kvp.Value)
                    .ToList();
            }
        }

        private readonly ConcurrentBag<Exception> _handlerExceptions = new();

        private readonly ConcurrentDictionary<Type, EventHandlers> _handlers = new();
        private readonly ConcurrentQueue<EventInvocation> _eventQueue = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cts = new();

        private readonly ConcurrentDictionary<Type, EventTypeState> _eventStates = new();
        private readonly ConcurrentQueue<Type> _pendingEventTypes = new();
        private readonly ConcurrentDictionary<Type, byte> _pendingTypes = new();

        private readonly SemaphoreSlim _completionSignal = new(0, int.MaxValue);
        private int _activeEventCount = 0;

        public event EventHandler<EventBusErrorEventArgs> OnError;
        public event EventHandler<EventBusLogEventArgs> OnLog;
        public event EventHandler<EventBusSubscribeEventArgs> OnSubscribe;

        public EventBus(EventBusOptions options = null)
        {
            _options = options ?? EventBusOptions.Default;
            _options.Validate();

            _memoryStreamManager = _options.MemoryStreamManager;

            if (_options.EnableMetrics)
            {
                //_meter = new Meter("EventBus");
                //_eventsProcessedCounter = _meter.CreateCounter<int>("events-processed");
            }

            // Start a background task to process events
            Task.Run(() => ProcessEventsAsync(_cts.Token));
        }

        private static EventBusHandler CreateHandlerWrapper<TEvent>(
            Delegate handler,
            HandlerOptions options,
            int priority
        )
        {
            return handler switch
            {
                Action<TEvent> syncHandler => new SyncHandlerWrapper<TEvent>(
                    syncHandler,
                    priority,
                    options
                ),
                Func<TEvent, ValueTask> asyncHandler => new AsyncHandlerWrapper<TEvent>(
                    asyncHandler,
                    priority,
                    options
                ),
                Func<TEvent, CancellationToken, ValueTask> canceledAsyncHandler => new CanceledAsyncHandlerWrapper<TEvent>(
                    canceledAsyncHandler,
                    priority,
                    options
                ),
                Func<TEvent, Task> asyncTaskHandler => new AsyncHandlerTaskWrapper<TEvent>(
                    asyncTaskHandler,
                    priority,
                    options
                ),
                Func<TEvent, CancellationToken, Task> canceledAsyncTaskTaskHandler => new CanceledAsyncHandlerTaskWrapper<TEvent>(
                    canceledAsyncTaskTaskHandler,
                    priority,
                    options
                ),
                _ => throw new ArgumentException(
                    $"Unsupported handler type: {handler.GetType()}",
                    nameof(handler)
                )
            };
        }

        private IDisposable SubscribeInternal<TEvent>(
            Delegate handler,
            HandlerOptions options = null,
            int? priority = null)
        {
            ThrowIfDisposed();

            if (options == null)
                options = new HandlerOptions()
                {
                    CircuitBreaker = _options.CircuitBreaker,
                    ErrorHandler = _options.GlobalErrorHandler,
                    Timeout = _options.DefaultHandlerTimeout
                };
            else
            {
                options.CircuitBreaker = new CircuitBreakerSettings
                {
                    FailureThreshold = options.CircuitBreaker?.FailureThreshold ?? _options.CircuitBreaker.FailureThreshold,
                    DurationOfBreak = options.CircuitBreaker?.DurationOfBreak ?? _options.CircuitBreaker.DurationOfBreak,
                    Enabled = options.CircuitBreaker?.Enabled ?? _options.CircuitBreaker.Enabled
                };

                options.ErrorHandler ??= _options.GlobalErrorHandler;
                options.Timeout ??= _options.DefaultHandlerTimeout;
                options.Mode ??= _options.DefaultExecutionMode;
            }

            var wrapper = CreateHandlerWrapper<TEvent>(handler, options, priority ?? _options.DefaultHandlerPriority);
            AddHandler(typeof(TEvent), wrapper);
            wrapper.SetToken(new SubscriptionToken(() => RemoveHandler(typeof(TEvent), wrapper)));

            return wrapper.Token;
        }

        private IDisposable SubscribeInternal1<TEvent>(
            Delegate handler,
            HandlerOptions options = null,
            int? priority = null)
        {
            ThrowIfDisposed();
            var wrapper = CreateHandlerWrapper<TEvent>(handler, options, priority ?? _options.DefaultHandlerPriority);
            AddHandler(typeof(TEvent), wrapper);
            wrapper.SetToken(new SubscriptionToken(() => RemoveHandler(typeof(TEvent), wrapper)));

            return wrapper.Token;
        }

        public IDisposable Subscribe<TEvent>(
                Action<TEvent> handler,
                HandlerOptions options = null,
                EventHandlerPriority? priority = null)
            => SubscribeInternal<TEvent>(handler, options, (int?)priority);

        public IDisposable Subscribe<TEvent>(
                Func<TEvent, Task> handler,
                HandlerOptions options = null,
                EventHandlerPriority? priority = null)
            => SubscribeInternal<TEvent>(handler, options, (int?)priority);

        public IDisposable Subscribe<TEvent>(
                Func<TEvent, CancellationToken, Task> handler,
                HandlerOptions options = null,
                EventHandlerPriority? priority = null)
            => SubscribeInternal<TEvent>(handler, options, (int?)priority);

        private void UnsubscribeInternal<TEvent>(
            Delegate handler,
            HandlerOptions options,
            int priority)
        {
            ThrowIfDisposed();

            var wrapper = CreateHandlerWrapper<TEvent>(handler, options, priority);
            RemoveHandler(typeof(TEvent), wrapper);
        }

        private void UnsubscribeInternal1<TEvent>(
            Delegate handler,
            HandlerOptions options,
            int priority)
        {
            ThrowIfDisposed();

            var wrapper = CreateHandlerWrapper<TEvent>(handler, options, priority);
            RemoveHandler(typeof(TEvent), wrapper);
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler, HandlerOptions options = null,
                EventHandlerPriority priority = EventHandlerPriority.VeryLow)
            => UnsubscribeInternal<TEvent>(handler, options, (int)priority);

        public void Unsubscribe<TEvent>(Func<TEvent, Task> handler, HandlerOptions options = null,
                EventHandlerPriority priority = EventHandlerPriority.VeryLow)
            => UnsubscribeInternal<TEvent>(handler, options, (byte)priority);

        public void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler, HandlerOptions options = null,
                EventHandlerPriority priority = EventHandlerPriority.VeryLow)
            => UnsubscribeInternal<TEvent>(handler, options, (int)priority);

        public void Publish<TEvent>(TEvent eventData)
        {
            ThrowIfDisposed();
            var eventType = typeof(TEvent);

            if (_eventStates.TryGetValue(eventType, out var state))
            {
                if (state.EventQueue.Count >= _options.MaxQueueSize)
                {
                    HandleQueueFull(eventType);
                    return;
                }

                Interlocked.Increment(ref _activeEventCount);
                state.EventQueue.Enqueue(new EventInvocation(typeof(TEvent), eventData));

                if (_pendingTypes.TryAdd(eventType, 0))
                {
                    _pendingEventTypes.Enqueue(eventType);
                    _signal.Release();
                }
            }
        }

        private void HandleQueueFull(Type eventType)
        {
            switch (_options.QueueFullStrategy)
            {
                case QueueFullStrategy.Block:
                    SpinWait.SpinUntil(() =>
                        _eventStates[eventType].EventQueue.Count < _options.MaxQueueSize);
                    break;
                case QueueFullStrategy.DropNew:
                    return;
                case QueueFullStrategy.ThrowException:
                    throw new InvalidOperationException(
                        $"Event queue full for type {eventType.Name}");
            }
        }

        public void Publish1<TEvent>(TEvent eventData)
        {
            ThrowIfDisposed();
            Interlocked.Increment(ref _activeEventCount);
            _eventQueue.Enqueue(new EventInvocation(typeof(TEvent), eventData));
            _signal.Release();
        }

        private void AddHandler(Type eventType, EventBusHandler handler)
        {
            var state = _eventStates.GetOrAdd(eventType, _ => new EventTypeState());

            state.Lock.EnterWriteLock();
            try
            {
                if (!state.Handlers.TryGetValue(handler.Priority, out var handlers))
                {
                    handlers = new List<EventBusHandler>();
                    state.Handlers[handler.Priority] = handlers;
                }
                handlers.Add(handler);
                state.UpdateCachedHandlers();
            }
            finally
            {
                state.Lock.ExitWriteLock();
            }
        }

        private void AddHandler1(Type eventType, EventBusHandler handler)
        {
            var eventHandlers = _handlers.GetOrAdd(eventType, _ => new EventHandlers());

            eventHandlers.Lock.EnterWriteLock();
            try
            {
                //var index = FindInsertIndex(eventHandlers.Handlers, handler.Priority);
                //eventHandlers.Handlers.Insert(index, handler);

                if (!eventHandlers.Handlers.TryGetValue(handler.Priority, out var handlers))
                {
                    handlers = new List<EventBusHandler>();

                    eventHandlers.Handlers[handler.Priority] = handlers;
                }

                handlers.Add(handler);

                eventHandlers.UpdateCachedSnapshots();
            }
            finally
            {
                eventHandlers.Lock.ExitWriteLock();
            }
        }

        private void RemoveHandler(Type eventType, EventBusHandler handler)
        {
            if (!_eventStates.TryGetValue(eventType, out var state)) return;

            state.Lock.EnterWriteLock();
            try
            {
                if (state.Handlers.TryGetValue(handler.Priority, out var handlers))
                {
                    handlers.Remove(handler);
                    if (handlers.Count == 0)
                    {
                        state.Handlers.Remove(handler.Priority);
                    }
                    state.UpdateCachedHandlers();
                }
            }
            finally
            {
                state.Lock.ExitWriteLock();
            }
        }

        private void RemoveHandler1(Type eventType, EventBusHandler handler)
        {
            if (!_handlers.TryGetValue(eventType, out var eventHandlers)) return;

            eventHandlers.Lock.EnterWriteLock();
            try
            {
                if (eventHandlers.Handlers.TryGetValue(handler.Priority, out var priorityHandlers))
                {
                    priorityHandlers.Remove(handler);
                    if (priorityHandlers.Count == 0)
                    {
                        eventHandlers.Handlers.Remove(handler.Priority);
                    }

                    eventHandlers.UpdateCachedSnapshots();
                }
            }
            finally
            {
                eventHandlers.Lock.ExitWriteLock();
            }
        }

        private static int FindInsertIndex(IList<EventBusHandler> handlers, int priority)
        {
            // Binary search for optimal insertion point
            int low = 0, high = handlers.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (handlers[mid].Priority <= priority)
                    low = mid + 1;
                else
                    high = mid;
            }
            return low;
        }


    }
}
