using System;

namespace LAV.EventBus
{
    /// <summary>
    /// Provides data for the error event in the event bus.
    /// </summary>
    public class EventBusErrorEventArgs<T> : EventArgs where T: class
    {
        /// <summary>
        /// Gets the exception that occurred.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Gets the event data associated with the error.
        /// </summary>
        public T EventData { get; }

        /// <summary>
        /// Gets the handler delegate that was processing the event.
        /// </summary>
        public Delegate Handler { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="EventBusErrorEventArgs{T}"/> class.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        /// <param name="eventData">The event data associated with the error.</param>
        /// <param name="handler">The handler delegate that was processing the event.</param>
        public EventBusErrorEventArgs(Exception exception, T eventData, Delegate handler)
        {
            Exception = exception;
            EventData = eventData;
            Handler = handler;
        }
    }

    public class EventBusErrorEventArgs : EventBusErrorEventArgs<object>
    {
        public EventBusErrorEventArgs(Exception exception, object eventData, Delegate handler) : base(exception, eventData, handler)
        {
        }
    }
}
