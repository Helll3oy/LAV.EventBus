using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LAV.EventBus.Helpers
{
#if NET5_0_OR_GREATER
    internal class CustomChannel<T> : System.Threading.Channels.Channel<T>
    { }
#else
    internal class CustomChannel<T> : IDisposable
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim _itemsAvailable = new SemaphoreSlim(0, int.MaxValue);
        private readonly SemaphoreSlim _spaceAvailable;

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private bool _isCompleted = false;
        private bool disposedValue;

        public ChannelReader<T> Reader { get; }
        public ChannelWriter<T> Writer { get; }

        public CustomChannel(int capacity = int.MaxValue)
        {
            _spaceAvailable = new SemaphoreSlim(capacity, capacity);
            Reader = new ChannelReader<T>(this);
            Writer = new ChannelWriter<T>(this);
        }

        public async Task<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(CustomChannel<T>));

            await _spaceAvailable.WaitAsync(cancellationToken); // Wait for space to be available

            return true;
        }

        public async Task WriteAsync(T item, CancellationToken cancellationToken = default)
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(CustomChannel<T>));

            await _spaceAvailable.WaitAsync(cancellationToken); // Wait for space to be available

            _queue.Enqueue(item); // Enqueue the item
            _itemsAvailable.Release(); // Signal that an item is available
        }

        public async Task<T> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(CustomChannel<T>));

            await _itemsAvailable.WaitAsync(cancellationToken); // Wait for an item to be available

            if (_queue.TryDequeue(out T item))
            {
                _spaceAvailable.Release(); // Signal that space is available
                return item;
            }

            //return default;
            throw new InvalidOperationException("No item available in the queue.");
        }

        // Internal method to mark the channel as complete
        private void CompleteInternal()
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(CustomChannel<T>));

            if(!_isCompleted)
            { 
                _isCompleted = true;

                if(_itemsAvailable.CurrentCount == 0)
                    _itemsAvailable.Release(int.MaxValue); // Release all waiting readers
            }
        }

        public void Complete()
        {
            _lock.Wait();
            try
            {
                CompleteInternal();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                await Task.Run(() => CompleteInternal(), cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        // Internal method to check if the channel is completed
        private bool IsCompletedInternal()
        {
            return _isCompleted && _queue.Count == 0;
        }

        public bool IsCompleted()
        {
            return IsCompletedInternal();
        }

        public Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => IsCompletedInternal(), cancellationToken);
        }

        public void Reset()
        {
            if (disposedValue)
                throw new ObjectDisposedException(nameof(CustomChannel<T>));

            _lock.Wait();
            try
            {
                _isCompleted = false;
            }
            finally
            {
                _lock.Release();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue) return;

            if (disposing)
            {
                // TODO: освободить управляемое состояние (управляемые объекты)

            }

            // TODO: освободить неуправляемые ресурсы (неуправляемые объекты) и переопределить метод завершения
            // TODO: установить значение NULL для больших полей
            disposedValue = true;

            _lock.Dispose();
            _itemsAvailable.Dispose();
            _spaceAvailable.Dispose();
        }

        // // TODO: переопределить метод завершения, только если "Dispose(bool disposing)" содержит код для освобождения неуправляемых ресурсов
        // ~CustomChannel()
        // {
        //     // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    internal class ChannelReader<T>
    {
        private readonly CustomChannel<T> _channel;

        internal ChannelReader(CustomChannel<T> channel)
        {
            _channel = channel;
        }

        // Read an item from the channel
        public async Task<T> ReadAsync(CancellationToken cancellationToken = default)
        {
            return await _channel.ReadAsync(cancellationToken);
        }

        // Check if the channel is completed and empty
        public bool IsCompleted()
        {
            return _channel.IsCompleted();
        }

        public Task<bool> IsCompletedAsync(CancellationToken cancellationToken = default)
        {
            return _channel.IsCompletedAsync(cancellationToken);
        }
    }

    internal class ChannelWriter<T>
    {
        private readonly CustomChannel<T> _channel;

        internal ChannelWriter(CustomChannel<T> channel)
        {
            _channel = channel;
        }

        // Write an item to the channel
        public async Task WriteAsync(T item, CancellationToken cancellationToken = default)
        {
            await _channel.WriteAsync(item, cancellationToken);
        }

        // Mark the channel as complete
        public void Complete()
        {
            _channel.Complete();
        }

        public async Task CompleteAsync()
        {
            await _channel.CompleteAsync();
        }
    }
#endif
    }
