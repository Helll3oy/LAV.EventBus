using System;
using System.Reflection;

namespace LAV.EventBus
{
    /// <summary>
    /// Represents a weak event handler that allows the target to be garbage collected.
    /// </summary>
    internal class WeakHandler : IDisposable
    {
        private readonly WeakReference _weakReference;
        private readonly MethodInfo _method;
        private readonly Type _delegateType;

        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="WeakHandler"/> class.
        /// </summary>
        /// <param name="handler">The delegate to be handled weakly.</param>
        public WeakHandler(Delegate handler)
        {
            if (handler.Target == null)
            {
                throw new ArgumentNullException(nameof(handler.Target), "Handler target cannot be null.");
            }

            _weakReference = new WeakReference(handler.Target);
            _method = handler.GetMethodInfo() ?? throw new ArgumentNullException(nameof(handler), "Handler method info cannot be null.");
            _delegateType = handler.GetType();
        }

        /// <summary>
        /// Gets a value indicating whether the target is still alive.
        /// </summary>
        public bool IsAlive => _weakReference.IsAlive;

        /// <summary>
        /// Creates a delegate of the specified type for the target method.
        /// </summary>
        /// <returns>A delegate if the target is alive; otherwise, null.</returns>
        public Delegate CreateDelegate()
        {
            var target = _weakReference.Target;
            return target != null ? _method.CreateDelegate(_delegateType, target) : null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // Free any other managed objects here.
            }

            // Free any unmanaged objects here.
            _disposed = true;
        }

        ~WeakHandler()
        {
            Dispose(false);
        }
    }
}
