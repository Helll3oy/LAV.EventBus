using System;

namespace LAV.EventBus
{

    internal class HandlerInfo<TEvent> : HandlerInfo 
    {
        private new Func<TEvent, bool> Filter { get; }

        public HandlerInfo(object handler, int priority, int order, Func<TEvent, bool> filter = null, bool isWeakReference = false, bool isAsync = false)
            : base(handler, priority, order, filter, isWeakReference, isAsync)
        {
            Filter = filter;
        }
    }

    /// <summary>
    /// Represents information about an event handler.
    /// </summary>
    internal class HandlerInfo
    {
        /// <summary>
        /// Gets the handler, which can be a WeakHandler or a direct Delegate.
        /// </summary>
        public object Handler { get; }

        /// <summary>
        /// Gets the priority of the handler.
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Gets the order of the handler.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Gets the filter delegate for the handler.
        /// </summary>
        public Delegate Filter { get; }

        /// <summary>
        /// Gets a value indicating whether the handler is a weak reference.
        /// </summary>
        public bool IsWeakReference { get; }

        /// <summary>
        /// Gets a value indicating whether the handler is asynchronous.
        /// </summary>
        public bool IsAsync { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HandlerInfo"/> class.
        /// </summary>
        /// <param name="handler">The handler, which can be a WeakHandler or a direct Delegate.</param>
        /// <param name="priority">The priority of the handler.</param>
        /// <param name="order">The order of the handler.</param>
        /// <param name="filter">The filter delegate for the handler.</param>
        /// <param name="isWeakReference">Indicates whether the handler is a weak reference.</param>
        /// <param name="isAsync">Indicates whether the handler is asynchronous.</param>
        public HandlerInfo(
            object handler,
            int priority,
            int order,
            Delegate filter = null,
            bool isWeakReference = false,
            bool isAsync = false)
        {
            if (priority < 0) throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be non-negative.");
            if (order < 0) throw new ArgumentOutOfRangeException(nameof(order), "Order must be non-negative.");

            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Priority = priority;
            Order = order;
            Filter = filter;
            IsWeakReference = isWeakReference;
            IsAsync = isAsync;
        }
    }
}
