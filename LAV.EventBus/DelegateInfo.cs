using System;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;

namespace LAV.EventBus
{
    /// <summary>
    /// Represents an abstract base class for delegate information.
    /// </summary>
    public class DelegateInfo
    {
        private static readonly Lazy<DelegateInfo> _default =
            new Lazy<DelegateInfo>(() => new DelegateInfo());
        public static DelegateInfo Default => _default.Value;

        public virtual string FilterSourceCode { get; }
        public virtual bool HasFilter { get; }

        public virtual Type EventType { get; } = typeof(Event);

        /// <summary>
        /// Filters the specified event.
        /// </summary>
        /// <param name="event">The event to filter.</param>
        /// <returns>True if the event passes the filter; otherwise, false.</returns>
        public virtual bool ApplyFilter(Event @event) => true;

        public DelegateInfo(string filterSourceCode = null)
        {
            FilterSourceCode = filterSourceCode?.Trim() ?? string.Empty;
            HasFilter = !string.IsNullOrEmpty(FilterSourceCode);
        }
    }

    public sealed class DelegateInfo<TEvent> : DelegateInfo where TEvent : Event
    {
        private static readonly Lazy<DelegateInfo<TEvent>> _default =
            new Lazy<DelegateInfo<TEvent>>(() => new DelegateInfo<TEvent>());
        new public static DelegateInfo<TEvent> Default => _default.Value;

        private Func<TEvent, bool> _eventFilter;

        public override Type EventType { get; } = typeof(TEvent);

        public override bool ApplyFilter(Event @event)
        {
            return _eventFilter == null || !(@event is TEvent ev) || _eventFilter.Invoke(ev);
        }

        public DelegateInfo() : base(null)
        {
            _eventFilter = null;
        }

        public DelegateInfo(Expression<Func<TEvent, bool>> filterExpression) : base(filterExpression?.ToString())
        {
            _eventFilter = filterExpression?.Compile();
        }

        public DelegateInfo(Func<TEvent, bool> filter) : base(filter?.ToString())
        {
            _eventFilter = filter;
        }
    }
}
