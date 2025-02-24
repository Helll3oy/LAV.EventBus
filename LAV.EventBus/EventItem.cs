using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

// #if NETSTANDARD2_0_OR_GREATER
// using System.Threading.Channels;
// #endif

namespace LAV.EventBus
{
    public sealed class EventItem : IDisposable
    {
        private bool disposedValue;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

#if NETSTANDARD2_0_OR_GREATER
        private System.Threading.Channels.Channel<object> _channel { get; set; } 
            = System.Threading.Channels.Channel.CreateUnbounded<object>();
#endif

        private ConcurrentDictionary<Delegate, DelegateInfo> _handlers
            = new ConcurrentDictionary<Delegate, DelegateInfo>();

        public bool ProcessStarted { get; private set; }

        public bool AddHandler<TDelegateInfo>(Delegate handler, TDelegateInfo delegateInfo)
            where TDelegateInfo : DelegateInfo
        {
            if(disposedValue) return false;

            _semaphoreSlim.Wait();
            try
            {
                if (_handlers.ContainsKey(handler)) return false;
                
                if (_handlers.TryAdd(handler, delegateInfo ?? DelegateInfo.Default)) return true;

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

            await _semaphoreSlim.WaitAsync(cancellationToken);
            try
            {
                if (_handlers.ContainsKey(handler)) return false;
                
                if (_handlers.TryAdd(handler, delegateInfo ?? DelegateInfo.Default)) return true;

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
            throw new NotImplementedException();
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
            await Task.FromResult(new NotImplementedException());
#endif
        }

        public async Task ListenAsync<TEvent>(CancellationToken cancellationToken = default)
            where TEvent : Event
        {
            if(ProcessStarted || disposedValue) return;

            ProcessStarted = true;
            
#if NETSTANDARD2_0_OR_GREATER
            var reader = _channel.Reader;

            while (await reader.WaitToReadAsync(cancellationToken))
            {
                var eventToHandle = await reader.ReadAsync(cancellationToken);

                if (_handlers == null || !(eventToHandle is TEvent @event))
                    continue;

                var iterator = _handlers.GetEnumerator();
                while (iterator.MoveNext())
                {
                    var eventHandler = (Func<TEvent, CancellationToken, Task>)iterator.Current.Key;
                    var eventHandlerInfo = iterator.Current.Value;

                    if(!eventHandlerInfo.ApplyFilter(@event)) continue;

                    await eventHandler(@event, cancellationToken).ConfigureAwait(false);
                }
            }
#else
            await Task.FromResult(new NotImplementedException());
#endif
            ProcessStarted = false;
        }

        public void Reset(bool reopen = true)
        {
            if(disposedValue) return;

            _semaphoreSlim.Wait();
            try
            {
#if NETSTANDARD2_0_OR_GREATER
                _channel?.Writer.Complete();
#endif
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
#endif
                _semaphoreSlim.Release(1);
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
            }

            // Free unmanaged resources (unmanaged objects) and override finalizer
            // Set large fields to null
#if NETSTANDARD2_0_OR_GREATER
            _channel = null;
#endif
            _handlers = null;

            disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
