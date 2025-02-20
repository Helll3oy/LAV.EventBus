using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LAV.EventBus
{
    public sealed class EventBus : IDisposable
    {
        private readonly ConcurrentDictionary<Type, ConcurrentBag<HandlerInfo>> _handlers = new ConcurrentDictionary<Type, ConcurrentBag<HandlerInfo>>();
        //private readonly object _lock = new object();
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
            if (_disposed) throw new ObjectDisposedException(nameof(EventBus));

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler), "Handler cannot be null.");
            }

            var eventType = typeof(TEvent);

            var handlerObj = useWeakReference
                ? new WeakHandler(handler)
                : (object)handler;

            var handlerInfo = new HandlerInfo<TEvent>(
                handler: handlerObj,
                priority: priority,
                order: _handlerOrderCounter++,
                filter: filter,
                isWeakReference: useWeakReference,
                isAsync: isAsync);

            var handlersBag = _handlers.GetOrAdd(eventType, _ => new ConcurrentBag<HandlerInfo>());
            handlersBag.Add(handlerInfo);
        }

        public void Publish<TEvent>(TEvent eventData)
        {
            PublishInternal(eventData, async: false).ConfigureAwait(false).GetAwaiter().GetResult();
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

        private void ProcessHandler<TEvent>(HandlerInfo handlerInfo, TEvent eventData, bool async, List<Task> tasks)
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
            _handlers.Clear();
            _disposed = true;
        }
    }
}
