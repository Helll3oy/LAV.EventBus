using System;
using System.Linq;

namespace LAV.EventBus
{
    public sealed partial class EventBus : IDisposable
    {
        private bool disposedValue;

        private void ThrowIfDisposed()
        {
            if (!disposedValue) return;

            throw new ObjectDisposedException(nameof(EventBus), "The EventBus has been disposed.");
        }

        private void Dispose(bool disposing)
        {
            if (disposedValue) return;
            disposedValue = true;

            _cts?.Cancel();
            _signal?.Release();
            _completionSignal?.Release();

            if (disposing)
            {

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
                foreach (var state in _eventStates.Values)
                {
                    state.Storage.Writer.Complete();
                }
#endif

                //// Clear all handlers
                //foreach (var eventHandlers in _handlers.Select(s => s.Value))
                //{
                //    eventHandlers.Lock.EnterUpgradeableReadLock();
                //    try
                //    {
                //        int groupedHandlersCount = eventHandlers.Handlers.Count;
                //        for (var i = groupedHandlersCount - 1; i >= 0; i--)
                //        {
                //            var handlers = eventHandlers.Handlers.ElementAt(i).Value;

                //            int handlersCount = handlers.Count;
                //            for (var j = handlersCount - 1; j >= 0; j--)
                //            {
                //                var handler = handlers[j];
                //                handler.Token.Dispose();
                //            }
                //        }

                //        eventHandlers.Handlers.Clear();
                //    }
                //    finally
                //    {
                //        eventHandlers.Lock.ExitUpgradeableReadLock();
                //    }

                //    eventHandlers.Handlers.Clear();
                //}

                //_handlers.Clear();
            }

            _cts?.Dispose();
            _signal?.Dispose();
            _completionSignal?.Dispose();
        }

        ~EventBus()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
