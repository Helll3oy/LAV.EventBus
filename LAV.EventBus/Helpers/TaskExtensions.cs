using System.Threading.Tasks;
using System;
using System.Threading;

namespace LAV.EventBus.Helpers
{
    public static class TaskExtensions
    {
#if !(NET6_0_OR_GREATER || NETSTANDARD2_1)
        public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            // Create a Task that completes after the specified timeout
            var timeoutTask = Task.Delay(timeout, cancellationToken);

            // Use Task.WhenAny to wait for either the original task or the timeout task
            var completedTask = await Task.WhenAny(task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("The operation has timed out.");
            }

            // Ensure the original task is completed (in case it threw an exception)
            await task;
        }
#endif
    }
}