using System;

namespace LAV.EventBus
{
    /// <summary>
    /// Represents the event data for logging events in the EventBus.
    /// </summary>
    /// <remarks>
    /// This class is used to encapsulate the message associated with an EventBus log event.
    /// </remarks>
    /// <param name="message">The log message associated with the event.</param>
    public sealed class EventBusLogEventArgs : EventArgs
    {
        public EventBusLogEventArgs(string message) => Message = message;

        public string Message { get; }
    }
}
