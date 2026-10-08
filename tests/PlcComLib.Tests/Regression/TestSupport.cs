using System.Net;
using System.Net.Sockets;
using PlcComLib.Core.PlcTypes;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Tests.Regression;

/// <summary>The README's headline telegram: 4×INT/WORD, BOOL, REAL → 14 bytes in the PLC layout.</summary>
public class ReadmeMachineStatus : S7TelegramBase<ReadmeMachineStatus>
{
    public S7Int  TlgId        { get; set; } = 0;     // 0
    public S7Int  TlgLength    { get; set; } = 0;     // 2
    public S7Word MachineId    { get; set; } = 0;     // 4
    public S7Int  CurrentSpeed { get; set; } = 0;     // 6
    public S7Bool IsRunning    { get; set; } = false; // 8.0
    public S7Real Temperature  { get; set; } = 0f;    // 10 (after one pad byte)
}

/// <summary>Id field preceded by a 1-byte field, so its PLC offset (2) differs from the sum of sizes (1).</summary>
public class PaddedIdTelegram : S7TelegramBase<PaddedIdTelegram>
{
    public S7Byte Prefix { get; set; } = 0;  // 0
    public S7Int  TlgId  { get; set; } = 0;  // 2
    public S7Word Value  { get; set; } = 0;  // 4
}

public class CharArrayStatus : S7TelegramBase<CharArrayStatus>
{
    public S7Int           TlgId { get; set; } = 0;
    public S7CharArray<L4> Code  { get; set; } = Array.Empty<char>();
    public S7Bool          Ready { get; set; } = false;
    public S7Real          Value { get; set; } = 0f;
}

internal static class TestNet
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    public static async Task<T> WithTimeout<T>(this Task<T> task)
        => await task.WaitAsync(Timeout);

    public static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }
}
