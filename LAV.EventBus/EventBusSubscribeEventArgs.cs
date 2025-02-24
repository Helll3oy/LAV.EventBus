using System;

namespace LAV.EventBus
{
    public sealed class EventBusSubscribeEventArgs : EventArgs
    {
        public EventBusSubscribeEventArgs(Delegate @delegate, DelegateInfo delegateInfo)
        {
            Delegate = @delegate;
            DelegateInfo = delegateInfo;
        }

        public Delegate Delegate { get; }
        public DelegateInfo DelegateInfo { get; }
    }
}
