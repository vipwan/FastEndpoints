﻿using FakeItEasy;
using FastEndpoints;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EventQueue;

public class RoundRobinEventQueueTests
{
    [Fact]
    public async Task multiple_subscribers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, LoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        var provider = services.BuildServiceProvider();
        var hub = new EventHub<RRTestEvent, InMemoryEventStorageRecord, InMemoryEventHubStorage>(provider);
        EventHub<RRTestEvent, InMemoryEventStorageRecord, InMemoryEventHubStorage>.Mode = HubMode.EventPublisher;

        var writerA = new TestServerStreamWriter<RRTestEvent>();
        var writerB = new TestServerStreamWriter<RRTestEvent>();

        var ctx = A.Fake<ServerCallContext>();
        A.CallTo(ctx).WithReturnType<CancellationToken>().Returns(default);

        _ = hub.OnSubscriberConnected(hub, Guid.NewGuid().ToString(), writerA, ctx);
        _ = hub.OnSubscriberConnected(hub, Guid.NewGuid().ToString(), writerB, ctx);

        var e1 = new RRTestEvent { EventID = 111 };
        await EventHubBase.AddToSubscriberQueues(e1, default);

        var e2 = new RRTestEvent { EventID = 222 };
        await EventHubBase.AddToSubscriberQueues(e2, default);

        var e3 = new RRTestEvent { EventID = 333 };
        await EventHubBase.AddToSubscriberQueues(e3, default);

        if (writerA.Responses.Count == 2)
        {
            writerB.Responses.Count.Should().Be(1);
            writerB.Responses[0].EventID.Should().Be(222);

            writerA.Responses[0].EventID.Should().Be(111);
            writerA.Responses[1].EventID.Should().Be(333);
        }

        if (writerB.Responses.Count == 2)
        {
            writerA.Responses.Count.Should().Be(1);
            writerA.Responses[0].EventID.Should().Be(222);

            writerB.Responses[0].EventID.Should().Be(111);
            writerB.Responses[1].EventID.Should().Be(333);
        }
    }

    [Fact]
    public async Task multiple_subscribers_but_one_goes_offline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, LoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        var provider = services.BuildServiceProvider();
        var hub = new EventHub<RRTestEvent, InMemoryEventStorageRecord, InMemoryEventHubStorage>(provider);
        EventHub<RRTestEvent, InMemoryEventStorageRecord, InMemoryEventHubStorage>.Mode = HubMode.EventPublisher;

        var writerA = new TestServerStreamWriter<RRTestEvent>();
        var writerB = new TestServerStreamWriter<RRTestEvent>();

        var ctxA = A.Fake<ServerCallContext>();
        A.CallTo(ctxA).WithReturnType<CancellationToken>().Returns(default);
        _ = hub.OnSubscriberConnected(hub, Guid.NewGuid().ToString(), writerA, ctxA);

        var ctxB = A.Fake<ServerCallContext>();
        var cts = new CancellationTokenSource();
        A.CallTo(ctxB).WithReturnType<CancellationToken>().Returns(cts.Token);
        _ = hub.OnSubscriberConnected(hub, Guid.NewGuid().ToString(), writerB, ctxB);

        var e1 = new RRTestEvent { EventID = 111 };
        await EventHubBase.AddToSubscriberQueues(e1, default);

        cts.Cancel();

        var e2 = new RRTestEvent { EventID = 222 };
        await EventHubBase.AddToSubscriberQueues(e2, default);

        if (writerA.Responses.Count == 2)
        {
            writerA.Responses[0].EventID.Should().Be(111);
            writerA.Responses[1].EventID.Should().Be(222);
            writerB.Responses.Count.Should().Be(0);
        }

        if (writerB.Responses.Count == 2)
        {
            writerB.Responses[0].EventID.Should().Be(111);
            writerB.Responses[1].EventID.Should().Be(222);
            writerA.Responses.Count.Should().Be(0);
        }
    }

    [Fact]
    public async Task only_one_subscriber()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, LoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        var provider = services.BuildServiceProvider();
        var hub = new EventHub<RRTestEvent, InMemoryEventStorageRecord, InMemoryEventHubStorage>(provider);
        EventHub<RRTestEvent, InMemoryEventStorageRecord, InMemoryEventHubStorage>.Mode = HubMode.EventPublisher;

        var writer = new TestServerStreamWriter<RRTestEvent>();

        var ctx = A.Fake<ServerCallContext>();
        A.CallTo(ctx).WithReturnType<CancellationToken>().Returns(default);

        _ = hub.OnSubscriberConnected(hub, Guid.NewGuid().ToString(), writer, ctx);

        var e1 = new RRTestEvent { EventID = 111 };
        await EventHubBase.AddToSubscriberQueues(e1, default);

        var e2 = new RRTestEvent { EventID = 222 };
        await EventHubBase.AddToSubscriberQueues(e2, default);

        var e3 = new RRTestEvent { EventID = 333 };
        await EventHubBase.AddToSubscriberQueues(e3, default);

        while (writer.Responses.Count < 1)
        {
            await Task.Delay(100);
        }

        writer.Responses.Count.Should().Be(3);
        writer.Responses[0].EventID.Should().Be(111);
        writer.Responses[1].EventID.Should().Be(222);
        writer.Responses[2].EventID.Should().Be(333);
    }

    private class RRTestEvent : IRoundRobinEvent
    {
        public int EventID { get; set; }
    }

    private class TestServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public List<T> Responses { get; } = new List<T>();

        public async Task WriteAsync(T message)
            => Responses.Add(message);
    }
}
