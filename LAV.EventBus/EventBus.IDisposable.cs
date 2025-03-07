using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

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

            _cts?.Cancel();
            _signal?.Release();
            _cts?.Dispose();
            _signal?.Dispose();
            _completionSignal?.Dispose();

            disposedValue = true;

            if (disposing)
            {
                // Clear all handlers
                foreach (var eventHandlers in _handlers.Select(s => s.Value))
                {
                    eventHandlers.Lock.EnterUpgradeableReadLock();
                    try
                    {
                        int groupedHandlersCount = eventHandlers.Handlers.Count;
                        for (var i = groupedHandlersCount - 1; i >= 0; i--)
                        {
                            var handlers = eventHandlers.Handlers.ElementAt(i).Value;

                            int handlersCount = handlers.Count;
                            for (var j = handlersCount - 1; j >= 0; j--)
                            {
                                var handler = handlers[j];
                                handler.Token.Dispose();
                            }
                        }

                        eventHandlers.Handlers.Clear();
                    }
                    finally
                    {
                        eventHandlers.Lock.ExitUpgradeableReadLock();
                    }

                    eventHandlers.Handlers.Clear();
                }

                _handlers.Clear();
            }
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
