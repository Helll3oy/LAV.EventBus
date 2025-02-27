using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LAV.EventBus
{
    public abstract class EventItemBase : IDisposable
    {
        protected ConcurrentDictionary<Delegate, DelegateInfo> Handlers { get; private set; }
            = new ConcurrentDictionary<Delegate, DelegateInfo>();
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        private ConcurrentDictionary<DelegateInfo, byte> _weakDelegateInfos = new ConcurrentDictionary<DelegateInfo, byte>();

        protected bool disposedValue;

        public bool ProcessStarted { get; private set; }

        private static void InvokeEventHandler<TEvent>(TEvent @event, Delegate eventHandler, CancellationToken cancellationToken) 
            where TEvent : Event
        {
            if (eventHandler is Func<TEvent, CancellationToken, Task> asyncEventHandler)
            {
                _ = asyncEventHandler(@event, cancellationToken).ConfigureAwait(false);
            }
            else if (eventHandler is Action<TEvent> actionEventHandler)
            {
                _ = Task.Run(() => actionEventHandler(@event), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException(nameof(eventHandler));
            }
        }

        public bool AddHandler<TDelegateInfo>(Delegate handler, TDelegateInfo delegateInfo)
            where TDelegateInfo : DelegateInfo
        {
            if (disposedValue) return false;

            if (delegateInfo == null) throw new ArgumentNullException(nameof(delegateInfo));

            _semaphoreSlim.Wait();
            try
            {
                if (delegateInfo.IsWeakReference)
                {
                    _weakDelegateInfos.TryAdd(delegateInfo, 1);

                    return true;
                }
                else
                {
                    if (Handlers.ContainsKey(handler)) return false;

                    if (Handlers.TryAdd(handler, delegateInfo)) return true;
                }

                throw new ArgumentException(nameof(handler));
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }

        public async Task<bool> AddHandlerAsync<TDelegateInfo>(Delegate handler, TDelegateInfo delegateInfo, 
            CancellationToken cancellationToken = default) 
            where TDelegateInfo : DelegateInfo
        {
            if (disposedValue) return false;

            if (delegateInfo == null) throw new ArgumentNullException(nameof(delegateInfo));

            await _semaphoreSlim.WaitAsync(cancellationToken);
            try
            {
                if (delegateInfo.IsWeakReference)
                {
                    _weakDelegateInfos.TryAdd(delegateInfo, 1);

                    return true;
                }
                else
                {
                    if (Handlers.ContainsKey(handler)) return false;

                    if (Handlers.TryAdd(handler, delegateInfo)) return true;
                }

                throw new ArgumentException(nameof(handler));
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }

        protected abstract Task ListenInternalAsync<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : Event;

        public async Task ListenAsync<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            if (ProcessStarted || disposedValue) return;

            ProcessStarted = true;

            await ListenInternalAsync<TEvent>(cancellationToken);//.ConfigureAwait(false);

            ProcessStarted = false;

        }

        protected abstract void PublishInternal<TEvent>(TEvent eventToPublish)
            where TEvent : Event;

        public void Publish<TEvent>(TEvent eventToPublish)
            where TEvent : Event
        {
            if (disposedValue) return;

            PublishInternal(eventToPublish);
        }

        protected abstract Task PublishInternalAsync<TEvent>(TEvent eventToPublish, CancellationToken cancellationToken = default)
            where TEvent : Event;

        public async Task PublishAsync<TEvent>(TEvent eventToPublish, CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            if (disposedValue) return;

            await PublishInternalAsync(eventToPublish, cancellationToken).ConfigureAwait(false);
        }

        protected abstract void CompleteInternal();
        protected abstract void ReopenInternal();

        public void Reset(bool reopen = true)
        {
            if (disposedValue) return;

            _semaphoreSlim.Wait();
            try
            {
                CompleteInternal();
                if (!reopen && Handlers != null && !Handlers.IsEmpty)
                {
                    Handlers.Clear();
                }
            }
//#if NETSTANDARD2_0_OR_GREATER
//            catch (System.Threading.Channels.ChannelClosedException)
//            {

//            }
//#endif
            finally
            {
                if (reopen) ReopenInternal();

                _semaphoreSlim.Release();
            }
        }

        protected void ProcessEventHandlers<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : Event
        {
            if(disposedValue) return;

            var iterator = Handlers?.GetEnumerator();

            if(iterator == null) return;

            while (iterator?.MoveNext() ?? false)
            {
                if (disposedValue) return;

                var eventHandlerInfo = iterator.Current.Value;

                if (eventHandlerInfo.HasFilter && !eventHandlerInfo.ApplyFilter(@event)) continue;

                var eventHandler = iterator.Current.Key;
                InvokeEventHandler<TEvent>(@event, eventHandler, cancellationToken);
            }

            if (!_weakDelegateInfos.IsEmpty)
            {
                foreach (var weakDelegateInfo in _weakDelegateInfos.Keys)
                {
                    if (weakDelegateInfo.IsWeakAlive)
                    {
                        if (weakDelegateInfo.HasFilter && !weakDelegateInfo.ApplyFilter(@event)) continue;

                        var eventHandler = weakDelegateInfo.CreateDelegate();

                        InvokeEventHandler<TEvent>(@event, eventHandler, cancellationToken);
                    }
                    else
                    {
                        _weakDelegateInfos.TryRemove(weakDelegateInfo, out var _);
                    }
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue) return;

            if (disposing)
            {
                Reset(false);
                Handlers.Clear();
                _semaphoreSlim.Dispose();
            }

            disposedValue = true;

            Handlers = null;
            _semaphoreSlim = null;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}