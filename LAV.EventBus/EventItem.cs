using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#if NETSTANDARD2_0_OR_GREATER
using System.Threading.Channels;
#endif

namespace LAV.EventBus
{
    public sealed class EventItem : IDisposable
    {
        private bool disposedValue;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

#if NETSTANDARD2_0_OR_GREATER
        private System.Threading.Channels.Channel<object> _channel
            = System.Threading.Channels.Channel.CreateUnbounded<object>();
#else
        //private AsyncEnumerator<object> _enumerator = new AsyncEnumerator<object>(); 
        private CustomChannel<object> _channel = new CustomChannel<object>(); 
#endif

        private ConcurrentDictionary<Delegate, DelegateInfo> _handlers
            = new ConcurrentDictionary<Delegate, DelegateInfo>();

        public bool ProcessStarted { get; private set; }

        private ConcurrentDictionary<DelegateInfo, byte> _weakDelegateInfos = new ConcurrentDictionary<DelegateInfo, byte>();

        public bool AddHandler<TDelegateInfo>(Delegate handler, TDelegateInfo delegateInfo)
            where TDelegateInfo : DelegateInfo
        {
            if(disposedValue) return false;

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
                    if (_handlers.ContainsKey(handler)) return false;

                    if (_handlers.TryAdd(handler, delegateInfo)) return true;
                }

                throw new ArgumentException(nameof(handler));
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }

        public async Task<bool> AddHandlerAsync<TDelegateInfo>(Delegate handler, TDelegateInfo delegateInfo, CancellationToken cancellationToken = default)
            where TDelegateInfo : DelegateInfo
        {
            if(disposedValue) return false;

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
                    if (_handlers.ContainsKey(handler)) return false;

                    if (_handlers.TryAdd(handler, delegateInfo)) return true;
                }

                throw new ArgumentException(nameof(handler));
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }

        public void Publish<TEvent>(TEvent eventToPublish)
            where TEvent : Event
        {
            if(disposedValue) return;

#if NETSTANDARD2_0_OR_GREATER
            _channel.Writer.TryWrite(eventToPublish);
#else
            //_enumerator.AddItem(eventToPublish);
            Task.WaitAll(_channel.Writer.WriteAsync(eventToPublish));
#endif
        }

        public async Task PublishAsync<TEvent>(TEvent eventToPublish, CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            if(disposedValue) return;

#if NETSTANDARD2_0_OR_GREATER
            if(!await _channel.Writer.WaitToWriteAsync(cancellationToken)) 
                throw new OperationCanceledException();
                
            await _channel.Writer.WriteAsync(eventToPublish, cancellationToken);
#else
            //await _enumerator.AddItemAsync(eventToPublish);
            await _channel.Writer.WriteAsync(eventToPublish, cancellationToken);
#endif
        }

        public async Task ListenAsync<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            if(ProcessStarted || disposedValue) return;

            ProcessStarted = true;

#if NETSTANDARD2_0_OR_GREATER
            await foreach (TEvent @event in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (_handlers == null) continue;

                ProcessEventHandlers<TEvent>(@event, cancellationToken);
            }
#else
            while (!(disposedValue || await _channel?.Reader.IsCompletedAsync(cancellationToken)))
            {
                try
                {
                    var eventToHandle = await _channel?.Reader.ReadAsync(cancellationToken);

                    if (_handlers == null || eventToHandle is not TEvent @event)
                        continue;

                    ProcessEventHandlers<TEvent>(@event, cancellationToken);
                }
                catch (InvalidOperationException e)
                {
                    break;
                }
            }
#endif
            
            ProcessStarted = false;
        
        }

        private void ProcessEventHandlers<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : Event
        {
            var iterator = _handlers.GetEnumerator();
            while (iterator.MoveNext())
            {
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

        private static void InvokeEventHandler<TEvent>(TEvent @event, Delegate eventHandler, CancellationToken cancellationToken) where TEvent : Event
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

        public void Reset(bool reopen = true)
        {
            if(disposedValue) return;

            _semaphoreSlim.Wait();
            try
            {
#if NETSTANDARD2_0_OR_GREATER
                _channel?.Writer.TryComplete();
#else
                _channel?.Writer.Complete();
#endif
                if(!reopen && _handlers != null && !_handlers.IsEmpty)
                {
                    _handlers.Clear();
                }
            }
#if NETSTANDARD2_0_OR_GREATER
            catch(System.Threading.Channels.ChannelClosedException)
            {

            }
#endif
            finally
            {
#if NETSTANDARD2_0_OR_GREATER
                if(reopen) _channel = System.Threading.Channels.Channel.CreateUnbounded<object>();
#else
                if (reopen) _channel?.Reset();
#endif
                _semaphoreSlim.Release();
            }
        }

        private void Dispose(bool disposing)
        {
            if (disposedValue) return;

            if (disposing)
            {
                // Dispose managed state (managed objects)
                Reset(false);
                _handlers.Clear();
                _semaphoreSlim.Dispose();
            }

            // Free unmanaged resources (unmanaged objects) and override finalizer
            // Set large fields to null
            disposedValue = true;

            _channel = null;
            _handlers = null;
            _semaphoreSlim = null;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
