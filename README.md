# PlcComLib

A high-performance **.NET 10** communication library for bidirectional TCP/IP and UDP communication with Siemens SIMATIC PLCs and other industrial devices.

## Features

- **Roslyn Source Generator** – declare typed telegrams as `partial` classes; the compiler generates zero-allocation serialize/deserialize code at build time
- **TCP Client & Server** – connect to PLCs or accept incoming connections with automatic reconnection
- **UDP Client & Server** – connectionless datagram communication
- **Fluent Builder API** – `TcpPlcClientBuilder`, `TcpPlcServerBuilder`, `UdpPlcClientBuilder`, `UdpPlcServerBuilder` with full per-parameter XML documentation
- **`ITelegram` interface** – every typed telegram carries a `TelegramId`; only `ITelegram` types can be sent or subscribed to
- **`Subscribe<T>` / `SendAsync<T>`** – strongly-typed send and receive using static abstract interface members (zero reflection)
- **`UnknownTelegramReceived` event** – callback whenever a received payload cannot be matched to any registered telegram
- **MessageId-based Dispatch** – every typed telegram carries a 2-byte MessageId as the first wire bytes, eliminating size-collision ambiguity; byte order follows the connection setting
- **Connection-Level Byte Order** – `WithByteOrder(ByteOrder.LittleEndian)` on the builder; byte order is a connection concern, not per-telegram
- **Full S7 Data Type Support** – Bool, Byte/USInt, SInt, Word/UInt, Int, DWord/UDInt, DInt, LWord/ULInt, LInt, Real, LReal, Char, WChar, S7String, S7WString, Date, Time, TimeOfDay, DateAndTime, Raw
- **Message Framing** – length-prefix (default) or fixed-length framers; plug in your own via `IMessageFramer`
- **Thread-Safe** – `SemaphoreSlim`-guarded sends, `ConcurrentDictionary` client tracking
- **Structured Logging** – injectable `ILogger` via `Microsoft.Extensions.Logging`

---

## Quick Start

### 1. Declare a typed telegram

Add the `PlcComLib.SourceGenerator` project as an **Analyzer** reference. The generator injects the `[S7Telegram]` attribute and all field attributes into your compilation automatically.

```csharp
using PlcComLib.SourceGenerator;

// A telegram for a Siemens S7 PLC (big-endian by default)
[S7Telegram(messageId: 0x0001)]
public partial class MachineStatus
{
    [S7Word]                  public ushort MachineId    { get; set; }
    [S7Real]                  public float  Speed        { get; set; }
    [S7Real]                  public float  Temperature  { get; set; }
    [S7String(maxLength: 20)] public string Label        { get; set; } = "";
}

// A telegram for a Linux/Windows sensor board (little-endian set on the connection)
[S7Telegram(messageId: 0x0010)]
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
| `ushort TelegramId` | Instance property — same value as `MessageId` at runtime; fulfils the `ITelegram` interface (instance-level alias of `MessageId`) |
| `static int WireSize` | 2 (header) + sum of all field wire sizes |
| `static TelegramDefinition Definition` | Built lazily from the declared attributes (no JSON required) |
| `byte[] Serialize(ByteOrder byteOrder = BigEndian)` | Inlined `BinaryPrimitives` calls — no boxing |
| `static T Deserialize(ReadOnlySpan<byte>, ByteOrder byteOrder = BigEndian)` | Validates TelegramId & length, then reads fields |

---

## Siemens S7 Datablock Layout

The following shows how the example `MachineStatus` telegram maps onto a Siemens S7 DB.
The first two bytes are always the `TelegramId` (`MessageId`) — a `WORD` at `DBW0` in the datablock:

```
Wire layout (34 bytes total):
┌─────────────────────────────────────────────────────────────────────────┐
│ Offset │ Size │ S7 Type │ DB variable        │ C# property               │
├────────┼──────┼─────────┼────────────────────┼───────────────────────────┤
│   0    │  2   │  WORD   │ DB1.DBW0           │ TelegramId = 0x0001       │
│   2    │  2   │  WORD   │ DB1.DBW2           │ MachineId  (ushort)        │
│   4    │  4   │  REAL   │ DB1.DBD4           │ Speed      (float)         │
│   8    │  4   │  REAL   │ DB1.DBD8           │ Temperature (float)        │
│  12    │  22  │  STRING │ DB1.DBB12 (len=20) │ Label      (string)        │
│        │      │         │   [0]=max=20        │                           │
│        │      │         │   [1]=actual length │                           │
│        │      │         │   [2..21]=chars     │                           │
└─────────────────────────────────────────────────────────────────────────┘
Total payload = 2 (TelegramId) + 2 + 4 + 4 + 22 = 34 bytes
```

**Corresponding S7 SCL datablock:**

```pascal
DATA_BLOCK DB1
  TITLE = MachineStatus
  VERSION : 0.1

  STRUCT
    TelegramId  : WORD;      // DBW0  — matches [S7Telegram(messageId: 0x0001)]
    MachineId   : WORD;      // DBW2  — matches [S7Word]
    Speed       : REAL;      // DBD4  — matches [S7Real]
    Temperature : REAL;      // DBD8  — matches [S7Real]
    Label       : STRING[20];// DBB12 — matches [S7String(maxLength: 20)]
  END_STRUCT;

BEGIN
  TelegramId  := W#16#0001;
  MachineId   := W#16#0000;
  Speed       := 0.0;
  Temperature := 0.0;
  Label       := '';
END_DATA_BLOCK
```

---

## MessageId / TelegramId Explained

Every telegram type declared with `[S7Telegram(messageId: 0x0001)]` carries a **MessageId** — a 2-byte unsigned integer that uniquely identifies the telegram type on the wire.

> **Two names, one value:** The same 2-byte identifier is accessible in two ways:
> - **`MessageId`** — the `static` compile-time constant on the class (set via the `messageId` attribute parameter).
> - **`TelegramId`** — the instance property from the `ITelegram` interface; always returns the same value as `MessageId` at runtime.
>
> On the **wire and in the S7 DB** it is the first 2 bytes / first `WORD` field (`DBW0`).

### Why it exists

In a TCP stream, multiple different telegram types may flow between PLC and host. Without a type identifier, the receiver must guess which telegram arrived based only on payload size — which is fragile (two types can have the same size) and impossible when sizes change.

The TelegramId solves this: it is **always the first two bytes of every payload**, serialised using the connection's byte order. The dispatcher reads those two bytes (respecting byte order), looks up the registered definition, and hands the rest to the correct deserializer.

### Wire format

```
┌──────────────────────┬──────────────────────────────────────┐
│  TelegramId (2 bytes)│  Data fields (N bytes)               │
│  byte order =        │  byte order = connection setting     │
│  connection setting  │                                      │
└──────────────────────┴──────────────────────────────────────┘
```

- Both the TelegramId header and data fields (Word, Int, Real, DWord, …) use the connection's `ByteOrder` setting.

### ITelegram interface

```csharp
public interface ITelegram
{
    ushort TelegramId { get; }
}
```

Every source-generated telegram class implements `ITelegram` automatically. Only types that implement `ITelegram` (via `ITypedS7Telegram<T>`) can be passed to `SendAsync<T>` or `Subscribe<T>`. This is enforced at compile time.

### Choosing MessageId values

- Use values `0x0001`–`0xFFFE`. `0x0000` is reserved for legacy size-based matching.
- Keep IDs unique across your entire project. A global `TelegramIds.cs` enum or constants file is recommended:

```csharp
public static class TelegramIds
{
    public const ushort MachineStatus = 0x0001;
    public const ushort SensorReading = 0x0010;
    public const ushort DeviceCommand = 0x0020;
}
```

---

## Message Framing Explained

TCP is a *stream* protocol — it delivers bytes in order but with no concept of message boundaries. PlcComLib uses a **framer** to split the raw byte stream into discrete messages before they reach the dispatcher.

### Built-in framers

#### `LengthPrefixFramer` (default)

```
Wire: [Length: UInt32 BE (4 bytes)][Payload: byte * Length]
```

Each message is preceded by a 4-byte big-endian integer that encodes the payload length. The receiver buffers incoming bytes, reads the 4-byte header, then waits until the declared number of payload bytes have arrived before handing off the complete message.

**Pros:** Handles any payload size; robust against partial reads; simple and widely compatible.  
**Use when:** Both ends of the connection are running PlcComLib or a compatible implementation.

```csharp
new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithLengthPrefixFramer()   // default — can be omitted
    ...
```

#### `FixedLengthFramer`

```
Wire: [Payload: exactly N bytes]
```

No header. Every message is exactly `N` bytes. The receiver buffers until `N` bytes are available, then delivers them.

**Pros:** Zero overhead; compatible with legacy PLCs that send fixed-size blocks.  
**Cons:** All messages must be exactly the same size; mismatches cause deserialization errors.

```csharp
new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithFixedLengthFramer(MachineStatus.WireSize)
    ...
```

### Custom `IMessageFramer`

Implement `IMessageFramer` when your device uses a proprietary framing protocol such as:
- STX/ETX delimiters
- SLIP (Serial Line Internet Protocol) encoding
- A fixed header with a type-dependent length field
- Any other non-standard scheme

**Interface:**

```csharp
public interface IMessageFramer
{
    /// <summary>
    /// Wraps a raw payload in the framing envelope (adds header, trailer, escaping, etc.)
    /// before it is written to the network stream.
    /// </summary>
    /// <param name="payload">The raw telegram bytes to wrap.</param>
    /// <returns>The complete framed byte array ready for transmission.</returns>
    byte[] Frame(ReadOnlySpan<byte> payload);

    /// <summary>
    /// Attempts to extract one complete message from the receive buffer.
    /// Called repeatedly until it returns false.
    /// </summary>
    /// <param name="buffer">All bytes received so far (may contain partial or multiple messages).</param>
    /// <param name="message">
    /// When this method returns true, contains the extracted payload (without framing bytes).
    /// </param>
    /// <param name="consumed">
    /// When this method returns true, the number of bytes consumed from the start of
    /// <paramref name="buffer"/> (including any framing overhead). The caller removes
    /// these bytes from its accumulation buffer.
    /// </param>
    /// <returns>
    /// <c>true</c> if a complete message was extracted; <c>false</c> if more data is needed.
    /// </returns>
    bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed);
}
```

**Example — STX/ETX framer (0x02 start-of-text, 0x03 end-of-text):**

```csharp
using PlcComLib.Framing;

public sealed class StxEtxFramer : IMessageFramer
{
    private const byte STX = 0x02;
    private const byte ETX = 0x03;

    /// <summary>
    /// Wraps payload as: [STX][payload bytes][ETX]
    /// </summary>
    public byte[] Frame(ReadOnlySpan<byte> payload)
    {
        var framed = new byte[1 + payload.Length + 1];
        framed[0] = STX;
        payload.CopyTo(framed.AsSpan(1));
        framed[^1] = ETX;
        return framed;
    }

    /// <summary>
    /// Scans the buffer for an STX…ETX pair. If found, returns the bytes between them.
    /// Discards any leading bytes before STX (guards against noise on the line).
    /// </summary>
    public bool TryExtract(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> message, out int consumed)
    {
        message  = default;
        consumed = 0;

        // Find STX
        int stxIndex = buffer.IndexOf(STX);
        if (stxIndex < 0) return false;

        // Find ETX after STX
        var afterStx = buffer[(stxIndex + 1)..];
        int etxIndex = afterStx.IndexOf(ETX);
        if (etxIndex < 0) return false;

        message  = afterStx[..etxIndex];            // bytes between STX and ETX
        consumed = stxIndex + 1 + etxIndex + 1;     // STX + payload + ETX
        return true;
    }
}
```

**Registration:**

```csharp
await using var client = new TcpPlcClientBuilder()
    .ConnectTo("10.0.0.5", 9000)
    .WithFramer(new StxEtxFramer())     // plug in your custom framer
    .WithByteOrder(ByteOrder.LittleEndian)
    .RegisterTelegram<SensorReading>()
    .Build();
```

---

## TCP Client

```csharp
using PlcComLib.Tcp;
using PlcComLib.DataTypes;

await using var client = new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)       // PLC IP and port
    .WithReconnectInterval(TimeSpan.FromSeconds(3))
    .WithTimeout(TimeSpan.FromSeconds(10))
    .WithLengthPrefixFramer()               // or WithFixedLengthFramer / WithFramer
    .WithByteOrder(ByteOrder.BigEndian)     // S7 default; omit for same effect
    .WithLogger(loggerFactory.CreateLogger<TcpPlcClient>())
    .RegisterTelegram<MachineStatus>()
    .RegisterTelegram<SensorReading>()
    .Build();

// Typed subscription (only ITelegram types accepted)
using var sub = client.Subscribe<MachineStatus>(msg =>
    Console.WriteLine($"[{msg.MachineId}] Speed={msg.Speed} rpm  Temp={msg.Temperature}°C"));

// Callback for unrecognised telegrams
client.UnknownTelegramReceived += (_, e) =>
    Console.WriteLine($"Unknown payload ({e.Payload.Length} bytes, candidate TelegramId=0x{e.CandidateTelegramId:X4})");

client.ConnectionStateChanged += (_, e) =>
    Console.WriteLine($"TCP: {(e.IsConnected ? "Connected" : "Disconnected")} — {e.Reason}");

await client.StartAsync();

// Send a typed telegram (byte order applied automatically from connection setting)
await client.SendAsync(new MachineStatus { MachineId = 1, Speed = 1500f, Temperature = 22.5f, Label = "Line-A" });

Console.ReadLine();
```

---

## TCP Server

```csharp
using PlcComLib.Tcp;
using PlcComLib.DataTypes;

await using var server = new TcpPlcServerBuilder()
    .ListenOn("0.0.0.0", 2000)
    .WithMaxConnections(50)
    .WithTimeout(TimeSpan.FromSeconds(30))
    .WithLengthPrefixFramer()
    .WithByteOrder(ByteOrder.BigEndian)
    .WithLogger(loggerFactory.CreateLogger<TcpPlcServer>())
    .RegisterTelegram<MachineStatus>()
    .RegisterTelegram<SensorReading>()
    .Build();

using var sub = server.Subscribe<MachineStatus>(msg =>
    Console.WriteLine($"Server received: MachineId={msg.MachineId} Speed={msg.Speed}"));

server.UnknownTelegramReceived += (_, e) =>
    Console.WriteLine($"Server: unrecognised telegram, {e.Payload.Length} bytes, id=0x{e.CandidateTelegramId:X4}");

server.ConnectionStateChanged += (_, e) =>
    Console.WriteLine($"Server: {e.Reason}");

await server.StartAsync();

// Broadcast to all connected clients
await server.SendAsync(new MachineStatus { MachineId = 99, Speed = 0, Label = "Shutdown" });

Console.ReadLine();
await server.StopAsync();
```

---

## UDP Client

```csharp
using PlcComLib.Udp;
using PlcComLib.DataTypes;

await using var client = new UdpPlcClientBuilder()
    .SendTo("192.168.1.100", 5000)
    .WithTimeout(TimeSpan.FromSeconds(5))
    .WithByteOrder(ByteOrder.BigEndian)
    .WithLogger(loggerFactory.CreateLogger<UdpPlcClient>())
    .RegisterTelegram<SensorReading>()
    .Build();

using var sub = client.Subscribe<SensorReading>(r =>
    Console.WriteLine($"Sensor {r.SensorId}: {r.Value}"));

client.UnknownTelegramReceived += (_, e) =>
    Console.WriteLine($"UDP: unrecognised datagram ({e.Payload.Length} bytes)");

await client.StartAsync();

await client.SendAsync(new SensorReading { SensorId = 42, Value = 23.7f });

Console.ReadLine();
```

---

## UDP Server

```csharp
using PlcComLib.Udp;
using PlcComLib.DataTypes;

await using var server = new UdpPlcServerBuilder()
    .ListenOn("0.0.0.0", 5000)
    .WithByteOrder(ByteOrder.BigEndian)
    .WithLogger(loggerFactory.CreateLogger<UdpPlcServer>())
    .RegisterTelegram<SensorReading>()
    .Build();

using var sub = server.Subscribe<SensorReading>(r =>
    Console.WriteLine($"UDP server got sensor {r.SensorId}: {r.Value}"));

server.UnknownTelegramReceived += (_, e) =>
    Console.WriteLine($"UDP server: unrecognised datagram ({e.Payload.Length} bytes)");

await server.StartAsync();

Console.ReadLine();
await server.StopAsync();
```

---

## Non-PLC Devices (Little-Endian)

```csharp
// Declare the telegram — byte order is NOT part of the attribute any more
[S7Telegram(messageId: 0x0020)]
public partial class DeviceCommand
{
    [S7DWord] public uint  CommandCode { get; set; }
    [S7Real]  public float Parameter   { get; set; }
    [S7Bool]  public bool  Execute     { get; set; }
}

// Configure little-endian on the connection builder
await using var client = new TcpPlcClientBuilder()
    .ConnectTo("10.0.0.5", 9000)
    .WithFixedLengthFramer(DeviceCommand.WireSize)
    .WithByteOrder(ByteOrder.LittleEndian)      // <-- connection-level byte order
    .RegisterTelegram<DeviceCommand>()
    .Build();

await client.StartAsync();
await client.SendAsync(new DeviceCommand { CommandCode = 1, Parameter = 3.14f, Execute = true });
```

> **Note:** Both the 2-byte **TelegramId header** and all data fields use the same `ByteOrder` set on the connection.

---

## Unknown Telegram Callback

When a received payload's `TelegramId` or size does not match any registered definition, the
`UnknownTelegramReceived` event fires instead of silently discarding the message:

```csharp
client.UnknownTelegramReceived += (sender, e) =>
{
    // e.Payload          — raw bytes of the unrecognised message (framing stripped)
    // e.CandidateTelegramId — first 2 bytes interpreted as ushort using the connection's ByteOrder (0 if < 2 bytes)

    Console.WriteLine(
        $"Unrecognised telegram: {e.Payload.Length} bytes, " +
        $"candidate TelegramId=0x{e.CandidateTelegramId:X4}");

    // Optional: log hex dump for debugging
    Console.WriteLine(BitConverter.ToString(e.Payload));
};
```

This event is available on all four connection types: `TcpPlcClient`, `TcpPlcServer`,
`UdpPlcClient`, and `UdpPlcServer`. It is also surfaced in `IPlcConnection` so it can be
handled generically.

---

## API Reference

### `TcpPlcClientBuilder`

| Method | Parameter(s) | Description |
|--------|-------------|-------------|
| `ConnectTo(host, port)` | `host`: hostname or IP; `port`: TCP port | Sets the remote PLC host and TCP port to connect to. |
| `WithReconnectInterval(interval)` | `interval`: `TimeSpan` | How long to wait between automatic reconnection attempts. Default: **5 s**. |
| `WithTimeout(timeout)` | `timeout`: `TimeSpan` | Send/receive socket timeout. If no data flows within this period, the connection is reset. Default: **10 s**. |
| `WithLengthPrefixFramer()` | — | 4-byte big-endian length header before each payload. **Default.** |
| `WithFixedLengthFramer(frameSize)` | `frameSize`: exact bytes per message | No header; every message is exactly `frameSize` bytes. |
| `WithFramer(framer)` | `framer`: `IMessageFramer` | Plug in a fully custom framing implementation. |
| `WithByteOrder(byteOrder)` | `byteOrder`: `ByteOrder` | `BigEndian` (S7 default) or `LittleEndian`. Applied to all data fields on this connection. |
| `WithLogger(logger)` | `logger`: `ILogger<TcpPlcClient>` | Structured logging via Microsoft.Extensions.Logging. |
| `RegisterTelegram<T>()` | `T : ITypedS7Telegram<T>` | Register a source-generated telegram type for dispatch. |
| `RegisterTelegram(definition)` | `definition`: `TelegramDefinition` | Register a hand-crafted definition for legacy dispatch. |
| `Build()` | — | Returns a configured `TcpPlcClient`. Call `.StartAsync()` to open the connection. |

### `TcpPlcServerBuilder`

| Method | Parameter(s) | Description |
|--------|-------------|-------------|
| `ListenOn(host, port)` | `host`: bind address; `port`: TCP port | Local address to bind. Use `"0.0.0.0"` to accept on all interfaces. |
| `WithMaxConnections(max)` | `max`: int | Maximum concurrent client connections. Default: **10**. |
| `WithTimeout(timeout)` | `timeout`: `TimeSpan` | Per-client send/receive socket timeout. Default: **10 s**. |
| `WithLengthPrefixFramer()` | — | 4-byte big-endian length header. **Default.** |
| `WithFixedLengthFramer(frameSize)` | `frameSize`: int | Fixed-size messages, no header. |
| `WithFramer(framer)` | `framer`: `IMessageFramer` | Custom framing implementation. |
| `WithByteOrder(byteOrder)` | `byteOrder`: `ByteOrder` | `BigEndian` (default) or `LittleEndian`. |
| `WithLogger(logger)` | `logger`: `ILogger<TcpPlcServer>` | Structured logging. |
| `RegisterTelegram<T>()` | `T : ITypedS7Telegram<T>` | Register a source-generated telegram. |
| `RegisterTelegram(definition)` | `definition`: `TelegramDefinition` | Register a hand-crafted definition. |
| `Build()` | — | Returns a configured `TcpPlcServer`. Call `.StartAsync()` to begin accepting. |

### `UdpPlcClientBuilder`

| Method | Parameter(s) | Description |
|--------|-------------|-------------|
| `SendTo(host, port)` | `host`: hostname or IP; `port`: UDP port | Sets the remote endpoint to send datagrams to. |
| `WithTimeout(timeout)` | `timeout`: `TimeSpan` | Socket-level send/receive timeout. Default: **5 s**. |
| `WithByteOrder(byteOrder)` | `byteOrder`: `ByteOrder` | `BigEndian` (default) or `LittleEndian`. |
| `WithLogger(logger)` | `logger`: `ILogger<UdpPlcClient>` | Structured logging. |
| `RegisterTelegram<T>()` | `T : ITypedS7Telegram<T>` | Register a source-generated telegram. |
| `RegisterTelegram(definition)` | `definition`: `TelegramDefinition` | Register a hand-crafted definition. |
| `Build()` | — | Returns a configured `UdpPlcClient`. Call `.StartAsync()` to begin. |

### `UdpPlcServerBuilder`

| Method | Parameter(s) | Description |
|--------|-------------|-------------|
| `ListenOn(host, port)` | `host`: bind address; `port`: UDP port | Local UDP endpoint to listen on. |
| `WithByteOrder(byteOrder)` | `byteOrder`: `ByteOrder` | `BigEndian` (default) or `LittleEndian`. |
| `WithLogger(logger)` | `logger`: `ILogger<UdpPlcServer>` | Structured logging. |
| `RegisterTelegram<T>()` | `T : ITypedS7Telegram<T>` | Register a source-generated telegram. |
| `RegisterTelegram(definition)` | `definition`: `TelegramDefinition` | Register a hand-crafted definition. |
| `Build()` | — | Returns a configured `UdpPlcServer`. Call `.StartAsync()` to begin listening. |

### `[S7Telegram]` attribute

```csharp
[S7Telegram(ushort messageId)]
```

| Parameter | Description |
|-----------|-------------|
| `messageId` | Unique 2-byte identifier. Always transmitted big-endian as the first 2 bytes of every payload. Must be unique across all telegram types in your project. |

> **Byte order is no longer set per telegram.** Use `.WithByteOrder()` on the connection builder instead. This makes the byte order a connection concern — all telegrams on a given connection share the same byte order.

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

### `IPlcConnection` events

| Event | Args type | Fired when |
|-------|-----------|------------|
| `TelegramReceived` | `TelegramReceivedEventArgs` | A payload was matched and deserialised successfully. |
| `ConnectionStateChanged` | `ConnectionStateChangedEventArgs` | The connection opened, closed, or changed state. |
| `UnknownTelegramReceived` | `UnknownTelegramEventArgs` | A payload could not be matched to any registered telegram definition. |

**`UnknownTelegramEventArgs` properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Payload` | `byte[]` | Raw wire bytes of the unrecognised message (framing already stripped). |
| `CandidateTelegramId` | `ushort` | First 2 bytes of `Payload` interpreted as a `ushort` using the connection's `ByteOrder`; `0` if payload shorter than 2 bytes. Useful for diagnosing which telegram type was received but not registered. |

### `ByteOrder` enum (`PlcComLib.DataTypes`)

| Value | Description |
|-------|-------------|
| `ByteOrder.BigEndian` | Most-significant byte first. Siemens S7 wire format. **Default.** |
| `ByteOrder.LittleEndian` | Least-significant byte first. x86/ARM native byte order (Windows, Linux). |

---

## Project Structure

```
src/
  PlcComLib/
    Core/          IPlcConnection, ConnectionConfiguration, event args
                   (TelegramReceivedEventArgs, ConnectionStateChangedEventArgs,
                    UnknownTelegramEventArgs), enums
    DataTypes/     ByteOrder, S7DataType, ByteSwapper, S7String/S7WString,
                   S7TypeConverter
    Framing/       IMessageFramer, LengthPrefixFramer, FixedLengthFramer
    Telegrams/     ITelegram, ITypedS7Telegram<T>, TelegramField,
                   TelegramDefinition, Telegram, TelegramSerializer,
                   TelegramRegistry
    Tcp/           TcpPlcClient, TcpPlcClientBuilder,
                   TcpPlcServer, TcpPlcServerBuilder
    Udp/           UdpPlcClient, UdpPlcClientBuilder,
                   UdpPlcServer, UdpPlcServerBuilder

  PlcComLib.SourceGenerator/
    S7TelegramGenerator.cs   IIncrementalGenerator implementation
                             (injects [S7Telegram] and all [S7*] field attributes)

tests/
  PlcComLib.Tests/                   Core unit tests
  PlcComLib.SourceGenerator.Tests/   Generator integration tests
```

---

## Message Framing

| Framer | Wire format | Best for |
|--------|-------------|----------|
| `LengthPrefixFramer` (default) | `[Length:UInt32 BE][Payload]` | TCP streams of varying-size messages |
| `FixedLengthFramer(n)` | `[Payload]` exactly n bytes | Legacy PLCs or fixed-size protocols |
| Custom `IMessageFramer` | Any proprietary scheme | STX/ETX, SLIP, custom headers, etc. |

See [Custom `IMessageFramer`](#custom-imessageframer) above for a complete implementation example.

---

## License

MIT
