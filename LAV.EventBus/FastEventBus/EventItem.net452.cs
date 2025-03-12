using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LAV.EventBus.Helpers;

namespace LAV.EventBus.FastEventBus
{
    public sealed partial class EventItem
    {
        private CustomChannel<object> _channel = new CustomChannel<object>();

        protected override void PublishInternal<TEvent>(TEvent eventToPublish)
        {
            Task.WaitAll(_channel.Writer.WriteAsync(eventToPublish));
        }

        protected override async Task PublishInternalAsync<TEvent>(TEvent eventToPublish, CancellationToken cancellationToken = default)
        {
            await _channel.Writer.WriteAsync(eventToPublish, cancellationToken);
        }

        protected override async Task ListenInternalAsync<TEvent>(CancellationToken cancellationToken = default)
        {
            while (!(disposedValue || await _channel?.Reader.IsCompletedAsync(cancellationToken)))
            {
                if(disposedValue) break;

                try
                {
                    var eventToHandle = await _channel?.Reader.ReadAsync(cancellationToken);

                    if (Handlers == null || eventToHandle is not TEvent @event)
                        continue;

                    ProcessEventHandlers<TEvent>(@event, cancellationToken);
                }
                catch (InvalidOperationException e)
                {
                    break;
                }
                catch (Exception e)
                {
                    break;
                }
            }
        }

        protected override void CompleteInternal()
        {
            _channel?.Writer.Complete();
        }

        protected override void ReopenInternal()
        {
            _channel?.Reset();
        }
    }
}
