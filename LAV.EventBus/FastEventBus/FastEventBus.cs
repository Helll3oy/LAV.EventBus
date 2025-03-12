using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Linq.Expressions;
using System.Reflection;
using System.Linq;

namespace LAV.EventBus.FastEventBus
{
    /// <summary>
    /// Represents a fast event bus for managing event subscriptions and publications.
    /// </summary>
    /// <remarks>
    /// This class provides methods to publish and subscribe to events, both synchronously and asynchronously.
    /// It manages event handlers using a concurrent dictionary and ensures thread safety with semaphores.
    /// The class also supports unsubscribing from events and processing all subscribed events.
    /// Implements <see cref="IDisposable"/> to release resources.
    /// </remarks>
    public sealed class FastEventBus : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 5);
        private readonly SemaphoreSlim _processSemaphore = new SemaphoreSlim(1, 1);
        private ConcurrentBag<Task> _tasks = new ConcurrentBag<Task>();
        private ConcurrentDictionary<Type, EventItem> _eventItems = new ConcurrentDictionary<Type, EventItem>();

        public event EventHandler<EventBusErrorEventArgs> OnError;

        public event EventHandler<EventBusLogEventArgs> OnLog;

        public event EventHandler<EventBusSubscribeEventArgs> OnSubscribe;

        public FastEventBus()
        {
        }

        private void RaiseErrorEvent(Exception ex, object eventData, Delegate eventHandler = null)
        {
            var handler = OnError;
            if (handler == null) return;

            Task.Factory
                .StartNew(() => handler.Invoke(this, new EventBusErrorEventArgs(ex, eventData, eventHandler)))
#if DEBUG                
                .ContinueWith(t =>
                {
                    Debug.WriteLineIf(t.IsFaulted, "OnError event handler threw an exception: " + t.Exception);
                })
#endif
                .ConfigureAwait(false);
        }

        private void RaiseLogEvent(string message)
        {
            var handler = OnLog;
            if (handler == null) return;

            Task.Factory
                .StartNew(() => handler.Invoke(this, new EventBusLogEventArgs(message)))
#if DEBUG 
                .ContinueWith(t =>
                {
                    Debug.WriteLineIf(t.IsFaulted, "OnLog event handler threw an exception: " + t.Exception);
                })
#endif
                .ConfigureAwait(false);
        }

        private void RaiseSubscribeEvent(Delegate @delegate, DelegateInfo delegateInfo)
        {
            var handler = OnSubscribe;
            if (handler == null) return;
            //handler.Invoke(this, new EventBusSubscribeEventArgs(@delegate, delegateInfo));

            Task.Factory
                .StartNew(() => handler.Invoke(this, new EventBusSubscribeEventArgs(@delegate, delegateInfo)))
#if DEBUG 
                .ContinueWith(t =>
                {
                    Debug.WriteLineIf(t.IsFaulted, "OnSubscribe event handler threw an exception: " + t.Exception);
                })
#endif
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes an event of the specified type to all subscribed handlers.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event to publish, which must inherit from Event.</typeparam>
        /// <param name="eventToPublish">The event instance to be published.</param>
        /// <remarks>
        /// If there are no handlers subscribed for the event type, the method returns without action.
        /// </remarks>
        public void Publish<TEvent>(TEvent eventToPublish) where TEvent : Event
        {
            Type type = typeof(TEvent);
            if (!_eventItems.TryGetValue(type, out var eventItem)) return;

            eventItem.Publish(eventToPublish);
        }

        public async Task PublishAsync<TEvent>(TEvent eventToPublish, CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            Type type = typeof(TEvent);
            if (!_eventItems.TryGetValue(type, out var eventItem)) return;

            await eventItem.PublishAsync(eventToPublish, cancellationToken);
        }

        public bool Unsubscribe<TEvent>() where TEvent : Event
        {
            _semaphoreSlim.Wait();
            return UnsubscribeInternal<TEvent>();
        }

        public async Task<bool> UnsubscribeAsync<TEvent>(CancellationToken cancellationToken = default) where TEvent : Event
        {
            await _semaphoreSlim.WaitAsync(cancellationToken);
            return UnsubscribeInternal<TEvent>();
        }

        private bool UnsubscribeInternal<TEvent>() where TEvent : Event
        {
            EventItem eventItem = null;
            Type type = typeof(TEvent);
            bool removed = false;

            try
            {
                removed = _eventItems.TryRemove(type, out eventItem);
                if (removed)
                {
                    eventItem?.Dispose();
                    RaiseLogEvent($"Unsubscribed from event: {type.Name}");
                }
                else
                {
                    throw new UnsubscribeException($"Failed to unsubscribe from event: {type.Name}");
                }
            }
            catch (Exception ex)
            {
                RaiseErrorEvent(ex, eventItem, null);
            }
            finally
            {
                _semaphoreSlim?.Release(1);
            }

            return removed;
        }

        public async Task SubscribeAsync<TEvent>(
            Action<TEvent> handler,
            Expression<Func<TEvent, bool>> filter = null,
            CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            var delegateInfo = new DelegateInfo<TEvent>(handler, filter);

            await SubscribeInternalAsync(handler, delegateInfo, cancellationToken);
        }

        public async Task SubscribeAsync<TEvent>(
            Func<TEvent, CancellationToken, Task> handler,
            Expression<Func<TEvent, bool>> filter = null,
            CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            var delegateInfo = new DelegateInfo<TEvent>(handler, filter);
            await SubscribeInternalAsync(handler, delegateInfo, cancellationToken);
        }

        //private async Task SubscribeAsync<TEvent>(
        //    Action<TEvent> handler,
        //    DelegateInfo<TEvent> delegateInfo = null,
        //    CancellationToken cancellationToken = default)
        //    where TEvent : Event
        //{
        //    await SubscribeAsync((ev, token) => Task.Run(() => handler(ev), token), delegateInfo, cancellationToken);
        //}

        private async Task SubscribeInternalAsync<TEvent>(
            //Func<TEvent, CancellationToken, Task> handler,
            Delegate handler,
            DelegateInfo<TEvent> delegateInfo = null,
            CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            await _semaphoreSlim.WaitAsync(cancellationToken);

            try
            {
                var eventItem = GetOrAddEventItem<TEvent>();

                delegateInfo ??= DelegateInfo<TEvent>.Default;

                if (await eventItem.AddHandlerAsync(handler, delegateInfo, cancellationToken))
                {
                    RaiseSubscribeEvent(handler, delegateInfo);
                }

                if (!eventItem.ProcessStarted) _tasks.Add(eventItem.ListenAsync<TEvent>(cancellationToken));

            }
            catch (Exception ex)
            {
                RaiseErrorEvent(ex, delegateInfo, handler);
            }
            finally
            {
                _semaphoreSlim?.Release(1);
            }
        }

        private EventItem GetOrAddEventItem<TEvent>()
            where TEvent : Event
        {
            Type type = typeof(TEvent);

            if (!_eventItems.TryGetValue(type, out var eventItem))
            {
                eventItem = new EventItem();
                if (!_eventItems.TryAdd(type, eventItem))
                {
                    throw new ArgumentException(nameof(type));
                }
            }

            return eventItem;
        }

        public async Task<bool> StartProcessingAsync(CancellationToken cancellationToken = default)
        {
            await _processSemaphore.WaitAsync(cancellationToken);

            try
            {
                if (_tasks.IsEmpty) return false;

                await Task.WhenAll(_tasks.AsEnumerable()).ConfigureAwait(false);
                
                return true;
            }
            catch (Exception ex)
            {
                RaiseErrorEvent(ex, null, null);
                return false;
            }
            finally
            {
                if(!disposedValue && _processSemaphore.CurrentCount > 0)
                    _processSemaphore?.Release();
            }
        }

        public bool StartProcessing()
        {
            return StartProcessingAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }

        public void CompleteAll(bool dispose = false)
        {
            if (_eventItems.IsEmpty) return;

            //Parallel.ForEach(_eventItems, eventItem =>
            //{
            //    eventItem.Value.Reset(!dispose);

            //    if (dispose) eventItem.Value.Dispose();
            //});

            foreach (var eventItem in _eventItems)
            {
                eventItem.Value.Reset(!dispose);

                if (dispose) eventItem.Value.Dispose();
            }

            _eventItems.Clear();
        }

        private bool disposedValue;

        private void Dispose(bool disposing)
        {
            if (disposedValue) return;

            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                CompleteAll(true);

                _semaphoreSlim.Dispose();
                _processSemaphore.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;

            _eventItems = null;
            _tasks = null;
        }

        // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~FastEventBus()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
