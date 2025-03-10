using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.IO;
using System.Threading;

namespace LAV.EventBus
{
    /// <summary>
    /// Strategy type when max queue size is reached
    /// </summary>
    public enum QueueFullStrategy
    {
        Block,
        DropNew,
        ThrowException
    }

    public sealed class EventBusOptions
    {
        /// <summary>
        /// Maximum number of events to process in parallel (default: ProcessorCount)
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Number of events to process in a single batch (default: 100)
        /// </summary>
        public int BatchSize { get; set; } = 100;

        public int ProcessedTypesSize { get; set; } = Math.Max(10, Environment.ProcessorCount);

        /// <summary>
        /// Default timeout for handler execution (default: infinite)
        /// </summary>
        public TimeSpan DefaultHandlerTimeout { get; set; } = Timeout.InfiniteTimeSpan;

        /// <summary>
        /// Circuit breaker configuration for handler fault tolerance
        /// </summary>
        public CircuitBreakerSettings CircuitBreaker { get; set; } = new()
        { 
            FailureThreshold = 5,
            DurationOfBreak = TimeSpan.FromMinutes(1),
            Enabled = true
        };

        /// <summary>
        /// Global error handler for uncaught exceptions
        /// </summary>
        public Action<Exception> GlobalErrorHandler { get; set; }

        /// <summary>
        /// Memory stream manager for event data serialization
        /// </summary>
        public RecyclableMemoryStreamManager MemoryStreamManager { get; set; } = new();

        /// <summary>
        /// Maximum number of queued events before applying backpressure (default: 10,000)
        /// </summary>
        public int MaxQueueSize { get; set; } = 100000;

        /// <summary>
        /// Strategy when max queue size is reached (default: Block)
        /// </summary>
        public QueueFullStrategy QueueFullStrategy { get; set; } = QueueFullStrategy.DropNew;

        /// <summary>
        /// Default handler execution mode (default: Parallel)
        /// </summary>
        public ExecutionMode DefaultExecutionMode { get; set; } = ExecutionMode.Parallel;

        /// <summary>
        /// Enable performance metrics collection (default: false)
        /// </summary>
        public bool EnableMetrics { get; set; } = false;

        /// <summary>
        /// Default priority for handlers without explicit priority (default: EventHandlerPriority.VeryLow)
        /// </summary>
        public int DefaultHandlerPriority { get; set; } = (int)EventHandlerPriority.VeryLow;

        /// <summary>
        /// Interval for metrics sampling (default: 1 minute)
        /// </summary>
        public TimeSpan MetricsSampleInterval { get; set; } = TimeSpan.FromMinutes(1);

        public void Validate()
        {
            if (MaxDegreeOfParallelism < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism));

            if (BatchSize < 1)
                throw new ArgumentOutOfRangeException(nameof(BatchSize));

            if (MaxQueueSize < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxQueueSize));
        }

        public static EventBusOptions Default => new EventBusOptions
        {
            BatchSize = 200,
            //MaxDegreeOfParallelism = 1,
            DefaultHandlerTimeout = TimeSpan.FromSeconds(30),
            QueueFullStrategy = QueueFullStrategy.DropNew,
            GlobalErrorHandler = ex =>
                Console.WriteLine($"Event bus error: {ex}"),
        };
    }
}
