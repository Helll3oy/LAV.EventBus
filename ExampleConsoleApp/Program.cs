using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;
using ExampleConsoleApp;
using LAV.EventBus;

internal class Program
{
    private static async Task Main(string[] args)
    {
        //var summary = BenchmarkRunner.Run<Benchmarks>();
        int cnt = 1000;
        DateTimeOffset start = DateTimeOffset.UtcNow;

        //var res1 = await FastEventBusExamples(cnt);
        //var dur1 = DateTimeOffset.UtcNow - start;

        start = DateTimeOffset.UtcNow;
        var res2 = await EventBusExamples(cnt);
        var dur2 = DateTimeOffset.UtcNow - start;

        Console.WriteLine("=========================================================================");
        //Console.WriteLine($"Result 1: {res1} [{dur1.TotalMilliseconds} мс]!");
        Console.WriteLine($"Result 2: {res2} [{dur2.TotalMilliseconds} мс]!");

        Console.ReadLine();
    }

    private static async Task<long> EventBusExamples(int cnt)
    {
        long orderCnt = 0;
        long userCnt = 0;

        using (var eventBus = new EventBus())
        {
            eventBus.Subscribe<OrderPlacedEvent>(async (orderPlacedEvent, ct) =>
            {
                long i = Interlocked.Increment(ref orderCnt);

                //Console.WriteLine($"2. Order placed 1 [{orderPlacedEvent.OrderId}]");

                //await Task.Delay(1, ct);
            },
            options: new HandlerOptions
            {
                Mode = ExecutionMode.Parallel
            },
            priority: EventHandlerPriority.Medium);

            //eventBus.Subscribe<UserCreatedEvent>(async (userCreatedEvent, ct) =>
            //{
            //    long i = Interlocked.Increment(ref userCnt);
            //    Console.WriteLine($"UserCreatedEvent [{userCreatedEvent.UserName}]");
            //    await Task.Delay(0, ct);
            //},
            //options: new HandlerOptions
            //{
            //    Mode = ExecutionMode.Parallel
            //},
            //priority: EventHandlerPriority.VeryLow);

            //eventBus.Subscribe<OrderPlacedEvent>(async (orderPlacedEvent, ct) =>
            //{
            //    long i = Interlocked.Increment(ref orderCnt);
            //    Console.WriteLine($"Order placed 2 [{orderPlacedEvent.OrderId}]");
            //    await Task.Delay(0, ct);
            //},
            //options: new HandlerOptions
            //{
            //    Mode = ExecutionMode.Parallel
            //},
            //priority: EventHandlerPriority.VeryHigh);


            ////List<Task> tasks = new List<Task>();
            //for (int i = 1; i <= cnt; i++)
            //{
            //    //tasks.Add(eventBus.PublishAsync(new OrderPlacedEvent { OrderId = i }));

            //    eventBus.Publish(new OrderPlacedEvent { OrderId = i });
            //}

            ////Task.WaitAll(tasks);
            ////await Task.Delay(5000);

            // Publish events
            Parallel.For(0, cnt, async i =>
            {
                await eventBus.PublishAsync(new OrderPlacedEvent { OrderId = i });
                //await eventBus.PublishAsync(new UserCreatedEvent { UserName = $"UserName #{i}" });
            });

            await eventBus.WaitAllEventsCompleted();
          
            Console.WriteLine("All events processed.");

            //await eventBus.PublishAsync(new OrderPlacedEvent { OrderId = 66666666 });
            //await eventBus.WaitAllEventsCompleted();

            //Console.WriteLine($"OrderCnt: {Interlocked.Read(ref orderCnt)}");
            //Console.WriteLine($"UserCnt: {Interlocked.Read(ref userCnt)}");

            return Interlocked.Read(ref orderCnt);

            ////eventBus.OnError += (sender, a) =>
            ////    Console.WriteLine($"Error: {a.Exception.Message}");

            ////eventBus.OnLog += (sender, a) =>
            ////    Console.WriteLine($"Log: {a.Message}");

            ////eventBus.OnSubscribe += (sender, a) =>
            ////    Console.WriteLine($"Subscribe to [{a?.DelegateInfo?.EventType?.FullName}]" +
            ////        $"{(a?.DelegateInfo?.HasFilter ?? false ? $" with filter [{a?.DelegateInfo?.FilterSourceCode}]" : "")}.");

            //eventBus.Subscribe<UserCreatedEvent>(async userCreatedEvent =>
            //{
            //    Console.WriteLine($"User created: {userCreatedEvent.UserName}");
            //    //await Task.CompletedTask;
            //});

            //// Subscribe to OrderPlacedEvent
            //eventBus.Subscribe<OrderPlacedEvent>((orderPlacedEvent) =>
            //{
            //    Console.WriteLine($"{Interlocked.Increment(ref cnt)}. Order placed #1 [{orderPlacedEvent.OrderId}]");
            //});

            //// Weak reference subscription with filter
            //var weakSubscriber = new WeakSubscriber();
            //eventBus.Subscribe<SampleEvent>(weakSubscriber.HandleEvent);

            //// _ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent, _) =>
            //// {
            ////     Console.WriteLine($"Order placed #2: {orderPlacedEvent.OrderId}");
            ////     return Task.CompletedTask;
            //// });

            //// _= fastEventBus.SubscribeAsync<SampleEvent>(
            ////     async (e , _) =>
            ////     {
            ////         await Task.Delay(100);
            ////         Console.WriteLine($"SampleEvent received #1: {e.Message}, Severity: {e.Severity}");
            ////         await Task.CompletedTask;
            ////     });

            //// _= fastEventBus.SubscribeAsync<SampleEvent>(
            ////     async (e , _) =>
            ////     {
            ////         await Task.Delay(10);
            ////         Console.WriteLine($"SampleEvent received #2: {e.Message}, Severity: {e.Severity}");
            ////         await Task.CompletedTask;
            ////     },
            ////     (e) => e.Severity < 3);

            //// _= fastEventBus.SubscribeAsync<SampleEvent>(
            ////     (e) =>
            ////     {
            ////         Console.WriteLine($"SampleEvent received #3: {e.Message}, Severity: {e.Severity}");
            ////     },
            ////     (e) => e.Severity > 2);

            //eventBus.Subscribe<SampleEvent>(SampleEventHandle);//, e => e.Severity == 1);
            //// _= fastEventBus.SubscribeAsync<SampleEvent>(SampleEventAsyncHandle, e => e.Severity == 2);

            //eventBus.Subscribe<SampleEvent>(WeakSubscriber.StaticHandleEvent);//, e => e.Severity == 1);

            //// async Task SampleEventAsyncHandle(SampleEvent e, CancellationToken cancellationToken = default)
            //// {
            ////     Console.WriteLine($"SampleEvent received #5: {e.Message}, Severity: {e.Severity}");
            ////     await Task.CompletedTask;
            //// }

            //void SampleEventHandle(SampleEvent e)
            //{
            //    Console.WriteLine($"SampleEvent received #4: {e.Message}, Severity: {e.Severity}");
            //}

            //// Start processing events
            ////var processingTask = fastEventBus.StartProcessingAsync();

            //// Give some time for the events to be processed
            //await Task.Delay(100);

            //// Publish some events
            //await eventBus.PublishAsync(new UserCreatedEvent { UserName = "JohnDoe" });
            //// await fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 123 });
            //await eventBus.PublishAsync(new SampleEvent { Message = "1. First publish", Severity = 1 });

            //// await Task.Delay(100);

            //// await fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 456 });
            //// await fastEventBus.PublishAsync(new UserCreatedEvent { UserName = "Mr. Smith" });

            //await Task.Delay(100);

            //await eventBus.PublishAsync(new SampleEvent { Message = "2. Second publish", Severity = 2 });

            //await Task.Delay(100);

            //weakSubscriber.Dispose();

            //weakSubscriber = null;
            //GC.Collect();
            //GC.WaitForPendingFinalizers();

            //await Task.Delay(1000);

            //await eventBus.PublishAsync(new SampleEvent { Message = "3. Third publish", Severity = 3 });

            //await Task.Delay(100);

            //// _ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent, _) =>
            //// {
            ////     Console.WriteLine($"Order placed #3: {orderPlacedEvent.OrderId}");
            ////     return Task.CompletedTask;
            //// });

            //for (int i = 0; i < 1000; i++)
            //{
            //    eventBus.PublishAsync(new OrderPlacedEvent { OrderId = 789 });
            //}

            //// Give some time for the events to be processed
            //await Task.Delay(1000);

            //// Stop processing
            ////fastEventBus.Dispose();
            //// await fastEventBus.UnsubscribeAsync<SampleEvent>();
            //// await fastEventBus.UnsubscribeAsync<OrderPlacedEvent>();
            //// await fastEventBus.UnsubscribeAsync<UserCreatedEvent>();

            ////await processingTask;
        }
    }

    private static async Task<long> FastEventBusExamples(long cnt)
    {
        return 0;
        //long test1 = 0;
        //using (var fastEventBus = new FastEventBus())
        //{
        //    Task.WaitAll(fastEventBus.SubscribeAsync<OrderPlacedEvent>(async (orderPlacedEvent) =>
        //    {
        //        Interlocked.Increment(ref test1);

        //        //Console.WriteLine($"1. Order placed 1 [{orderPlacedEvent.OrderId}]");

        //        await Task.Delay(10);
        //    }));

        //    var processingTask = fastEventBus.StartProcessingAsync();

        //    List<Task> tasks = new List<Task>();
        //    for (int i = 0; i < cnt; i++)
        //    {
        //        tasks.Add(fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = i }));
        //    }

        //    await Task.WhenAll(tasks);

        //    fastEventBus.Dispose();

        //    await processingTask;

        //    await Task.Delay(100);

        //    return Interlocked.Read(ref test1);

        //    ////fastEventBus.OnError += (sender, a) =>
        //    ////    Console.WriteLine($"Error: {a.Exception.Message}");

        //    ////fastEventBus.OnLog += (sender, a) =>
        //    ////    Console.WriteLine($"Log: {a.Message}");

        //    ////fastEventBus.OnSubscribe += (sender, a) =>
        //    ////    Console.WriteLine($"Subscribe to [{a?.DelegateInfo?.EventType?.FullName}]" +
        //    ////        $"{(a?.DelegateInfo?.HasFilter ?? false ? $" with filter [{a?.DelegateInfo?.FilterSourceCode}]" : "")}.");

        //    //_ = fastEventBus.SubscribeAsync<UserCreatedEvent>(async (userCreatedEvent, _) =>
        //    //{
        //    //    Console.WriteLine($"User created: {userCreatedEvent.UserName}");
        //    //    //await Task.CompletedTask;
        //    //});

        //    //// Subscribe to OrderPlacedEvent
        //    //_ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent) =>
        //    //{
        //    //    Console.WriteLine($"{Interlocked.Increment(ref cnt)}. Order placed #1 [{orderPlacedEvent.OrderId}]");
        //    //});

        //    //// Weak reference subscription with filter
        //    //var weakSubscriber = new WeakSubscriber();
        //    //_ = fastEventBus.SubscribeAsync<SampleEvent>(
        //    //    weakSubscriber.HandleEvent);

        //    //// _ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent, _) =>
        //    //// {
        //    ////     Console.WriteLine($"Order placed #2: {orderPlacedEvent.OrderId}");
        //    ////     return Task.CompletedTask;
        //    //// });

        //    //// _= fastEventBus.SubscribeAsync<SampleEvent>(
        //    ////     async (e , _) =>
        //    ////     {
        //    ////         await Task.Delay(100);
        //    ////         Console.WriteLine($"SampleEvent received #1: {e.Message}, Severity: {e.Severity}");
        //    ////         await Task.CompletedTask;
        //    ////     });

        //    //// _= fastEventBus.SubscribeAsync<SampleEvent>(
        //    ////     async (e , _) =>
        //    ////     {
        //    ////         await Task.Delay(10);
        //    ////         Console.WriteLine($"SampleEvent received #2: {e.Message}, Severity: {e.Severity}");
        //    ////         await Task.CompletedTask;
        //    ////     },
        //    ////     (e) => e.Severity < 3);

        //    //// _= fastEventBus.SubscribeAsync<SampleEvent>(
        //    ////     (e) =>
        //    ////     {
        //    ////         Console.WriteLine($"SampleEvent received #3: {e.Message}, Severity: {e.Severity}");
        //    ////     },
        //    ////     (e) => e.Severity > 2);

        //    //_ = fastEventBus.SubscribeAsync<SampleEvent>(SampleEventHandle, e => e.Severity == 1);
        //    //// _= fastEventBus.SubscribeAsync<SampleEvent>(SampleEventAsyncHandle, e => e.Severity == 2);

        //    //_ = fastEventBus.SubscribeAsync<SampleEvent>(WeakSubscriber.StaticHandleEvent, e => e.Severity == 1);

        //    //// async Task SampleEventAsyncHandle(SampleEvent e, CancellationToken cancellationToken = default)
        //    //// {
        //    ////     Console.WriteLine($"SampleEvent received #5: {e.Message}, Severity: {e.Severity}");
        //    ////     await Task.CompletedTask;
        //    //// }

        //    //void SampleEventHandle(SampleEvent e)
        //    //{
        //    //    Console.WriteLine($"SampleEvent received #4: {e.Message}, Severity: {e.Severity}");
        //    //}

        //    //// Start processing events
        //    //processingTask = fastEventBus.StartProcessingAsync();

        //    //// Give some time for the events to be processed
        //    //await Task.Delay(100);

        //    //// Publish some events
        //    //await fastEventBus.PublishAsync(new UserCreatedEvent { UserName = "JohnDoe" });
        //    //// await fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 123 });
        //    //await fastEventBus.PublishAsync(new SampleEvent { Message = "1. First publish", Severity = 1 });

        //    //// await Task.Delay(100);

        //    //// await fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 456 });
        //    //// await fastEventBus.PublishAsync(new UserCreatedEvent { UserName = "Mr. Smith" });

        //    //await Task.Delay(100);

        //    //await fastEventBus.PublishAsync(new SampleEvent { Message = "2. Second publish", Severity = 2 });

        //    //await Task.Delay(100);

        //    //weakSubscriber.Dispose();

        //    //weakSubscriber = null;
        //    //GC.Collect();
        //    //GC.WaitForPendingFinalizers();

        //    //await Task.Delay(1000);

        //    //await fastEventBus.PublishAsync(new SampleEvent { Message = "3. Third publish", Severity = 3 });

        //    //await Task.Delay(100);

        //    //// _ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent, _) =>
        //    //// {
        //    ////     Console.WriteLine($"Order placed #3: {orderPlacedEvent.OrderId}");
        //    ////     return Task.CompletedTask;
        //    //// });

        //    //for (int i = 0; i < 1000; i++)
        //    //{
        //    //    fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 789 });
        //    //}

        //    //// Give some time for the events to be processed
        //    //await Task.Delay(1000);

        //    //// Stop processing
        //    //fastEventBus.Dispose();
        //    //// await fastEventBus.UnsubscribeAsync<SampleEvent>();
        //    //// await fastEventBus.UnsubscribeAsync<OrderPlacedEvent>();
        //    //// await fastEventBus.UnsubscribeAsync<UserCreatedEvent>();

        //    //await processingTask;
        //}
    }
}

public class SampleEvent : Event
{
    public string Message { get; set; }
    public int Severity { get; set; }
}

class WeakSubscriber : IDisposable
{
    private bool disposedValue;

    public static void StaticHandleEvent(SampleEvent e)
    {
        Console.WriteLine($"Weak static handler received #2: {e.Message}, Severity: {e.Severity}");
    }

    public void HandleEvent(SampleEvent e)
    {
        Console.WriteLine($"Weak handler received #1 (disposedValue[{disposedValue}]): {e.Message}, Severity: {e.Severity}");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: освободить управляемое состояние (управляемые объекты)
            }

            // TODO: освободить неуправляемые ресурсы (неуправляемые объекты) и переопределить метод завершения
            // TODO: установить значение NULL для больших полей
            disposedValue = true;
        }
    }

    // TODO: переопределить метод завершения, только если "Dispose(bool disposing)" содержит код для освобождения неуправляемых ресурсов
    ~WeakSubscriber()
    {
        // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class UserCreatedEvent : Event
{
    public string UserName { get; set; }
    public override string ToString() => $"UserName: {UserName}";
}

public class OrderPlacedEvent : Event
{
    public int OrderId { get; set; }

    public override string ToString() => $"OrderId: {OrderId}";
}