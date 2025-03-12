using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using LAV.EventBus.Helpers;

namespace LAV.EventBus
{
    public sealed partial class EventBus
    {
        private bool _stopAfterAllCompleted = false;

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

            _stopAfterAllCompleted = true;
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

        private async Task ProcessEventsAsync(CancellationToken token)
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            };

            while (!token.IsCancellationRequested)
            {
                await _signal.WaitAsync(token);

                var typesToProcess = new HashSet<Type>();

                // Process all pending event types
                while (_pendingEventTypes.TryDequeue(out var eventType))
                {
                    if (_eventStates.TryGetValue(eventType, out _))
                    {
                        typesToProcess.Add(eventType);
                    }

                    _processingTypes.TryRemove(eventType, out _);
                }

                // Process each type's events in parallel
#if NET6_0_OR_GREATER
                await Parallel.ForEachAsync(typesToProcess, parallelOptions, async (eventType, ct) =>
#else
                await typesToProcess.ForEachAsync(parallelOptions, async (eventType, ct) =>
#endif
                {
                    if (!_eventStates.TryGetValue(eventType, out var state)) return;

                    await ProcessEventType(eventType, state, ct);
                });
            }
        }

        private async Task ProcessEventType(Type eventType, EventTypeState state, CancellationToken ct)
        {
            var remainingEvents = 0;
            int maxBatchSize = _options.BatchSize;
            var batch = new List<EventInvocation>(maxBatchSize);

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
            var reader = state.Storage.Reader;

            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var eventData))
                {
#else
            var reader = state.Storage;
            while (reader.TryDequeue(out var eventData))
            {
                while (batch.Count < maxBatchSize && reader.Count >= maxBatchSize)
                {
#endif
                    batch.Add(eventData);
                    if (batch.Count >= _options.BatchSize) break;
                }

                if (batch.Count > 0)
                {
                    var handlers = state.CachedHandlers;
                    await ProcessBatch(eventType, batch, handlers, ct);
                    remainingEvents = Interlocked.Add(ref _activeEventCount, -batch.Count);

                    batch.Clear();

                    if(_stopAfterAllCompleted && remainingEvents <= 0)
                    {
                        break;
                    }
                }
            }

            //while (state.Storage.TryDequeue(out var eventData))
            //{
            //    while (batch.Count < maxBatchSize && state.Storage.Count >= maxBatchSize)
            //    {
            //        batch.Add(eventData);
            //        if (batch.Count >= _options.BatchSize) break;
            //    }

            //    if (!(batch.Count < maxBatchSize && state.Storage.Count >= maxBatchSize))
            //    {
            //        var handlers = state.CachedHandlers;
            //        await ProcessBatch(eventType, batch, handlers, ct);
            //        remainingEvents = Interlocked.Add(ref _activeEventCount, -batch.Count);

            //        batch.Clear();
            //    }
            //} 

            if (remainingEvents <= 0)
            {
                _completionSignal.Release();
            }
        }

        private async Task ProcessBatch(
            Type eventType,
            IReadOnlyList<EventInvocation> batch,
            IReadOnlyList<EventBusHandler> handlers,
            CancellationToken ct)
        {
            var options = new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = _options.DefaultExecutionMode == ExecutionMode.Parallel
                    ? _options.MaxDegreeOfParallelism
                    : 1
            };

#if NET6_0_OR_GREATER
            await Parallel.ForEachAsync(batch, options, async (data, innerCt) =>
#else
            await batch.ForEachAsync(options, async (data, innerCt) =>
#endif
            {
                await ProcessEvent(eventType, data, handlers, innerCt);
            });

            if (_options.EnableMetrics)
            {
                _eventsProcessedCounter.Add(batch.Count);
            }
        }

        private async ValueTask ProcessEvent(
            Type eventType,
            EventInvocation eventInvocation,
            IReadOnlyList<EventBusHandler> handlers,
            CancellationToken ct)
        {
#if NET8_0_OR_GREATER 
            await foreach (var handler in handlers.ToAsyncEnumerable(ct))
            {
                await ExecuteHandler(handler, eventInvocation, ct);
            }
#elif NET6_0_OR_GREATER
            await Parallel.ForEachAsync(handlers, ct, async (handler, innerCt) =>
            {
                await ExecuteHandler(handler, eventInvocation, innerCt);
            });
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
    }
}
