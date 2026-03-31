# PlcComLib

A high-performance **.NET 10** communication library for bidirectional TCP/IP and UDP communication with Siemens SIMATIC PLCs and other industrial devices.

## Features

- **Roslyn Source Generator** – declare typed telegrams as `partial` classes; the compiler generates zero-allocation serialize/deserialize code at build time
- **TCP Client & Server** – connect to PLCs or accept incoming connections with automatic reconnection
- **UDP Client & Server** – connectionless datagram communication
- **Fluent Builder API** – `TcpPlcClientBuilder`, `TcpPlcServerBuilder`, `UdpPlcClientBuilder`, `UdpPlcServerBuilder`
- **Typed `Subscribe<T>` / `SendAsync<T>`** – strongly-typed send and receive using static abstract interface members (zero reflection)
- **MessageId-based Dispatch** – every typed telegram carries a 2-byte big-endian MessageId as the first wire bytes, eliminating size-collision ambiguity
- **Configurable Byte Order** – `ByteOrder.BigEndian` (Siemens S7, default) or `ByteOrder.LittleEndian` (Windows/Linux devices)
- **Full S7 Data Type Support** – Bool, Byte/USInt, SInt, Word/UInt, Int, DWord/UDInt, DInt, LWord/ULInt, LInt, Real, LReal, Char, WChar, S7String, S7WString, Date, Time, TimeOfDay, DateAndTime, Raw
- **Message Framing** – length-prefix (default) or fixed-length framers; bring your own via `IMessageFramer`
- **Thread-Safe** – `SemaphoreSlim`-guarded sends, `ConcurrentDictionary` client tracking
- **Structured Logging** – injectable `ILogger` via `Microsoft.Extensions.Logging`

---

## Quick Start

### 1. Declare a typed telegram

Add the `PlcComLib.SourceGenerator` project as an **Analyzer** reference. The generator injects the attribute types into your compilation automatically — no extra package reference needed at runtime.

```csharp
using PlcComLib.SourceGenerator;

// Big-endian (Siemens S7 default)
[S7Telegram(messageId: 0x0001)]
public partial class MachineStatus
{
    [S7Word]              public ushort MachineId    { get; set; }
    [S7Real]              public float  Speed        { get; set; }
    [S7Real]              public float  Temperature  { get; set; }
    [S7String(maxLength: 20)] public string Label   { get; set; } = "";
}

// Little-endian (non-PLC device, e.g. a Linux sensor board)
[S7Telegram(messageId: 0x0010, byteOrder: ByteOrder.LittleEndian)]
public partial class SensorReading
{
    [S7Word] public ushort SensorId { get; set; }
    [S7Real] public float  Value    { get; set; }
}
```

The generator emits (among other members):

| Member | Description |
|--------|-------------|
| `static ushort MessageId` | The compile-time constant from the attribute |
| `static int WireSize` | 2 (header) + sum of all field sizes |
| `static TelegramDefinition Definition` | Built lazily from the declared attributes |
| `byte[] Serialize()` | Inlined `BinaryPrimitives` calls — no boxing |
| `static T Deserialize(ReadOnlySpan<byte>)` | Validates MessageId & length, then reads fields |

---

## TCP Client

```csharp
using PlcComLib.Tcp;

await using var client = new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithReconnectInterval(TimeSpan.FromSeconds(3))
    .WithTimeout(TimeSpan.FromSeconds(10))
    .WithLengthPrefixFramer()           // default; can also use .WithFixedLengthFramer(n)
    .WithLogger(loggerFactory.CreateLogger<TcpPlcClient>())
    .RegisterTelegram<MachineStatus>()  // reads T.Definition — zero reflection
    .RegisterTelegram<SensorReading>()
    .Build();

// Typed, zero-reflection subscription
using var sub = client.Subscribe<MachineStatus>(msg =>
    Console.WriteLine($"[{msg.MachineId}] Speed={msg.Speed} rpm  Temp={msg.Temperature}°C  Label={msg.Label}"));

client.ConnectionStateChanged += (_, e) =>
    Console.WriteLine($"TCP: {(e.IsConnected ? "Connected" : "Disconnected")} — {e.Reason}");

await client.StartAsync();

// Send a typed telegram
await client.SendAsync(new MachineStatus { MachineId = 1, Speed = 1500f, Temperature = 22.5f, Label = "Line-A" });

Console.ReadLine();
```

---

## TCP Server

```csharp
using PlcComLib.Tcp;

await using var server = new TcpPlcServerBuilder()
    .ListenOn("0.0.0.0", 2000)
    .WithMaxConnections(50)
    .WithTimeout(TimeSpan.FromSeconds(30))
    .WithLengthPrefixFramer()
    .WithLogger(loggerFactory.CreateLogger<TcpPlcServer>())
    .RegisterTelegram<MachineStatus>()
    .RegisterTelegram<SensorReading>()
    .Build();

// Typed subscription — called for every MachineStatus telegram from any client
using var sub = server.Subscribe<MachineStatus>(msg =>
    Console.WriteLine($"Server received: MachineId={msg.MachineId} Speed={msg.Speed}"));

server.ConnectionStateChanged += (_, e) =>
    Console.WriteLine($"Server: {e.Reason}");

await server.StartAsync();

// Broadcast to all clients
await server.SendAsync(new MachineStatus { MachineId = 99, Speed = 0, Label = "Shutdown" });

// Unicast to a specific client (clientId comes from tracking your connections)
// await server.SendToAsync<MachineStatus>(clientId, new MachineStatus { ... });

Console.ReadLine();
await server.StopAsync();
```

---

## UDP Client

```csharp
using PlcComLib.Udp;

await using var client = new UdpPlcClientBuilder()
    .SendTo("192.168.1.100", 5000)
    .WithTimeout(TimeSpan.FromSeconds(5))
    .WithLogger(loggerFactory.CreateLogger<UdpPlcClient>())
    .RegisterTelegram<SensorReading>()
    .Build();

using var sub = client.Subscribe<SensorReading>(r =>
    Console.WriteLine($"Sensor {r.SensorId}: {r.Value}"));

await client.StartAsync();

// Fire-and-forget datagram
await client.SendAsync(new SensorReading { SensorId = 42, Value = 23.7f });

Console.ReadLine();
```

---

## UDP Server

```csharp
using PlcComLib.Udp;

await using var server = new UdpPlcServerBuilder()
    .ListenOn("0.0.0.0", 5000)
    .WithLogger(loggerFactory.CreateLogger<UdpPlcServer>())
    .RegisterTelegram<SensorReading>()
    .Build();

using var sub = server.Subscribe<SensorReading>(r =>
    Console.WriteLine($"UDP server got sensor {r.SensorId}: {r.Value}"));

await server.StartAsync();

// Reply to last sender
// await server.SendAsync(new SensorReading { SensorId = 0, Value = 0 });

// Send to a specific endpoint
// await server.SendToAsync(new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5001), response);

Console.ReadLine();
await server.StopAsync();
```

---

## Non-PLC Devices (Little-Endian)

```csharp
// Telegram for a Linux/Windows device that uses little-endian layout
[S7Telegram(messageId: 0x0020, byteOrder: ByteOrder.LittleEndian)]
public partial class DeviceCommand
{
    [S7DWord] public uint   CommandCode { get; set; }
    [S7Real]  public float  Parameter   { get; set; }
    [S7Bool]  public bool   Execute     { get; set; }
}

await using var client = new TcpPlcClientBuilder()
    .ConnectTo("10.0.0.5", 9000)
    .WithFixedLengthFramer(DeviceCommand.WireSize)   // no length header needed
    .RegisterTelegram<DeviceCommand>()
    .Build();

await client.SendAsync(new DeviceCommand { CommandCode = 1, Parameter = 3.14f, Execute = true });
```

> **Note:** The 2-byte **MessageId header is always big-endian** regardless of `ByteOrder`. Only the data fields (Word, Int, Real, …) are affected by the byte-order setting.

---

## Source Generator Reference

### `[S7Telegram]` attribute

```csharp
[S7Telegram(ushort messageId, ByteOrder byteOrder = ByteOrder.BigEndian)]
```

| Parameter | Description |
|-----------|-------------|
| `messageId` | Unique 2-byte identifier. First 2 bytes on the wire, always big-endian. |
| `byteOrder` | `BigEndian` (Siemens S7, default) or `LittleEndian` (non-PLC). |

### Property attributes by S7 data type

| Attribute(s) | .NET type | Wire size |
|--------------|-----------|-----------|
| `[S7Bool]` | `bool` | 1 byte |
| `[S7Byte]` / `[S7USInt]` | `byte` | 1 byte |
| `[S7SInt]` | `sbyte` | 1 byte |
| `[S7Char]` | `char` | 1 byte |
| `[S7Word]` / `[S7UInt]` | `ushort` | 2 bytes |
| `[S7Int]` / `[S7Date]` | `short` | 2 bytes |
| `[S7WChar]` | `char` | 2 bytes |
| `[S7DWord]` / `[S7UDInt]` / `[S7TimeOfDay]` | `uint` | 4 bytes |
| `[S7DInt]` / `[S7Time]` | `int` | 4 bytes |
| `[S7Real]` | `float` | 4 bytes |
| `[S7LWord]` / `[S7ULInt]` | `ulong` | 8 bytes |
| `[S7LInt]` | `long` | 8 bytes |
| `[S7LReal]` | `double` | 8 bytes |
| `[S7DateAndTime]` | `DateTime` | 8 bytes (BCD) |
| `[S7String(byte maxLength = 254)]` | `string` | 2 + maxLength |
| `[S7WString(ushort maxLength = 254)]` | `string` | 4 + maxLength × 2 |
| `[S7Raw(int byteCount)]` | `byte[]` | byteCount |

---

## Project Structure

```
src/
  PlcComLib/
    Core/          IPlcConnection, ConnectionConfiguration, event args, enums
    DataTypes/     S7DataType, ByteSwapper, S7String/S7WString, S7TypeConverter
    Framing/       IMessageFramer, LengthPrefixFramer, FixedLengthFramer
    Telegrams/     ITypedS7Telegram<T>, TelegramField, TelegramDefinition,
                   Telegram, TelegramSerializer, TelegramRegistry
    Tcp/           TcpPlcClient, TcpPlcClientBuilder,
                   TcpPlcServer, TcpPlcServerBuilder
    Udp/           UdpPlcClient, UdpPlcClientBuilder,
                   UdpPlcServer, UdpPlcServerBuilder

  PlcComLib.SourceGenerator/
    S7TelegramGenerator.cs   IIncrementalGenerator implementation
                             (also injects all [S7Telegram] / [S7*] attributes)

tests/
  PlcComLib.Tests/                   Core unit tests
  PlcComLib.SourceGenerator.Tests/   Generator integration tests
```

---

## Message Framing

| Framer | Wire format | Use case |
|--------|-------------|----------|
| `LengthPrefixFramer` (default) | `[Length:UInt32 BE][Payload]` | TCP streams |
| `FixedLengthFramer(n)` | `[Payload]` exactly n bytes | UDP or fixed-size protocols |
| Custom `IMessageFramer` | Anything | Proprietary protocols |

---

## License

MIT
