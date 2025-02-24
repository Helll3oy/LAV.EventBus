using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

#if NETSTANDARD2_0_OR_GREATER
using System.Threading.Channels;
#endif

namespace LAV.EventBus
{
    public sealed class EventBus : IDisposable
    {
#if NETSTANDARD2_0_OR_GREATER
        private readonly ConcurrentDictionary<Type, Channel<object>> _eventChannels = new ConcurrentDictionary<Type, Channel<object>>();
        private readonly ConcurrentDictionary<Type, ConcurrentBag<HandlerInfo>> _handlers = new ConcurrentDictionary<Type, ConcurrentBag<HandlerInfo>>();
        private readonly int _channelCapacity = 1000; // Adjust as needed
#else
        private readonly ConcurrentDictionary<Type, ConcurrentBag<HandlerInfo>> _handlers = new ConcurrentDictionary<Type, ConcurrentBag<HandlerInfo>>();

#endif

        private int _handlerOrderCounter;

        private bool _disposed;

        public event EventHandler<EventBusErrorEventArgs> OnError;

        public void Subscribe<TEvent>(
            Action<TEvent> handler,
            int priority = 0,
            Func<TEvent, bool> filter = null,
            bool useWeakReference = false)
        {
            SubscribeInternal<TEvent>(handler, priority, filter, useWeakReference, isAsync: false);
        }

        public void SubscribeAsync<TEvent>(
            Func<TEvent, Task> handler,
            int priority = 0,
            Func<TEvent, bool> filter = null,
            bool useWeakReference = false)
        {
            SubscribeInternal<TEvent>(handler, priority, filter, useWeakReference, isAsync: true);
        }

        private void SubscribeInternal<TEvent>(
            Delegate handler,
            int priority,
            Func<TEvent, bool> filter = null,
            bool useWeakReference = false,
            bool isAsync = false)
        {
            if (_disposed) return;//throw new ObjectDisposedException(nameof(EventBus));

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler), "Handler cannot be null.");
            }

            var eventType = typeof(TEvent);

            var handlerObj = useWeakReference
                ? new WeakHandler(handler)
                : (object)handler;

            var handlerInfo = new HandlerInfo(
                handler: handlerObj,
                priority: priority,
                order: _handlerOrderCounter++,
                filter: filter,
                isWeakReference: useWeakReference,
                isAsync: isAsync);

            var handlersBag = _handlers.GetOrAdd(eventType, _ => new ConcurrentBag<HandlerInfo>());
            handlersBag.Add(handlerInfo);

#if NETSTANDARD2_0_OR_GREATER
            // Ensure a channel exists for this event type
            _eventChannels.GetOrAdd(eventType, _ => 
                Channel.CreateBounded<object>(_channelCapacity));
#endif
        }

#if NETSTANDARD2_0_OR_GREATER

        public void Publish<TEvent>(TEvent eventData)
        {
            if (_disposed) return; //throw new ObjectDisposedException(nameof(EventBus));

            var eventType = typeof(TEvent);
            if (_eventChannels.TryGetValue(eventType, out var channel))
            {
                channel.Writer.TryWrite(eventData);
            }

        }
        
        public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        {
            if (_disposed) return; //throw new ObjectDisposedException(nameof(EventBus));

            var eventType = typeof(TEvent);
            if (_eventChannels.TryGetValue(eventType, out var channel))
            {
                await channel.Writer.WriteAsync(eventData, cancellationToken);
            }
        }

        public async Task StartProcessingAsync(CancellationToken cancellationToken = default)
        {
            var processingTasks = new List<Task>();

            foreach (KeyValuePair<Type, Channel<object>> item/*(eventType, channel)*/ in _eventChannels)
            {
                processingTasks.Add(ProcessEventsAsync(item.Key, item.Value.Reader, cancellationToken));
            }

            await Task.WhenAll(processingTasks);
        }

        private async Task ProcessEventsAsync(
            Type eventType,
            ChannelReader<object> reader,
            CancellationToken cancellationToken)
        {
#if NETSTANDARD2_0 || NET451_OR_GREATER
            while (await reader.WaitToReadAsync())
            {
            //foreach (var eventData in reader.ReadAllAsync(cancellationToken))
                var eventData = await reader.ReadAsync(cancellationToken);
#elif NETSTANDARD2_0_OR_GREATER
            await foreach (var eventData in reader.ReadAllAsync(cancellationToken))
            {
#endif
                var tasks = new List<Task>();
                //foreach (var type in GetEventTypes(eventType))
                {
                    if (!_handlers.TryGetValue(eventType, out var handlers))
                    {
                        continue;
                    }

                    var handlerInfos = handlers
                        .OrderByDescending(h => h.Priority)
                        .ThenBy(h => h.Order)
                        .ToList();
                    
                    foreach (var handlerInfo in handlerInfos)
                    {
                        try
                        {
                            //ProcessHandler(handlerInfo, Convert.ChangeType(eventData, eventType), true, tasks);

                            // Handle filtering
                            if (handlerInfo.Filter is Func<object, bool> filter && !filter(eventData))
                            {
                                continue;
                            }

                            // Get actual delegate
                            Delegate handlerDelegate = handlerInfo.IsWeakReference
                                ? ((WeakHandler)handlerInfo.Handler).CreateDelegate()
                                : (Delegate)handlerInfo.Handler;

                            if (handlerDelegate == null) continue;

                            // tasks.Add(handlerInfo.IsAsync
                            //     ? ((Func<object, Task>)handlerDelegate)(eventData) //handlerDelegate.DynamicInvoke(eventData) //
                            //     : Task.Run(() => 
                            //         //((Action<object>)handlerDelegate)(Convert.ChangeType(eventData, eventType))
                            //         handlerDelegate.DynamicInvoke(eventData)
                            //         )
                            //     );

                            tasks.Add(Task.Run(() => handlerDelegate.DynamicInvoke(eventData)));
                        }
                        catch (Exception ex)
                        {
                            HandleError(ex, eventData, handlerInfo);
                        }
                    }
                }
                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
            }
        }
#else
        public void Publish<TEvent>(TEvent eventData)
        {
            if (_disposed) return; //throw new ObjectDisposedException(nameof(EventBus));

            PublishInternal(eventData, async: false).ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        public Task PublishAsync<TEvent>(TEvent eventData)
        {
            return PublishInternal(eventData, async: true);
        }

        private async Task PublishInternal<TEvent>(TEvent eventData, bool async)
        {
            if (_disposed) return; //throw new ObjectDisposedException(nameof(EventBus));

            var eventType = typeof(TEvent);
            var handlerInfos = new HashSet<HandlerInfo>();
            var deadHandlers = new HashSet<HandlerInfo>();

            // Collect all relevant handlers
            foreach (var type in GetEventTypes(eventType))
            {
                if (_handlers.TryGetValue(type, out var handlersBag))
                {
                    foreach (var handler in handlersBag)
                    {
                        if (handler.IsWeakReference)
                        {
                            var weakHandler = (WeakHandler)handler.Handler;
                            if (!weakHandler.IsAlive)
                            {
                                deadHandlers.Add(handler);
                                continue;
                            }
                        }
                        handlerInfos.Add(handler);
                    }

                    // Remove dead handlers
                    if (deadHandlers.Count > 0)
                    {
                        var newBag = new ConcurrentBag<HandlerInfo>(handlersBag.Except(deadHandlers));
                        _handlers[type] = newBag;
                        deadHandlers.Clear();

                        Console.WriteLine($"Removed {deadHandlers.Count} dead handlers for event type {type}.");
                    }
                }
            }

            // Order handlers
            var orderedHandlers = handlerInfos
                .OrderByDescending(h => h.Priority)
                .ThenBy(h => h.Order);

            // Process handlers
            var tasks = new List<Task>();
            Parallel.ForEach(orderedHandlers, handlerInfo =>
            {
                try
                {
                    ProcessHandler(handlerInfo, eventData, async, tasks);
                }
                catch (Exception ex)
                {
                    HandleError(ex, eventData, handlerInfo);
                }
            });

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
#endif

        private void ProcessHandler<TEvent>(HandlerInfo handlerInfo, TEvent eventData, bool async, IList<Task> tasks)
        {
            // Handle filtering
            if (handlerInfo.Filter is Func<TEvent, bool> filter && !filter(eventData))
            {
                return;
            }

            // Get actual delegate
            Delegate handlerDelegate = handlerInfo.IsWeakReference
                ? ((WeakHandler)handlerInfo.Handler).CreateDelegate()
                : (Delegate)handlerInfo.Handler;

            if (handlerDelegate == null) return;

            if (async)
            {
                tasks.Add(handlerInfo.IsAsync
                    ? ((Func<TEvent, Task>)handlerDelegate)(eventData)
                    : Task.Run(() => ((Action<TEvent>)handlerDelegate)(eventData)));
            }
            else
            {
                if (handlerInfo.IsAsync)
                {
                    ((Func<TEvent, Task>)handlerDelegate)(eventData).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                else
                {
                    ((Action<TEvent>)handlerDelegate)(eventData);
                }
            }
        }

        private void HandleError(Exception ex, object eventData, HandlerInfo handlerInfo)
        {
            OnError?.Invoke(this, new EventBusErrorEventArgs(
                ex,
                eventData,
                handlerInfo.Handler is WeakHandler wh ? wh.CreateDelegate() : handlerInfo.Handler as Delegate));
        }

        private static IEnumerable<Type> GetEventTypes(Type eventType)
        {
            var visited = new HashSet<Type>();
            var queue = new Queue<Type>();
            queue.Enqueue(eventType);

            while (queue.Count > 0)
            {
                var type = queue.Dequeue();
                if (visited.Add(type))
                {
                    yield return type;

                    if (type.BaseType != null)
                        queue.Enqueue(type.BaseType);

                    foreach (var interfaceType in type.GetInterfaces())
                        queue.Enqueue(interfaceType);
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;

#if NETSTANDARD2_0_OR_GREATER
            foreach (var channel in _eventChannels.Values)
            {
                channel.Writer.Complete();
            }
            _eventChannels.Clear();
#endif

            _handlers.Clear();
        }
    }
}
