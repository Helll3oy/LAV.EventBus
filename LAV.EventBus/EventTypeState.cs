using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

//using System.Diagnostics;
//using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
using System.Threading.Channels;
using System.Buffers;
#endif

using LAV.EventBus.Helpers;

//using Microsoft.IO;
//using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace LAV.EventBus
{
    internal class EventTypeState
    {
#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
        private readonly Channel<EventInvocation> _storage;
        public Channel<EventInvocation> Storage => _storage;
#else
        //private readonly RecyclableMemoryStreamManager _memoryStreamManager = new();

        private readonly ConcurrentQueue<EventInvocation> _storage = new();
        public ConcurrentQueue<EventInvocation> Storage => _storage;
#endif
        public ReaderWriterLockSlim Lock = new();
        public SortedDictionary<int, List<EventBusHandler>> Handlers = [];

        private List<EventBusHandler> _cachedHandlers = [];
        public IReadOnlyList<EventBusHandler> CachedHandlers
        {
            get
            {
                Lock.EnterReadLock();
                try
                {
                    return _cachedHandlers;
                }
                finally
                {
                    Lock.ExitReadLock();
                }
            }
        }

        public void UpdateCachedHandlers()
        {
            _cachedHandlers = Handlers
                .OrderBy(kvp => kvp.Key)
                .SelectMany(kvp => kvp.Value)
                .ToList();
        }

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
        protected EventTypeState() { }

        public EventTypeState(BoundedChannelOptions storageOptions)
        {
            _storage = Channel.CreateBounded<EventInvocation>(storageOptions);
        }
#else
        public EventTypeState() { }
#endif

        private List<EventBusHandler> GetHandlersSnapshot()
        {
            var snapshot = new List<EventBusHandler>();
            foreach (var priorityGroup in Handlers)
            {
                snapshot.AddRange(priorityGroup.Value);
            }
            return snapshot;
        }

        //private List<EventBusHandler> GetHandlersSnapshotFast()
        //{
        //    // Calculate total handlers for capacity
        //    int totalHandlers = Handlers.Sum(pg => pg.Value.Count);
        //    var snapshot = new List<EventBusHandler>(totalHandlers);

        //    // Use RecyclableMemoryStream for temporary storage
        //    using var memoryStream = _memoryStreamManager.GetStream();
        //    var writer = new BinaryWriter(memoryStream);

        //    foreach (var priorityGroup in Handlers)
        //    {
        //        foreach (var handler in priorityGroup.Value)
        //        {
        //            // Write handler reference (example)
        //            writer.Write(handler.GetHashCode()); // Or other identifier
        //            snapshot.Add(handler);
        //        }
        //    }

        //    return snapshot;
        //}

        //private List<EventBusHandler> GetHandlersSnapshotDeepCopy()
        //{
        //    using var memoryStream = _memoryStreamManager.GetStream();

        //    var formatter = new BinaryFormatter();
        //    formatter.Serialize(memoryStream, Handlers);

        //    memoryStream.Position = 0;

        //    var snapshot = (SortedList<int, List<EventBusHandler>>)formatter.Deserialize(memoryStream);

        //    var result = new List<EventBusHandler>();
        //    foreach (var priorityGroup in snapshot)
        //    {
        //        result.AddRange(priorityGroup.Value);
        //    }

        //    return result;
        //}

        private List<EventBusHandler> GetHandlersSnapshotPool()
        {
            var totalHandlers = Handlers.Sum(pg => pg.Value.Count);
            var snapshot = ArrayPool<EventBusHandler>.Shared.Rent(totalHandlers);

            try
            {
                int index = 0;
                foreach (var priorityGroup in Handlers)
                {
                    foreach (var handler in priorityGroup.Value)
                    {
                        snapshot[index++] = handler;
                    }
                }
                return [.. snapshot.Take(index)]; // Slice to actual size
            }
            catch
            {
                ArrayPool<EventBusHandler>.Shared.Return(snapshot);
                throw;
            }
        }
    }


}
