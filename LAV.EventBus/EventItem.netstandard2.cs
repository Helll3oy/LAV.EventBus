using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using System.Threading.Channels;


namespace LAV.EventBus
{
    public sealed partial class EventItem : EventItemBase
    {
        private System.Threading.Channels.Channel<object> _channel
            = System.Threading.Channels.Channel.CreateUnbounded<object>();

        protected override void PublishInternal<TEvent>(TEvent eventToPublish)
        {
            _channel.Writer.TryWrite(eventToPublish);
        }

        protected override async Task PublishInternalAsync<TEvent>(TEvent eventToPublish, CancellationToken cancellationToken = default)
        {
            if (!await _channel.Writer.WaitToWriteAsync(cancellationToken))
                throw new OperationCanceledException();

            await _channel.Writer.WriteAsync(eventToPublish, cancellationToken);
        }

        protected override async Task ListenInternalAsync<TEvent>(CancellationToken cancellationToken = default)
        {

            await foreach (TEvent @event in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (Handlers == null) continue;

                ProcessEventHandlers<TEvent>(@event, cancellationToken);
            }
        }
        protected override void CompleteInternal()
        {
            _channel?.Writer.TryComplete();

        }

        protected override void ReopenInternal()
        {
            _channel = System.Threading.Channels.Channel.CreateUnbounded<object>();
        }
    }
}
