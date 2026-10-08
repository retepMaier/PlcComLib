using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using PlcComLib.Core.Events;
using PlcComLib.Tcp;

namespace PlcComLib.Tests.Regression;

/// <summary>
/// Bugs 4, 5, 6 and 8 over a real loopback TCP connection, built without a logger
/// (which used to throw NullReferenceException from the generated log methods).
/// </summary>
public class TcpEndToEndTests
{
    private static TcpPlcServer BuildServer(int port, long id) => new TcpPlcServerBuilder()
        .ListenOn("127.0.0.1", port)
        .RegisterTelegram<ReadmeMachineStatus>()
            .WithMessageId(id, (ReadmeMachineStatus t) => t.TlgId)
            .WithLength(ReadmeMachineStatus.WireSize, (ReadmeMachineStatus t) => t.TlgLength)
        .Build();

    private static TcpPlcClient BuildClient(int port, long id) => new TcpPlcClientBuilder()
        .ConnectTo("127.0.0.1", port)
        .WithReconnectInterval(TimeSpan.FromMilliseconds(50))
        .RegisterTelegram<ReadmeMachineStatus>()
            .WithMessageId(id, (ReadmeMachineStatus t) => t.TlgId)
            .WithLength(ReadmeMachineStatus.WireSize, (ReadmeMachineStatus t) => t.TlgLength)
        .Build();

    [Fact]
    public async Task ReadmeTelegram_RoundTrips_WithHeaderStampedOnSend()
    {
        int port = TestNet.FreeTcpPort();
        await using var server = BuildServer(port, 1);
        var typed   = new TaskCompletionSource<ReadmeMachineStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var untyped = new TaskCompletionSource<TelegramReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Subscribe<ReadmeMachineStatus>(t => typed.TrySetResult(t));
        server.TelegramReceived += (_, e) => untyped.TrySetResult(e);
        await server.StartAsync();

        await using var client = BuildClient(port, 1);
        await client.StartAsync();
        await TestNet.WaitUntil(() => client.IsConnected);

        // TlgId/TlgLength left at 0: SendAsync fills them in from the builder configuration.
        await client.SendAsync(new ReadmeMachineStatus
        {
            MachineId = 42, CurrentSpeed = 1500, IsRunning = true, Temperature = 78.5f,
        });

        var t = await typed.Task.WithTimeout();
        ((short)t.TlgId).Should().Be(1);
        ((short)t.TlgLength).Should().Be(14);
        ((ushort)t.MachineId).Should().Be(42);
        ((short)t.CurrentSpeed).Should().Be(1500);
        ((bool)t.IsRunning).Should().BeTrue();
        ((float)t.Temperature).Should().Be(78.5f);

        // The untyped event decodes the same PLC layout (bool bit, pad byte, REAL at offset 10).
        var e = await untyped.Task.WithTimeout();
        e.Telegram.GetValue<bool>("IsRunning").Should().BeTrue();
        e.Telegram.GetValue<float>("Temperature").Should().Be(78.5f);
    }

    [Fact]
    public async Task SameTelegramType_WithDifferentIdsOnTwoConnections_EachMatchesItsOwnId()
    {
        int port1 = TestNet.FreeTcpPort(), port2 = TestNet.FreeTcpPort();
        await using var server1 = BuildServer(port1, 1);
        await using var server2 = BuildServer(port2, 2);
        var got1 = new TaskCompletionSource<ReadmeMachineStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var got2 = new TaskCompletionSource<ReadmeMachineStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        server1.Subscribe<ReadmeMachineStatus>(t => got1.TrySetResult(t));
        server2.Subscribe<ReadmeMachineStatus>(t => got2.TrySetResult(t));
        await server1.StartAsync();
        await server2.StartAsync();

        await using var client1 = BuildClient(port1, 1);
        await using var client2 = BuildClient(port2, 2);
        await client1.StartAsync();
        await client2.StartAsync();
        await TestNet.WaitUntil(() => client1.IsConnected && client2.IsConnected);

        await client1.SendAsync(new ReadmeMachineStatus { MachineId = 11 });
        await client2.SendAsync(new ReadmeMachineStatus { MachineId = 22 });

        ((ushort)(await got1.Task.WithTimeout()).MachineId).Should().Be(11);
        ((ushort)(await got2.Task.WithTimeout()).MachineId).Should().Be(22);
    }

    [Fact]
    public async Task TypedSubscriber_StillFires_WhenTelegramReceivedHandlerThrows()
    {
        int port = TestNet.FreeTcpPort();
        await using var server = BuildServer(port, 1);
        int typedCount = 0;
        server.TelegramReceived += (_, _) => throw new InvalidOperationException("handler bug");
        server.Subscribe<ReadmeMachineStatus>(_ => Interlocked.Increment(ref typedCount));
        await server.StartAsync();

        await using var client = BuildClient(port, 1);
        await client.StartAsync();
        await TestNet.WaitUntil(() => client.IsConnected);

        await client.SendAsync(new ReadmeMachineStatus());
        await client.SendAsync(new ReadmeMachineStatus());

        // The throwing handler neither drops the connection nor stops later telegrams.
        await TestNet.WaitUntil(() => Volatile.Read(ref typedCount) == 2);
    }

    [Fact]
    public async Task Client_KeepsReconnecting_WhenConnectionStateHandlerThrows()
    {
        int port = TestNet.FreeTcpPort();
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        int accepted = 0;
        var acceptLoop = Task.Run(async () =>
        {
            // Accept and immediately drop every connection.
            while (true)
            {
                TcpClient peer;
                try { peer = await listener.AcceptTcpClientAsync(); }
                catch { return; }
                Interlocked.Increment(ref accepted);
                peer.Dispose();
            }
        });

        await using var client = BuildClient(port, 1);
        client.ConnectionStateChanged += (_, _) => throw new InvalidOperationException("handler bug");
        await client.StartAsync();

        try
        {
            await TestNet.WaitUntil(() => Volatile.Read(ref accepted) >= 3);
        }
        finally
        {
            listener.Stop();
            await acceptLoop;
        }
    }

    [Fact]
    public async Task UnknownTelegramHandlerThrowing_DoesNotDropConnection()
    {
        int port = TestNet.FreeTcpPort();
        await using var server = BuildServer(port, 1);
        int unknown = 0, known = 0;
        server.UnknownTelegramReceived += (_, _) => { Interlocked.Increment(ref unknown); throw new InvalidOperationException(); };
        server.Subscribe<ReadmeMachineStatus>(_ => Interlocked.Increment(ref known));
        await server.StartAsync();

        await using var client = BuildClient(port, 1);
        await client.StartAsync();
        await TestNet.WaitUntil(() => client.IsConnected);

        // Correct id but wrong length field → reported as unknown.
        var bad = new ReadmeMachineStatus { TlgId = 1, TlgLength = 99 }.Serialize();
        using (var raw = new TcpClient())
        {
            await raw.ConnectAsync(IPAddress.Loopback, port);
            await raw.GetStream().WriteAsync(bad);
            await TestNet.WaitUntil(() => Volatile.Read(ref unknown) == 1);
        }

        await client.SendAsync(new ReadmeMachineStatus());
        await TestNet.WaitUntil(() => Volatile.Read(ref known) == 1);
    }
}
