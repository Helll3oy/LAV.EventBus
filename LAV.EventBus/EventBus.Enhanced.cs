using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LAV.EventBus
{
    /// <summary>
    /// Enhanced EventBus Class
    /// </summary>
    public sealed partial class EventBus
	{
        /// <summary>
        /// Retry policy support
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="handler"></param>
        /// <param name="retryCount"></param>
        /// <param name="priority"></param>
        /// <returns></returns>
        public IDisposable SubscribeWithRetry<TEvent>(
			Func<TEvent, Task> handler,
			int retryCount,
            EventHandlerPriority? priority = null)
		{
			return Subscribe<TEvent>(async evt =>
			{
				for (int i = 0; i < retryCount; i++)
				{
					try
					{
						await handler(evt).ConfigureAwait(false);
						return;
					}
					catch when (i == retryCount - 1)
					{
						throw;
					}
					catch
					{
						await Task.Delay(100 * (i + 1));
					}
				}
			}, priority: priority);
		}

        /// <summary>
        /// Wait for all handlers of specific event
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <returns></returns>
        public async Task WaitForCompletion<TEvent>()
		{
			var eventType = typeof(TEvent);
			if (!_eventStates.TryGetValue(eventType, out var state)) return;

#if NETSTANDARD2_0_OR_GREATER || NET5_0_OR_GREATER
			while (state.Storage.Reader.Count > 0)
			{
				await Task.Delay(100);
			}
#endif
		}

        /// <summary>
        /// Get all registered event types
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Type> GetRegisteredEventTypes()
		{
			return _eventStates.Keys.ToArray();
		}

        /// <summary>
        /// Bulk unsubscribe
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        public void UnsubscribeAll<TEvent>()
		{
			var eventType = typeof(TEvent);
			if (_eventStates.TryGetValue(eventType, out var state))
			{
				state.Lock.EnterWriteLock();
				try
				{
					state.Handlers.Clear();
                    state.UpdateCachedHandlers();
				}
				finally
				{
					state.Lock.ExitWriteLock();
				}
			}
		}
	}
}
