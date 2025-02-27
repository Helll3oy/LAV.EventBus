using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LAV.EventBus;

// var s = new WeakSubscriber();
// s._eventFilter = (o) => o.GetHashCode() > 0;
// s._eventFilterWeak = (o) => WeakFilter(o);
// Console.WriteLine(s.GetSourceCode("_eventFilter"));
// Console.WriteLine(s.GetSourceCode("_eventFilterWeak"));

// s._eventFilter.Compile()(new object());

// return;

// bool WeakFilter(object o)
// {
//     return o.ToString().Length > 0;
// }

// using var eventBus = new EventBus();

// // Error handling
// eventBus.OnError += (sender, args) =>
//     Console.WriteLine($"Error: {args.Exception.Message}");

// // Regular subscription
// eventBus.Subscribe<SampleEvent>(e =>
//     Console.WriteLine($"Received: {e.Message}, Severity: {e.Severity}"));

// // Weak reference subscription with filter
// var weakSubscriber = new WeakSubscriber();
// eventBus.Subscribe<SampleEvent>(
//     weakSubscriber.HandleEvent,
//     filter: e => e.Severity > 2,
//     useWeakReference: true);

// // Async subscription with priority
// eventBus.SubscribeAsync<SampleEvent>(
//     async e =>
//     {
//         await Task.Delay(100);
//         Console.WriteLine($"Async received: {e.Message}, Severity: {e.Severity}");
//     },
//     filter: e => e.Severity < 4,
//     priority: 1);

// // Start processing events
// var processingTask = eventBus.StartProcessingAsync();

// // Publish event
// eventBus.Publish(new SampleEvent { Message = "1. First publish", Severity = 4 });
// eventBus.Publish(new SampleEvent { Message = "2. Second publish", Severity = 5 });
// eventBus.Publish(new SampleEvent { Message = "3. Third publish", Severity = 6 });

// // Async publish
// await Task.WhenAll(
//     eventBus.PublishAsync(new SampleEvent { Message = "4. Async publish", Severity = 7 }),
//     eventBus.PublishAsync(new SampleEvent { Message = "5. Async publish", Severity = 8 })
// );

// // Wait for processing to complete
// await Task.Delay(1000); // Simulate work
// eventBus.Dispose(); // Stop processing
// await processingTask;

internal class Program
{
    private static async Task Main(string[] args)
    {
        long cnt = 0;
        DateTimeOffset start = DateTimeOffset.UtcNow;

        using (var fastEventBus = new FastEventBus())
        {

            fastEventBus.OnError += (sender, a) =>
                Console.WriteLine($"Error: {a.Exception.Message}");

            fastEventBus.OnLog += (sender, a) =>
                Console.WriteLine($"Log: {a.Message}");

            fastEventBus.OnSubscribe += (sender, a) =>
                Console.WriteLine($"Subscribe to [{a?.DelegateInfo?.EventType?.FullName}]" +
                    $"{(a?.DelegateInfo?.HasFilter ?? false ? $" with filter [{a?.DelegateInfo?.FilterSourceCode}]" : "")}.");

            _ = fastEventBus.SubscribeAsync<UserCreatedEvent>(async (userCreatedEvent, _) =>
            {
                Console.WriteLine($"User created: {userCreatedEvent.UserName}");
                //await Task.CompletedTask;
            });

            // Subscribe to OrderPlacedEvent
            _ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent) =>
            {
                Console.WriteLine($"{Interlocked.Increment(ref cnt)}Order placed #1: {orderPlacedEvent.OrderId}");
            });

            // Weak reference subscription with filter
            var weakSubscriber = new WeakSubscriber();
            _ = fastEventBus.SubscribeAsync<SampleEvent>(
                weakSubscriber.HandleEvent);

            // _ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent, _) =>
            // {
            //     Console.WriteLine($"Order placed #2: {orderPlacedEvent.OrderId}");
            //     return Task.CompletedTask;
            // });

            // _= fastEventBus.SubscribeAsync<SampleEvent>(
            //     async (e , _) =>
            //     {
            //         await Task.Delay(100);
            //         Console.WriteLine($"SampleEvent received #1: {e.Message}, Severity: {e.Severity}");
            //         await Task.CompletedTask;
            //     });

            // _= fastEventBus.SubscribeAsync<SampleEvent>(
            //     async (e , _) =>
            //     {
            //         await Task.Delay(10);
            //         Console.WriteLine($"SampleEvent received #2: {e.Message}, Severity: {e.Severity}");
            //         await Task.CompletedTask;
            //     },
            //     (e) => e.Severity < 3);

            // _= fastEventBus.SubscribeAsync<SampleEvent>(
            //     (e) =>
            //     {
            //         Console.WriteLine($"SampleEvent received #3: {e.Message}, Severity: {e.Severity}");
            //     },
            //     (e) => e.Severity > 2);

            _ = fastEventBus.SubscribeAsync<SampleEvent>(SampleEventHandle, e => e.Severity == 1);
            // _= fastEventBus.SubscribeAsync<SampleEvent>(SampleEventAsyncHandle, e => e.Severity == 2);

            _ = fastEventBus.SubscribeAsync<SampleEvent>(WeakSubscriber.StaticHandleEvent, e => e.Severity == 1);

            // async Task SampleEventAsyncHandle(SampleEvent e, CancellationToken cancellationToken = default)
            // {
            //     Console.WriteLine($"SampleEvent received #5: {e.Message}, Severity: {e.Severity}");
            //     await Task.CompletedTask;
            // }

            void SampleEventHandle(SampleEvent e)
            {
                Console.WriteLine($"SampleEvent received #4: {e.Message}, Severity: {e.Severity}");
            }

            // Start processing events
            var processingTask = fastEventBus.StartProcessingAsync();

            // Give some time for the events to be processed
            await Task.Delay(100);

            // Publish some events
            await fastEventBus.PublishAsync(new UserCreatedEvent { UserName = "JohnDoe" });
            // await fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 123 });
            await fastEventBus.PublishAsync(new SampleEvent { Message = "1. First publish", Severity = 1 });

            // await Task.Delay(100);

            // await fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 456 });
            // await fastEventBus.PublishAsync(new UserCreatedEvent { UserName = "Mr. Smith" });

            await Task.Delay(100);

            await fastEventBus.PublishAsync(new SampleEvent { Message = "2. Second publish", Severity = 2 });

            await Task.Delay(100);

            weakSubscriber.Dispose();

            weakSubscriber = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            await Task.Delay(1000);

            await fastEventBus.PublishAsync(new SampleEvent { Message = "3. Third publish", Severity = 3 });

            await Task.Delay(100);

            // _ = fastEventBus.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent, _) =>
            // {
            //     Console.WriteLine($"Order placed #3: {orderPlacedEvent.OrderId}");
            //     return Task.CompletedTask;
            // });

            for (int i = 0; i < 1000; i++)
            {
                fastEventBus.PublishAsync(new OrderPlacedEvent { OrderId = 789 });
            }

            // Give some time for the events to be processed
            await Task.Delay(1000);

            // Stop processing
            fastEventBus.Dispose();
            // await fastEventBus.UnsubscribeAsync<SampleEvent>();
            // await fastEventBus.UnsubscribeAsync<OrderPlacedEvent>();
            // await fastEventBus.UnsubscribeAsync<UserCreatedEvent>();

            await processingTask;
        }

        Console.WriteLine($"Done [{(DateTimeOffset.UtcNow - start).TotalMilliseconds - 2400} мс]!");
        Console.ReadLine();
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
}

public class OrderPlacedEvent : Event
{
    public int OrderId { get; set; }
}