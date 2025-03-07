using System;

namespace LAV.EventBus
{
    public enum ExecutionMode { Sequential, Parallel }

    public class HandlerOptions
    {
        public ExecutionMode? Mode { get; set; }
        public TimeSpan? Timeout { get; set; }
        public CircuitBreakerSettings? CircuitBreaker { get; set; }
        public Action<Exception> ErrorHandler { get; set; }
    }
}
