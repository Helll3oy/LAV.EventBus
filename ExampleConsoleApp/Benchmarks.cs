using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using LAV.EventBus;

namespace ExampleConsoleApp
{
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    [SimpleJob(RunStrategy.ColdStart, launchCount: 50)]
    public class Benchmarks
    {
        private EventBus _EV;

        private List<int> _data;
        private const int DataSize = 100;

        [GlobalSetup]
        public void Setup()
        {
            _data = Enumerable.Range(0, DataSize).ToList();

            _EV = new EventBus();

            //_EV.Subscribe<OrderPlacedEvent>(async (orderPlacedEvent) =>
            //{
            //    await Task.Delay(1);
            //    //Console.WriteLine($"Order placed #1 [{orderPlacedEvent.OrderId}]");
            //});

            _EV.Subscribe<OrderPlacedEvent>(HandlerAsync);
        }

        [Benchmark]
        private async Task HandlerAsync(OrderPlacedEvent @event, CancellationToken token = default)
        {
            await Task.Delay(1);
        }

        //[Benchmark]
        //public void FastEventBus_Subscribe()
        //{
        //    foreach (var item in _data)
        //    {
        //        _fastEV.SubscribeAsync<OrderPlacedEvent>((orderPlacedEvent) =>
        //        {
        //            Console.WriteLine($"{item}. Order placed #1 [{orderPlacedEvent.OrderId}]");
        //        })
        //        .ConfigureAwait(false)
        //        .GetAwaiter()
        //        .GetResult();
        //    }
        //}
        //[Benchmark]
        //public void EventBus_Subscribe()
        //{
        //    foreach (var item in _data)
        //    {
        //        _EV.Subscribe<OrderPlacedEvent>((orderPlacedEvent) =>
        //        {
        //            Console.WriteLine($"{item}. Order placed #1 [{orderPlacedEvent.OrderId}]");
        //        });
        //    }
        //}

        [Benchmark]
        public void EventBus_Publish()
        {
            foreach (var item in _data)
            {
                _EV.Publish(new OrderPlacedEvent { OrderId = item });
            }
        }
    }
}
