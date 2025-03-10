using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using static Microsoft.IO.RecyclableMemoryStreamManager;
using System.Diagnostics;

namespace LAV.EventBus.Compatibility
{
#if !NET6_0
    internal static class ParallelExtensions
    {
        /// <summary>
        /// Выполняет асинхронную обработку элементов с ограничением параллелизма.
        /// </summary>
        /// <typeparam name="T">Тип элементов коллекции.</typeparam>
        /// <param name="source">Исходная коллекция элементов.</param>
        /// <param name="body">Асинхронный обработчик элемента.</param>
        /// <param name="maxDegreeOfParallelism">Максимальное количество параллельных задач.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static async Task ForEachAsync<T>(
            this IEnumerable<T> source,
            Func<T, CancellationToken, Task> body,
            int maxDegreeOfParallelism,
            CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (maxDegreeOfParallelism <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
            var tasks = new List<Task>();

            foreach (var item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Ожидаем слот в семафоре
                await semaphore.WaitAsync(cancellationToken);

                // Запускаем задачу
                //var task = Task.Run(async () =>
                //{
                //    try
                //    {
                //        await body(item, cancellationToken);
                //    }
                //    finally
                //    {
                //        semaphore.Release();
                //    }
                //}, cancellationToken);
                var task = body(item, cancellationToken)
                    .ContinueWith(t => semaphore.Release(), cancellationToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
        }

        public static async Task ForEachAsync<T>(
            this IEnumerable<T> source,
            ParallelOptions options,
            Func<T, CancellationToken, Task> body)
        {
            await ForEachAsync(source, body, options.MaxDegreeOfParallelism, options.CancellationToken);
        }

        public static async Task ForEachAsync<T>(
            this IEnumerable<T> source,
            CancellationToken token,
            Func<T, CancellationToken, Task> body)
        {
            await ForEachAsync(source, body, Environment.ProcessorCount, token);
        }

        //public static async Task ForEachAsync<T>(
        //    IEnumerable<T> source,
        //    CancellationToken token,
        //    Func<T, CancellationToken, Task> body)
        //{
        //    await ForEachAsync(source, new ParallelOptions
        //    {
        //        CancellationToken = token,
        //    },
        //    body);
        //}
        //public static async Task ForEachAsync<T>(
        //    IEnumerable<T> source,
        //    ParallelOptions options,
        //    Func<T, CancellationToken, Task> body)
        //{
        //    // Use a semaphore to limit the degree of parallelism
        //    var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism);

        //    // Create a list of tasks to track all running tasks
        //    var tasks = new List<Task>();

        //    foreach (var item in source)
        //    {
        //        // Wait for the semaphore to allow a new task to start
        //        await semaphore.WaitAsync(options.CancellationToken);

        //        //// Start a new task for the current item
        //        //var task = Task.Run(async () =>
        //        //{
        //        //    try
        //        //    {
        //        //        await body(item, options.CancellationToken);
        //        //    }
        //        //    finally
        //        //    {
        //        //        // Release the semaphore when the task is done
        //        //        semaphore.Release();
        //        //    }
        //        //});

        //        var task = body(item, options.CancellationToken)
        //            .ContinueWith(t => semaphore.Release(), options.CancellationToken);

        //        tasks.Add(task);
        //    }

        //    // Wait for all tasks to complete
        //    await Task.WhenAll(tasks);
        //}
    }
#endif

#if !(NETCOREAPP2_1 || NETSTANDARD2_1)
    public static class ArrayPool<T>
    {
        private static readonly ConcurrentBag<T[]> _pool = new ConcurrentBag<T[]>();

        public static class Shared
        {
            public static T[] Rent(int minimumLength)
            {
                if (_pool.TryTake(out var array) && array.Length >= minimumLength)
                {
                    return array;
                }
                return new T[minimumLength];
            }

            public static void Return(T[] array, bool clearArray = false)
            {
                if (clearArray)
                {
                    Array.Clear(array, 0, array.Length);
                }
                _pool.Add(array);
            }
        }
    }
#endif

    internal class CompatibilityHelpers
    {
    }
}
