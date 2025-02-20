using LAV.EventBus;

using var eventBus = new EventBus();

// Error handling
eventBus.OnError += (sender, args) =>
    Console.WriteLine($"Error: {args.Exception.Message}");

// Regular subscription
eventBus.Subscribe<SampleEvent>(e =>
    Console.WriteLine($"Received: {e.Message}, Severity: {e.Severity}"));

// Weak reference subscription with filter
var weakSubscriber = new WeakSubscriber();
eventBus.Subscribe<SampleEvent>(
    weakSubscriber.HandleEvent,
    filter: e => e.Severity > 2,
    useWeakReference: true);

// Async subscription with priority
eventBus.SubscribeAsync<SampleEvent>(
    async e =>
    {
        await Task.Delay(100);
        Console.WriteLine($"Async received: {e.Message}, Severity: {e.Severity}");
    },
    filter: e => e.Severity < 4,
    priority: 1);

// Publish event
eventBus.Publish(new SampleEvent { Message = "Publish event", Severity = 4 });

// Async publish
await Task.WhenAll(
    eventBus.PublishAsync(new SampleEvent { Message = "Async publish", Severity = 5 }),
    eventBus.PublishAsync(new SampleEvent { Message = "Async publish", Severity = 6 })
);

public class SampleEvent
{
    public required string Message { get; set; }
    public int Severity { get; set; }
}

class WeakSubscriber
{
    public void HandleEvent(SampleEvent e)
    {
        Console.WriteLine($"Weak handler received: {e.Message}, Severity: {e.Severity}");
    }
}