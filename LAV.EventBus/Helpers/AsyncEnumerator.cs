using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LAV.EventBus.Helpers
{
    internal class AsyncEnumerator<T>
    {
        private const int MAX_SEMAPHORE_COUNT = 10;

        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, MAX_SEMAPHORE_COUNT);
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        private readonly Queue<T> _queue = new Queue<T>();
        private TaskCompletionSource<bool> _itemAvailable = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<bool> _enumerationComplete = new TaskCompletionSource<bool>();
        private bool _isCompleted = false;


        private void AddItemInternal(T item)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_isCompleted)
                    throw new InvalidOperationException("Enumeration has already been completed.");

                _lock.EnterWriteLock();
                try
                {
                    _queue.Enqueue(item);
                    _itemAvailable.TrySetResult(true); // Signal that an item is available
                }
                finally {
                    _lock.ExitWriteLock();
                }
            }
            finally
            {
                _lock.ExitUpgradeableReadLock();
            }

        }
        // Add an item to the enumerator
        public void AddItem(T item)
        {
            _semaphoreSlim.Wait();
            try
            {
                AddItemInternal(item);
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }

            //lock (_queue)
            //{
            //    if (_isCompleted)
            //        throw new InvalidOperationException("Enumeration has already been completed.");

            //    _queue.Enqueue(item);
            //    _itemAvailable.TrySetResult(true); // Signal that an item is available
            //}
        }

        public async Task AddItemAsync(T item, CancellationToken token = default)
        {
            await _semaphoreSlim.WaitAsync(token);
            try
            {
                AddItemInternal(item);
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }

        private void CompleteInternal()
        {
            _lock.EnterWriteLock();
            try
            {
                _isCompleted = true;
                _enumerationComplete.TrySetResult(true); // Signal that enumeration is complete
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            //lock (_queue)
            //{
            //    _isCompleted = true;
            //    _enumerationComplete.TrySetResult(true); // Signal that enumeration is complete
            //}
        }

        // Mark the enumeration as complete
        public void Complete()
        {
            _semaphoreSlim.Wait();
            try
            {
                CompleteInternal();
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }

        public async Task CompleteAsync(CancellationToken token = default)
        {
            await _semaphoreSlim.WaitAsync(token);
            try
            {
                CompleteInternal();
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }

        // Get the next item asynchronously
        public async Task<T> GetNextAsync(CancellationToken token = default)
        {
            while (true)
            {
                // Wait for an item to be available or for the enumeration to complete
                var itemAvailableTask = _itemAvailable.Task;
                var completedTask = _enumerationComplete.Task;
                var finishedTask = await Task.WhenAny(itemAvailableTask, completedTask);

                if (finishedTask == completedTask)
                {
                    // Enumeration is complete
                    throw new InvalidOperationException("Enumeration has ended.");
                }

                // Reset the item available task
                _itemAvailable = new TaskCompletionSource<bool>();

                _lock.EnterUpgradeableReadLock();
                try
                {
                    if (_queue.Count > 0)
                    {
                        _lock.EnterWriteLock();
                        try
                        {
                            return _queue.Dequeue();
                        }
                        finally
                        {
                            _lock.ExitWriteLock();
                        }
                    }
                }
                finally
                {
                    _lock.ExitUpgradeableReadLock();
                }
            }
        }

        // Check if there are more items
        private bool HasMoreItemsInternal()
        {
            //lock (_queue)
            //{
            //    return !_isCompleted || _queue.Count > 0;
            //}

            _lock.EnterReadLock();
            try
            {
                return !_isCompleted || _queue.Count > 0;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public bool HasMoreItems()
        {
            return HasMoreItemsInternal();
        }

        // Check if there are more items
        public async Task<bool> HasMoreItemsAsync(CancellationToken token)
        {
            await _semaphoreSlim.WaitAsync(token);
            try
            {
                return HasMoreItemsInternal();
            }
            finally
            {
                _semaphoreSlim.Release(1);
            }
        }
    }
}
