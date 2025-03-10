using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using LAV.EventBus.Compatibility;
using Microsoft.IO;

namespace LAV.EventBus
{
    public sealed partial class EventBus
    {
        private static readonly RecyclableMemoryStreamManager _memoryManager = new RecyclableMemoryStreamManager();

        public async Task WaitAllEventsCompleted(CancellationToken token = default, Action<int> progressCallback = null)
        {
            await WaitAllEventsCompletedInternal(progressCallback, token);

            if (_handlerExceptions.Any())
            {
                throw new AggregateException(_handlerExceptions);
            }
        }

        private async Task WaitAllEventsCompletedInternal(Action<int> progressCallback, CancellationToken token)
        {
            if (progressCallback != null)
            {
                while (Volatile.Read(ref _activeEventCount) > 0)
                {
                    progressCallback.Invoke(_activeEventCount);
                    await _completionSignal.WaitAsync(token);
                }
            }
            else
            {
                while (Volatile.Read(ref _activeEventCount) > 0)
                {
                    await _completionSignal.WaitAsync(token);
                }
            }
        }

        public async Task<bool> TryWaitAllEventsCompleted(TimeSpan timeout, CancellationToken token = default, Action<int> progressCallback = null)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                timeoutCts.Token
            );

            try
            {
                await WaitAllEventsCompletedInternal(progressCallback, linkedCts.Token);

                return true;
            }
            catch (OperationCanceledException)
            {
                return false; // Timeout occurred
            }
        }

        private async Task ProcessEventsAsync(CancellationToken token)
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Options.MaxDegreeOfParallelism,
            };

            int maxBatchSize = Options.BatchSize;
            var typesToProcess = new HashSet<Type>();

            while (!token.IsCancellationRequested)
            {
                await _signal.WaitAsync(token);

                // Process all pending event types
                while (_pendingEventTypes.TryDequeue(out var eventType))
                {
                    _pendingTypes.TryRemove(eventType, out _);
                    typesToProcess.Add(eventType);
                }

                // Process each type's events in parallel
#if NET6_0
                await Parallel.ForEachAsync(typesToProcess, parallelOptions, async (eventType, ct) =>
#else
                await typesToProcess.ForEachAsync(parallelOptions, async (eventType, ct) =>
#endif
                {
                    if (!_eventStates.TryGetValue(eventType, out var state)) return;

                    var remainingEvents = 0;
                    var batch = new List<EventInvocation>(maxBatchSize);
                    while (state.EventQueue.TryDequeue(out var eventData))
                    {
                        batch.Add(eventData);

                        if (!(batch.Count < maxBatchSize && state.EventQueue.Count >= maxBatchSize))
                        {
                            var handlers = state.CachedHandlers;

                            await ProcessBatch(eventType, batch, handlers, ct);

                            remainingEvents = Interlocked.Add(ref _activeEventCount, -batch.Count);

                            batch.Clear();
                        }
                    }

                    if (remainingEvents <= 0)
                    {
                        _completionSignal.Release();
                    }
                    //else
                    //{
                    //    throw new InvalidOperationException(nameof(remainingEvents));
                    //}
                });
            }
        }

        private async Task ProcessBatch(
            Type eventType,
            IReadOnlyList<EventInvocation> batch,
            IReadOnlyList<EventBusHandler> handlers,
            CancellationToken ct)
        {
            if (_options.DefaultExecutionMode == ExecutionMode.Parallel)
            {
#if NET6_0
                await Parallel.ForEachAsync(batch, ct, async (data, innerCt) =>
#else
                await batch.ForEachAsync(ct, async (data, innerCt) =>
#endif
                {
                    await ProcessEvent(eventType, data, handlers, innerCt);
                });
            }
            else
            {
                foreach (var data in batch)
                {
                    await ProcessEvent(eventType, data, handlers, ct);
                }
            }

            if (_options.EnableMetrics)
            {
                //_eventsProcessedCounter.Add(batch.Count);
            }
        }

        private async ValueTask ProcessEvent(
            Type eventType,
            EventInvocation eventInvocation,
            IReadOnlyList<EventBusHandler> handlers,
            CancellationToken ct)
        {
#if NETSTANDARD2_1 || NETAPPCORE3_0 || NET5
            await foreach (var handler in handlers)
            {
                await ExecuteHandler(handler, eventInvocation, ct);
            }
#else
            await handlers.ForEachAsync(ct, async (handler, innerCt) =>
            {
                await ExecuteHandler(handler, eventInvocation, innerCt);
            });
#endif
        }

        private async ValueTask ExecuteHandler(
            EventBusHandler handler,
            EventInvocation eventInvocation,
            CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource();
            if (handler.Options.Timeout != Timeout.InfiniteTimeSpan)
            {
                timeoutCts.CancelAfter(handler.Options.Timeout.Value);
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                timeoutCts.Token
            );

            try
            {
                if (handler.CircuitBreaker?.IsOpen == true) return;

                await handler.HandleAsync(eventInvocation.EventData, linkedCts.Token);
                handler.CircuitBreaker?.RecordSuccess();

                //Console.WriteLine($"{eventInvocation.Timestamp} - {eventInvocation.EventData}");
            }
            catch (Exception ex)
            {
                handler.CircuitBreaker?.RecordFailure();
                handler.Options.ErrorHandler?.Invoke(ex);

                _handlerExceptions.Add(ex);
            }
        }

        private async Task ProcessEventsAsync1(CancellationToken cancellationToken)
        {
            const int maxBatchSize = 10;
            var batch = new List<EventInvocation>(maxBatchSize);

            while (!cancellationToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(cancellationToken);

                while (_eventQueue.TryDequeue(out var invocation) && batch.Count < maxBatchSize)
                {
                    batch.Add(invocation);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

#if NET6_0
                await Parallel.ForEachAsync(batch, parallelOptions, async (data, ct) =>
#else
                await ParallelExtensions.ForEachAsync(batch, parallelOptions, async (data, ct) =>
                //batch.OrderBy(o => o.Timestamp).ToList().ForEach(async (data) =>
#endif
                {
                    if (data is not EventInvocation eventInvoke) return;

                    var eventType = eventInvoke.EventType;
                    if (!_handlers.TryGetValue(eventType, out var eventHandlers)) return;

                    //using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    //    ct,
                    //    _cts.Token
                    //);

                    var seqTask = eventHandlers.SequentalCachedSnapshot.Count > 0
                        ? new Task[] {
                            Task.Run(async () =>
                            {
                                // Chain tasks sequentially
                                var tasks = eventHandlers.SequentalCachedSnapshot
                                    .Select(s => {
                                        return (Func<Task>)(() => ExecuteHandler(s, eventInvoke, _cts.Token).AsTask());
                                    });

                                var chain = tasks.Aggregate(
                                    (prevTask, nextTask) => async () =>
                                    {
                                        await prevTask();
                                        await nextTask();
                                    }
                                );

                                await chain();
                            }, _cts.Token)}
                        : [];

                    var parTasks = eventHandlers.ParallelCachedSnapshot.Count > 0
                        ? eventHandlers.ParallelCachedSnapshot
                            .Select(s => ExecuteHandler(s, eventInvoke, _cts.Token).AsTask())
                        : [];

                    await Task.WhenAll(parTasks.Concat(seqTask));
                });

                // Decrement active event count and signal completion if necessary
                var remainingEvents = Interlocked.Add(ref _activeEventCount, -batch.Count);
                if (remainingEvents == 0)
                {
                    _completionSignal.Release();
                }

                batch.Clear();
            }
        }

        private static List<EventBusHandler> GetHandlersSnapshot(SortedDictionary<int, List<EventBusHandler>> handlers)
        {
            var snapshot = new List<EventBusHandler>();
            foreach (var priorityGroup in handlers)
            {
                snapshot.AddRange(priorityGroup.Value);
            }
            return snapshot;
        }

        private static List<EventBusHandler> GetHandlersSnapshotFast(SortedList<int, List<EventBusHandler>> handlers)
        {
            // Calculate total handlers for capacity
            int totalHandlers = handlers.Sum(pg => pg.Value.Count);
            var snapshot = new List<EventBusHandler>(totalHandlers);

            // Use RecyclableMemoryStream for temporary storage
            using var memoryStream = _memoryManager.GetStream();
            var writer = new BinaryWriter(memoryStream);

            foreach (var priorityGroup in handlers)
            {
                foreach (var handler in priorityGroup.Value)
                {
                    // Write handler reference (example)
                    writer.Write(handler.GetHashCode()); // Or other identifier
                    snapshot.Add(handler);
                }
            }

            return snapshot;
        }

        private static List<EventBusHandler> GetHandlersSnapshotDeepCopy(SortedList<int, List<EventBusHandler>> handlers)
        {
            using var memoryStream = _memoryManager.GetStream();

            var formatter = new BinaryFormatter();
            formatter.Serialize(memoryStream, handlers);

            memoryStream.Position = 0;

            var snapshot = (SortedList<int, List<EventBusHandler>>)formatter.Deserialize(memoryStream);

            var result = new List<EventBusHandler>();
            foreach (var priorityGroup in snapshot)
            {
                result.AddRange(priorityGroup.Value);
            }

            return result;
        }

        private static List<EventBusHandler> GetHandlersSnapshotPool(SortedDictionary<int, List<EventBusHandler>> handlers)
        {
            var totalHandlers = handlers.Sum(pg => pg.Value.Count);
            var snapshot = ArrayPool<EventBusHandler>.Shared.Rent(totalHandlers);

            try
            {
                int index = 0;
                foreach (var priorityGroup in handlers)
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
