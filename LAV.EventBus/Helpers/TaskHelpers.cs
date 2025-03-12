using System.Threading.Tasks;
using System;
using System.Threading;

namespace LAV.EventBus.Helpers
{
    public static class TaskHelpers
    {
#if !(NETCOREAPP2_1 || NETSTANDARD2_1 || NET5_0_OR_GREATER)
        public static Task<T> CreateCanceledTask<T>(CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetCanceled(); // Mark the task as canceled
            return tcs.Task;
        }

        public static Task CreateCanceledTask(CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<byte>();
            tcs.SetCanceled(); // Mark the task as canceled
            return tcs.Task;
        }

        public static ValueTask<T> CreateCanceledValueTask<T>(CancellationToken cancellationToken = default)
        {
            // Create a canceled Task and wrap it in a ValueTask
            var canceledTask = CreateCanceledTask<T>(cancellationToken);
            return new ValueTask<T>(canceledTask);
        }

        public static ValueTask CreateCanceledValueTask(CancellationToken cancellationToken = default)
        {
            // Create a canceled Task and wrap it in a ValueTask
            var canceledTask = CreateCanceledTask(cancellationToken);
            return new ValueTask(canceledTask);
        }
#endif
    }
}