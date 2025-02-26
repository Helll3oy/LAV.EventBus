using System;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;
using System.ComponentModel;

namespace LAV.EventBus
{
    /// <summary>
    /// Represents an abstract base class for delegate information.
    /// </summary>
    public class DelegateInfo
    {
        private static readonly Lazy<DelegateInfo> _default =
            new Lazy<DelegateInfo>(() => new DelegateInfo());

        private readonly WeakReference _weakReference;
        private readonly MethodInfo _methodInfo;
        private readonly Type _delegateType;

        public static DelegateInfo Default => _default.Value;

        public virtual string FilterSourceCode { get; }
        public virtual bool HasFilter { get; }

        public virtual Type EventType { get; } = typeof(Event);

        public bool IsWeakReference { get; } = false;
        
        /// <summary>
        /// Gets a value indicating whether the target is still alive.
        /// </summary>
        public bool IsWeakAlive => _weakReference.IsAlive;

        /// <summary>
        /// Filters the specified event.
        /// </summary>
        /// <param name="event">The event to filter.</param>
        /// <returns>True if the event passes the filter; otherwise, false.</returns>
        public virtual bool ApplyFilter(Event @event) => true;

        public DelegateInfo(Delegate handler = null, string filterSourceCode = null)
        {
            if(handler != null)
            {
                _methodInfo = handler.GetMethodInfo();
                IsWeakReference = !_methodInfo.IsStatic && !_methodInfo.IsAssembly
                    && (handler.Target?.GetType().Equals(_methodInfo.DeclaringType) ?? false);

                if(IsWeakReference)
                {
                    _weakReference = new WeakReference(handler.Target);
                }

                _delegateType = handler.GetType();
            }

            FilterSourceCode = filterSourceCode?.Trim() ?? string.Empty;
            HasFilter = !string.IsNullOrEmpty(FilterSourceCode);
        }

        /// <summary>
        /// Creates a delegate of the specified type for the target method.
        /// </summary>
        /// <returns>A delegate if the target is alive; otherwise, null.</returns>
        public Delegate CreateDelegate()
        {
            var target = _weakReference.Target;
            return target != null ? _methodInfo.CreateDelegate(_delegateType, target) : null;
        }
    }

    public sealed class DelegateInfo<TEvent> : DelegateInfo where TEvent : Event
    {
        private static readonly Lazy<DelegateInfo<TEvent>> _default =
            new Lazy<DelegateInfo<TEvent>>(() => new DelegateInfo<TEvent>());
        new public static DelegateInfo<TEvent> Default => _default.Value;

        private readonly Func<TEvent, bool> _eventFilter;

        public override Type EventType { get; } = typeof(TEvent);

        public override bool ApplyFilter(Event @event)
        {
            return _eventFilter == null || !(@event is TEvent ev) || _eventFilter.Invoke(ev);
        }

        public DelegateInfo(Delegate handler = null) : base(handler, null)
        {
            _eventFilter = null;
        }

        public DelegateInfo(Delegate handler, Expression<Func<TEvent, bool>> filterExpression) 
            : base(handler, filterExpression?.ToString())
        {
            _eventFilter = filterExpression?.Compile();
        }

        public DelegateInfo(Delegate handler, Func<TEvent, bool> filter) 
            : base(handler, filter?.ToString())
        {
            _eventFilter = filter;
        }
    }
}
