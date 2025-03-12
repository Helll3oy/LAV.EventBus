using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

#if NETSTANDARD2_0_OR_GREATER
using System.Threading.Channels;
#endif

namespace LAV.EventBus.FastEventBus
{
    public sealed partial class EventItem : EventItemBase
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _channel = null;
        }
    }
}
