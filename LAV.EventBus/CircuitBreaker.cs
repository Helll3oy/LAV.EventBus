using System;
using System.Threading;

namespace LAV.EventBus
{
    public struct CircuitBreakerSettings()
    {
        /// <summary>
        /// Number of failures before opening circuit
        /// </summary>
        public int? FailureThreshold { get; set; }

        /// <summary>
        /// Duration to keep circuit open
        /// </summary>
        public TimeSpan? DurationOfBreak { get; set; }

        /// <summary>
        /// Enable circuit breaker
        /// </summary>
        public bool? Enabled { get; set; }
    }

    public class CircuitBreaker
    {
        private int _failures;
        private DateTimeOffset _blockedUntil;

        public CircuitBreakerSettings Settings { get; }
        public bool IsOpen => DateTimeOffset.UtcNow < _blockedUntil;

        public CircuitBreaker(CircuitBreakerSettings settings) => Settings = settings;

        public void RecordSuccess() => Interlocked.Exchange(ref _failures, 0);

        public void RecordFailure()
        {
            if (Interlocked.Increment(ref _failures) >= Settings.FailureThreshold)
            {
                _blockedUntil = DateTimeOffset.UtcNow + Settings.DurationOfBreak.Value;
            }
        }
    }

}
