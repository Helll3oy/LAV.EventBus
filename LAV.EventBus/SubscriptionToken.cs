using System;
using System.Collections.Generic;
using System.Text;

namespace LAV.EventBus
{
    internal sealed class SubscriptionToken : IDisposable
    {
        private Action _unsubscribeAction;

        public SubscriptionToken(Action unsubscribeAction)
        {
            _unsubscribeAction = unsubscribeAction;
        }

        public void Dispose()
        {
            _unsubscribeAction?.Invoke();
            _unsubscribeAction = null;
        }
    }
}
