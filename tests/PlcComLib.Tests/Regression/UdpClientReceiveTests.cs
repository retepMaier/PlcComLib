using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using PlcComLib.Core.PlcTypes;
using PlcComLib.Udp;

namespace PlcComLib.Tests.Regression;

/// <summary>Bugs 1 and 2: the UDP client must be able to receive, and keep receiving after errors.</summary>
public class UdpClientReceiveTests
{
    private static UdpPlcClient BuildClient(int remotePort) => new UdpPlcClientBuilder()
        .SendTo("127.0.0.1", remotePort)
        .RegisterTelegram<ReadmeMachineStatus>()
            .WithMessageId(1, (ReadmeMachineStatus t) => t.TlgId)
        .Build();

    [Fact]
    public async Task Client_ReceivesDatagram_BeforeItHasSentAnything()
    {
        await using var client = BuildClient(TestNet.FreeUdpPort());
        var received = new TaskCompletionSource<ReadmeMachineStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Subscribe<ReadmeMachineStatus>(t => received.TrySetResult(t));
        await client.StartAsync();

        client.LocalEndpoint.Should().NotBeNull();
        client.LocalEndpoint!.Port.Should().BeGreaterThan(0);

        using var plc = new UdpClient(AddressFamily.InterNetwork);
        var payload = new ReadmeMachineStatus { TlgId = 1, MachineId = 42 }.Serialize();
        await plc.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, client.LocalEndpoint.Port));

        var telegram = await received.Task.WithTimeout();
        ((ushort)telegram.MachineId).Should().Be(42);
        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Client_BindsToConfiguredLocalPort()
    {
        int localPort = TestNet.FreeUdpPort();
        await using var client = new UdpPlcClientBuilder()
            .SendTo("127.0.0.1", TestNet.FreeUdpPort())
            .WithLocalPort(localPort)
            .Build();
        await client.StartAsync();

        client.LocalEndpoint!.Port.Should().Be(localPort);
    }

    [Fact]
    public async Task Client_KeepsReceiving_AfterSendingToAClosedPort()
    {
        // Nobody listens on the remote port: the OS answers with ICMP port unreachable,
        // which on Windows surfaces as ConnectionReset on the next receive.
        await using var client = BuildClient(TestNet.FreeUdpPort());
        int count = 0;
        client.Subscribe<ReadmeMachineStatus>(_ => Interlocked.Increment(ref count));
        await client.StartAsync();

        await client.SendAsync(new ReadmeMachineStatus());
        await client.SendAsync(new ReadmeMachineStatus());
        await Task.Delay(100);

        using var plc = new UdpClient(AddressFamily.InterNetwork);
        var payload = new ReadmeMachineStatus { TlgId = 1 }.Serialize();
        await plc.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, client.LocalEndpoint!.Port));

        await TestNet.WaitUntil(() => Volatile.Read(ref count) == 1);
        client.IsConnected.Should().BeTrue();
    }

    [Theory]
    [InlineData(SocketError.ConnectionReset, true)]
    [InlineData(SocketError.ConnectionRefused, true)]
    [InlineData(SocketError.MessageSize, true)]
    [InlineData(SocketError.AccessDenied, false)]
    public void TransientSocketErrors_AreRecognised(SocketError error, bool transient)
        => UdpSocket.IsTransient(new SocketException((int)error)).Should().Be(transient);

    [Fact]
    public async Task Client_ResolvesHostNames()
    {
        await using var client = new UdpPlcClientBuilder().SendTo("localhost", TestNet.FreeUdpPort()).Build();
        await client.StartAsync();
        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Server_RepliesAndReceives_RoundTrip()
    {
        int serverPort = TestNet.FreeUdpPort();
        await using var server = new UdpPlcServerBuilder()
            .ListenOn("127.0.0.1", serverPort)
            .RegisterTelegram<ReadmeMachineStatus>()
                .WithMessageId(1, (ReadmeMachineStatus t) => t.TlgId)
            .Build();
        server.Subscribe<ReadmeMachineStatus>(async t =>
        {
            await server.SendAsync(new ReadmeMachineStatus { MachineId = (ushort)(t.MachineId + 1) });
        });
        await server.StartAsync();

        await using var client = BuildClient(serverPort);
        var reply = new TaskCompletionSource<ReadmeMachineStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Subscribe<ReadmeMachineStatus>(t => reply.TrySetResult(t));
        await client.StartAsync();

        await client.SendAsync(new ReadmeMachineStatus { MachineId = 7 });

        ((ushort)(await reply.Task.WithTimeout()).MachineId).Should().Be(8);
    }
}
