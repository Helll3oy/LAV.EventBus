using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using LAV.EventBus.Helpers;

namespace LAV.EventBus.SimpleEventBus
{
    public sealed partial class SimpleEventBus
    {
        public void Stop()
        {
            _cts.Cancel();
        }

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
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                token,
                _cts.Token
            );

            if (progressCallback != null)
            {
                while (Volatile.Read(ref _activeEventCount) > 0)
                {
                    progressCallback.Invoke(_activeEventCount);
                    await _completionSignal.WaitAsync(linkedCts.Token);
                }
            }
            else
            {
                while (Volatile.Read(ref _activeEventCount) > 0)
                {
                    await _completionSignal.WaitAsync(linkedCts.Token);
                }
            }
        }

        public async Task<bool> TryWaitAllEventsCompleted(TimeSpan timeout, CancellationToken token = default, 
            Action<int> progressCallback = null)
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
            catch (Exception ex) when (ex is OperationCanceledException)
            {
                handler.CircuitBreaker?.RecordFailure();
                _options.GlobalErrorHandler?.Invoke(ex);
            }
            catch (Exception ex)
            {
                handler.CircuitBreaker?.RecordFailure();
                handler.Options.ErrorHandler?.Invoke(ex);

                _handlerExceptions.Add(ex);
            }
        }

        private async Task ProcessEventsAsync(CancellationToken cancellationToken)
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
                await batch.ForEachAsync(parallelOptions, async (data, ct) =>
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
