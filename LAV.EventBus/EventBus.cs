using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
using System.Threading.Channels;
#endif

#if NET5_0_OR_GREATER
using System.Diagnostics;
using System.Diagnostics.Metrics;
#endif

namespace LAV.EventBus
{
    public sealed partial class EventBus
    {
#if NET5_0_OR_GREATER
        private readonly Meter _meter;
        private readonly Counter<int> _eventsProcessedCounter;
#endif
        private readonly EventBusOptions _options;
        public EventBusOptions Options => _options;

        private readonly ConcurrentBag<Exception> _handlerExceptions = new();

        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cts = new();

        private readonly ConcurrentDictionary<Type, EventTypeState> _eventStates = new();
        private readonly ConcurrentQueue<Type> _pendingEventTypes = new();
        private readonly ConcurrentDictionary<Type, byte> _processingTypes = new();

        private readonly SemaphoreSlim _completionSignal = new(0, int.MaxValue);
        private int _activeEventCount = 0;

        //public event EventHandler<EventBusErrorEventArgs> OnError;
        //public event EventHandler<EventBusLogEventArgs> OnLog;
        //public event EventHandler<EventBusSubscribeEventArgs> OnSubscribe;

        public EventBus(EventBusOptions options = null)
        {
            //IProcessEngine engine = null;

            _options = options ?? EventBusOptions.Default;
            _options.Validate();

            if (_options.EnableMetrics)
            {
#if NET5_0_OR_GREATER
                _meter = new Meter("EventBus");
                _eventsProcessedCounter = _meter.CreateCounter<int>("events-processed");
#endif
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

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
            var state = _eventStates.GetOrAdd(eventType, CreateEventTypeState(eventType));

            var data = new EventInvocation(typeof(TEvent), eventData);
            try
            {
                HandleQueueFull(state, data);

                if (_processingTypes.TryAdd(eventType, 0))
                {
                    _pendingEventTypes.Enqueue(eventType);
                    _signal.Release();
                }
            }
            catch (ChannelClosedException)
            {
                throw new ObjectDisposedException("EventBus is disposed");
            }
#else
            if (_eventStates.TryGetValue(eventType, out var state))
            {
                var data = new EventInvocation(typeof(TEvent), eventData);
                if (state.Storage.Count >= _options.MaxQueueSize)
                {
                    HandleQueueFull(state, data);
                    return;
                }

                Interlocked.Increment(ref _activeEventCount);
                state.Storage.Enqueue(data);

                if (_processingTypes.TryAdd(eventType, 0))
                {
                    _pendingEventTypes.Enqueue(eventType);
                    _signal.Release();
                }
            }
#endif
        }

        public async ValueTask PublishAsync<TEvent>(TEvent eventData)
        {
#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
            ThrowIfDisposed();
            var eventType = typeof(TEvent);

            if (_eventStates.TryGetValue(eventType, out var state))
            {
                //var state = _eventStates.GetOrAdd(eventType, CreateEventTypeState(eventType));

                var data = new EventInvocation(typeof(TEvent), eventData);
                try
                {
                    Interlocked.Increment(ref _activeEventCount);
                    await HandleQueueFullAsync(state, data);

                    if (_processingTypes.TryAdd(eventType, 0))
                    {
                        _pendingEventTypes.Enqueue(eventType);
                        _signal.Release();
                    }

                }
                catch (ChannelClosedException)
                {
                    throw new ObjectDisposedException("EventBus is disposed");
                }
            }
#else
            await Task.Run(() => Publish(eventData));
#endif
        }

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
        private EventTypeState CreateEventTypeState(Type eventType)
        {
            var channelOptions = new BoundedChannelOptions(_options.MaxQueueSize)
            {
                FullMode = _options.QueueFullStrategy switch
                {
                    QueueFullStrategy.Block => BoundedChannelFullMode.Wait,
                    QueueFullStrategy.DropNew => BoundedChannelFullMode.DropNewest,
                    QueueFullStrategy.ThrowException => BoundedChannelFullMode.DropWrite,
                    _ => throw new ArgumentOutOfRangeException(nameof(eventType))
                },
                SingleWriter = false,
                SingleReader = true
            };

            return new EventTypeState(channelOptions);
        }

        private async ValueTask HandleQueueFullAsync(EventTypeState state, EventInvocation data)
        {
            switch (_options.QueueFullStrategy)
            {
                case QueueFullStrategy.Block:
                    await state.Storage.Writer.WriteAsync(data, _cts.Token);
                    break;
                case QueueFullStrategy.DropNew:
                    if (!state.Storage.Writer.TryWrite(data)) return;
                    break;
                case QueueFullStrategy.ThrowException:
                    if (!state.Storage.Writer.TryWrite(data))
                        throw new InvalidOperationException(
                            $"Event queue full for {data.EventType.Name}");
                    break;
            }
        }

        private void HandleQueueFull(EventTypeState state, EventInvocation data)
        {
            switch (_options.QueueFullStrategy)
            {
                case QueueFullStrategy.Block:
                case QueueFullStrategy.DropNew:
                    if (!state.Storage.Writer.TryWrite(data)) return;
                    break;
                case QueueFullStrategy.ThrowException:
                    if (!state.Storage.Writer.TryWrite(data))
                        throw new InvalidOperationException(
                            $"Event queue full for {data.EventType.Name}");
                    break;
            }
        }
#else
        private EventTypeState CreateEventTypeState(Type eventType)
        {
            return new EventTypeState();
        }

        private void HandleQueueFull(EventTypeState state, EventInvocation data)
        {
            switch (_options.QueueFullStrategy)
            {
                case QueueFullStrategy.Block:
                    SpinWait.SpinUntil(() =>
                        state.Storage.Count < _options.MaxQueueSize);
                    break;
                case QueueFullStrategy.DropNew:
                    return;
                case QueueFullStrategy.ThrowException:
                    throw new InvalidOperationException(
                        $"Event queue full for type {data.EventType.Name}");
            }
        }
#endif

        private void AddHandler(Type eventType, EventBusHandler handler)
        {
            var state = _eventStates.GetOrAdd(eventType, CreateEventTypeState(eventType));

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
    }
}
