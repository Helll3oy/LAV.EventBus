using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;

namespace LAV.EventBus.Helpers
{ 
    internal static class ParallelExtensions
    {
#if NET6_0_OR_GREATER
        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var reader = source.GetEnumerator();
            while (reader.MoveNext())
            {
                yield return reader.Current;
            }
        }

        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IReadOnlyList<T> source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < source.Count; i++)
            {
                yield return source[i];
            }
        }
#else
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
            await source.ForEachAsync(body, options.MaxDegreeOfParallelism, options.CancellationToken);
        }

        public static async Task ForEachAsync<T>(
            this IEnumerable<T> source,
            CancellationToken token,
            Func<T, CancellationToken, Task> body)
        {
            await source.ForEachAsync(body, Environment.ProcessorCount, token);
        }
#endif
    }

#if !(NETCOREAPP2_1 || NETSTANDARD2_1 || NET5_0_OR_GREATER)
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
}
