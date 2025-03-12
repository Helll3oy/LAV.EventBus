using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

#if NETSTANDARD2_0_OR_GREATER
using System.Threading.Channels;
#endif
using System.Threading.Tasks;
using LAV.EventBus.Helpers;

namespace LAV.EventBus
{
    /// <summary>
    /// Sugar Methods Extension
    /// </summary>
    public static class EventBusExtensions
    {
        //// Simplified subscription with default priority
        //public static IDisposable Subscribe<TEvent>(
        //    this EventBus bus,
        //    Action<TEvent> handler)
        //    => bus.Subscribe(handler, priority: null);

        //public static IDisposable Subscribe<TEvent>(
        //    this EventBus bus,
        //    Func<TEvent, Task> handler)
        //    => bus.Subscribe(handler, priority: null);

        /// <summary>
        /// Fire-and-forget publishing
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="bus"></param>
        /// <param name="eventData"></param>
        public static void FireAndForget<TEvent>(this EventBus bus, TEvent eventData)
        {
            _ = bus.PublishAsync(eventData).ConfigureAwait(false);
        }

        /// <summary>
        /// Batch publishing
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="bus"></param>
        /// <param name="events"></param>
        /// <returns></returns>
        public static async Task PublishBatchAsync<TEvent>(
            this EventBus bus,
            IEnumerable<TEvent> events)
        {
            foreach (var evt in events)
            {
                await bus.PublishAsync(evt).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Conditional subscription
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="bus"></param>
        /// <param name="predicate"></param>
        /// <param name="action"></param>
        /// <param name="priority"></param>
        /// <returns></returns>
        public static IDisposable SubscribeWhen<TEvent>(
            this EventBus bus,
            Func<TEvent, bool> predicate,
            Action<TEvent> action,
            EventHandlerPriority? priority = null)
        {
            return bus.Subscribe<TEvent>(evt =>
            {
                if (predicate(evt)) action(evt);
            }, priority: priority);
        }

        public static IDisposable SubscribeWhen<TEvent>(
            this EventBus bus,
            Func<TEvent, bool> predicate,
            Func<TEvent, ValueTask> action,
            EventHandlerPriority? priority = null)
        {
            return bus.Subscribe<TEvent>(async evt =>
            {
                if (predicate(evt)) await action(evt).ConfigureAwait(false);
            }, priority: priority);
        }

        public static IDisposable SubscribeWhen<TEvent>(
            this EventBus bus,
            Func<TEvent, bool> predicate,
            Func<TEvent, CancellationToken, ValueTask> action,
            EventHandlerPriority? priority = null)
        {
            return bus.Subscribe<TEvent>(async (evt, ct) =>
            {
                if (predicate(evt)) await action(evt, ct).ConfigureAwait(false);
            }, priority: priority);
        }

        /// <summary>
        /// Debounced subscription
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="bus"></param>
        /// <param name="interval"></param>
        /// <param name="action"></param>
        /// <param name="priority"></param>
        /// <returns></returns>
        public static IDisposable Debounce<TEvent>(
            this EventBus bus,
            TimeSpan interval,
            Action<TEvent> action,
            EventHandlerPriority? priority = null)
        {
            var lastInvocation = DateTime.MinValue;
            var locker = new object();

            return bus.Subscribe<TEvent>(evt =>
            {
                lock (locker)
                {
                    if ((DateTime.UtcNow - lastInvocation) < interval) return;
                    lastInvocation = DateTime.UtcNow;
                }
                action(evt);
            }, priority: priority);
        }

        //// ValueTask support
        //public static IDisposable Subscribe<TEvent>(
        //    this EventBus bus,
        //    Func<TEvent, ValueTask> handler,
        //    int? priority = null)
        //{
        //    return bus.Subscribe<TEvent>(async evt =>
        //    {
        //        await handler(evt).ConfigureAwait(false);
        //    }, priority);
        //}

        //// Observable pattern
        //public static IAsyncEnumerable<TEvent> Observe<TEvent>(this EventBus bus)
        //{
        //    var channel = Channel.CreateUnbounded<TEvent>();
        //    var sub = bus.Subscribe<TEvent>(evt =>
        //    {
        //        channel.Writer.TryWrite(evt);
        //    });

        //    return channel.Reader.ReadAllAsync()
        //        .Finally(() => sub.Dispose());
        //}
    }

}
