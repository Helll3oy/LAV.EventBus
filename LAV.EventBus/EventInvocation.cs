using System;
using System.Diagnostics;

namespace LAV.EventBus
{
    internal sealed record EventInvocation
    {
        public Type EventType { get; }
        public object EventData { get; }

        public TimeSpan Timestamp { get; } = TimeSpan.FromTicks(Stopwatch.GetTimestamp());

        public EventInvocation(Type eventType, object eventData)
        {
            EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            EventData = eventData ?? throw new ArgumentNullException(nameof(eventData));
        }
    }
}
