using System;

namespace LAV.EventBus
{
    public interface IEvent
    {
    }

    public abstract class Event : IEvent
    {
        public DateTime Timestamp { get; } = DateTime.UtcNow;
    }
}