# PlcComLib

A .NET source-generated library for strongly-typed, zero-reflection communication with Siemens S7 PLCs (and any compatible device) over TCP or UDP.

---

## Features

- **Source-generated typed telegrams** — define a `partial class` with property attributes; the generator emits `Serialize`, `Deserialize`, `WireSize`, and `Definition` at compile time. No runtime reflection.
- **Fluent builder API** — configure connections, register telegram types, and set TelegramId + wire-size in a readable chain.
- **Flexible TelegramId types** — `.WithMessageId<TType>(id, byteOffset)` accepts any numeric type (`byte`, `short`, `int`, `uint`, `long`, `ulong` …) via the `S7*` framing markers; the `id` parameter is typed `long` so any numeric value passes without a cast.
- **Flexible length types** — `.WithLength<TType>(length, byteOffset)` accepts any numeric type the same way.
- **Multiple transports** — TCP (with auto-reconnect) and UDP, both with client and server variants.
- **Flexible framing** — default `TelegramIdFramer` reads the TelegramId from the configured byte offset and data type, and uses the registered wire-size to frame messages; `LengthFramer` for PLC payloads with an embedded length; or plug in your own `IMessageFramer`.
- **Configurable byte order** — `BigEndian` (Siemens S7 default) or `LittleEndian`, set once on the connection builder.

---

## Wire Format

Every serialised telegram has the layout:

```
[TelegramId field][Data fields…]
```

- The **TelegramId** is written/read using the connection's byte order. Its value, byte offset, and wire data type are set at registration time via `.WithMessageId<TType>(id, byteOffset)` (or the scalar `.WithMessageId(id)` overload).
- **No embedded length field by default** — frame boundaries are determined by the `TelegramIdFramer` using the registered wire size from `.WithLength(size)`.

For source-generated telegrams the default wire layout is:

```
[TelegramId : UInt16 (2 bytes)][Data fields…]
```

---

## Quick Start

### 1. Define a typed telegram

```csharp
using PlcComLib.SourceGenerator;

[S7Telegram]
public partial class MachineStatus
{
    // No [MsgId] or [MsgLength] attributes needed.
    // Define any properties you like — the TelegramId and wire-size
    // are registered on the connection builder below.

    [S7Word] public ushort MachineId    { get; set; }
    [S7Real] public float  CurrentSpeed { get; set; }
    [S7Bool] public bool   IsRunning    { get; set; }
    [S7Real] public float  Temperature  { get; set; }
}
```

The source generator emits, at compile time:

| Member | Description |
|---|---|
| `MachineStatus.WireSize` | Total wire size: 2 (TelegramId) + data fields |
| `MachineStatus.Definition` | `TelegramDefinition` with field list and runtime `MessageId` |
| `MachineStatus.Deserialize(span, byteOrder)` | Zero-allocation deserialization |
| `instance.Serialize(byteOrder)` | Serializes to `byte[]` |
| `instance.TelegramId` | Returns `(ushort)Definition.MessageId` (set by the builder) |

### 2. Create a TCP client

```csharp
using PlcComLib.Tcp;

var client = new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithByteOrder(ByteOrder.BigEndian)          // Siemens S7 default
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)                   // id on the wire
        .WithLength(MachineStatus.WireSize)      // used by the TelegramIdFramer
    .RegisterTelegram<SensorReading>()
        .WithMessageId(0x0002)
        .WithLength(SensorReading.WireSize)
    .Build();

// Subscribe to incoming typed telegrams
client.Subscribe<MachineStatus>(msg =>
{
    Console.WriteLine($"Machine {msg.MachineId}: speed={msg.CurrentSpeed}, running={msg.IsRunning}");
});

await client.StartAsync();
```

### 3. Send a typed telegram

```csharp
var status = new MachineStatus
{
    MachineId    = 42,
    CurrentSpeed = 1500.0f,
    IsRunning    = true,
    Temperature  = 78.3f,
};
await client.SendAsync(status);
```

### 4. Create a TCP server

```csharp
var server = new TcpPlcServerBuilder()
    .ListenOn("0.0.0.0", 2000)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)
        .WithLength(MachineStatus.WireSize)
    .Build();

server.Subscribe<MachineStatus>(msg =>
{
    Console.WriteLine($"Received from machine {msg.MachineId}");
});

await server.StartAsync();
```

---

## Registration Fluent API

The chain after `RegisterTelegram<T>()` configures the **TelegramId** and **wire size** for that telegram type. Both settings are stored in `T.Definition`.

```csharp
builder
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)               // id written/read at the configured offset
        .WithLength(MachineStatus.WireSize)  // tells TelegramIdFramer how many bytes = 1 message
    .RegisterTelegram<AlarmTelegram>()
        .WithMessageId(0x0010)
        .WithLength(AlarmTelegram.WireSize)
    .Build();
```

| Method | Description |
|---|---|
| `RegisterTelegram<T>()` | Registers the source-generated `T.Definition` in the registry |
| `RegisterTelegram(def)` | Registers a hand-crafted `TelegramDefinition` |
| `.WithMessageId(id)` | Sets the TelegramId on the last registered definition. `id` is `long` — any numeric type is accepted without a cast |
| `.WithMessageId<TType>(id, byteOffset)` | Sets the id, its byte offset in the payload, and its wire data type. `id` is `long` |
| `.WithLength(size)` | Sets the wire size on the last registered definition. `size` is `long` |
| `.WithLength<TType>(length, byteOffset)` | Sets the wire size, the length-field offset, and its wire data type. `length` is `long` |

> **Order matters:** `.WithMessageId()` and `.WithLength()` always apply to the *most recently* called `RegisterTelegram()`.

---

## Flexible TelegramId Types

The `id` parameter of `.WithMessageId<TType>(id, byteOffset)` (and the scalar `.WithMessageId(id)`) is typed `long`, so you can pass **any numeric value** without an explicit cast:

```csharp
// Hex literals, int, uint, ushort — all accepted directly
int    intId   = 0x0001;
uint   uintId  = 0xDEAD_BEEF;
ushort wordId  = 0x0001;

// 2-byte unsigned Word id at offset 0, 2-byte signed Int length at offset 2
.RegisterTelegram<T>()
    .WithMessageId<S7Word>(id: wordId, byteOffset: 0)
    .WithLength<S7Int>    (length: 16, byteOffset: 2)

// 4-byte unsigned DWord id at offset 0
.RegisterTelegram<T>()
    .WithMessageId<S7DWord>(id: uintId, byteOffset: 0)

// 8-byte signed LInt id
.RegisterTelegram<T>()
    .WithMessageId<S7LInt>(id: 0x0102_0304_0506_0708L, byteOffset: 0)
```

The `<TType>` generic parameter (one of the `S7*` framing markers below) tells the framer how many bytes to read and how to interpret them. The `id` value is compared using 64-bit signed arithmetic so no information is lost regardless of the concrete numeric type used.

### Framing type markers

| Marker | Wire bytes | Signed/Unsigned | S7 type |
|---|---|---|---|
| `S7Byte`  | 1 | Unsigned | BYTE |
| `S7SInt`  | 1 | Signed   | SINT |
| `S7Word`  | 2 | Unsigned | WORD |
| `S7Int`   | 2 | Signed   | INT  |
| `S7DWord` | 4 | Unsigned | DWORD |
| `S7DInt`  | 4 | Signed   | DINT |
| `S7LWord` | 8 | Unsigned | LWORD |
| `S7LInt`  | 8 | Signed   | LINT |

---

## Flexible Length Types

Similarly, the `length` / `wireSize` parameter of all `.WithLength()` overloads is typed `long`:

```csharp
ushort wireSize = 16;

.WithLength(wireSize)                               // scalar — sets the wire size only
.WithLength<S7Int>(length: wireSize, byteOffset: 2) // with length-field validation
```

---

## Supported S7 Data Types (Telegram Fields)

| Attribute | C# type | Wire bytes | S7 type |
|---|---|---|---|
| `[S7Bool]` | `bool` | 1 | BOOL |
| `[S7Byte]` / `[S7USInt]` | `byte` | 1 | BYTE / USINT |
| `[S7SInt]` | `sbyte` | 1 | SINT |
| `[S7Char]` | `char` | 1 | CHAR |
| `[S7Word]` / `[S7UInt]` | `ushort` | 2 | WORD / UINT |
| `[S7Int]` | `short` | 2 | INT |
| `[S7Date]` | `ushort` | 2 | DATE |
| `[S7WChar]` | `char` | 2 | WCHAR |
| `[S7DWord]` / `[S7UDInt]` | `uint` | 4 | DWORD / UDINT |
| `[S7DInt]` | `int` | 4 | DINT |
| `[S7Time]` | `int` | 4 | TIME |
| `[S7TimeOfDay]` | `uint` | 4 | TIME_OF_DAY |
| `[S7Real]` | `float` | 4 | REAL |
| `[S7LWord]` / `[S7ULInt]` | `ulong` | 8 | LWORD / ULINT |
| `[S7LInt]` | `long` | 8 | LINT |
| `[S7LReal]` | `double` | 8 | LREAL |
| `[S7DateAndTime]` | `DateTime` | 8 | DATE_AND_TIME |
| `[S7String(maxLength)]` | `string` | 2 + maxLength | STRING |
| `[S7WString(maxLength)]` | `string` | 4 + maxLength\*2 | WSTRING |
| `[S7Raw(byteCount)]` | `byte[]` | byteCount | raw bytes |
| `[S7CharArray(length)]` | `char[]` | length | fixed CHAR array |

---

## Framing

### Default: `TelegramIdFramer`

When telegrams are registered with `.WithMessageId(id).WithLength(size)`, the `TelegramIdFramer` is used automatically. It reads the TelegramId from the configured byte offset and data type, looks up the registered wire size, and waits until that many bytes are available before dispatching.

The default reads bytes 0–1 as a 16-bit unsigned Word. Use the generic overload to change both:

```csharp
// 4-byte DWord id at offset 0, 2-byte Int length at offset 4
.RegisterTelegram<MachineStatus>()
    .WithMessageId<S7DWord>(id: 0x0001_0002, byteOffset: 0)
    .WithLength<S7Int>     (length: 22,      byteOffset: 4)
```

### `LengthFramer` (PLC embedded-length format)

Use when the remote PLC/device embeds a total-length field at bytes 2–3 of the payload:

```
[TelegramId: UInt16 (2 bytes)][TotalLength: UInt16 (2 bytes)][Data fields…]
```

```csharp
new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithLengthFramer()   // reads TotalLength from wire bytes 2–3
    .RegisterTelegram<MachineStatus>().WithMessageId(0x0001).WithLength(22)
    .Build();
```

### Custom framer

```csharp
new TcpPlcClientBuilder()
    .WithFramer(new MyStxEtxFramer())
    .RegisterTelegram<MachineStatus>().WithMessageId(0x0001).WithLength(22)
    .Build();
```

---

## UDP

UDP datagrams are self-delimited (no stream framing needed). Use the UDP builders:

```csharp
// Client (send + receive)
var udpClient = new UdpPlcClientBuilder()
    .SendTo("192.168.1.100", 5000)
    .WithByteOrder(ByteOrder.BigEndian)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)
        .WithLength(MachineStatus.WireSize)
    .Build();

// Server (receive-only)
var udpServer = new UdpPlcServerBuilder()
    .ListenOn("0.0.0.0", 5000)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)
        .WithLength(MachineStatus.WireSize)
    .Build();
```

---

## Byte Order

Set the byte order once on the builder. It applies to both the TelegramId field and all multi-byte data fields:

```csharp
.WithByteOrder(ByteOrder.BigEndian)    // Siemens S7 PLCs (default)
.WithByteOrder(ByteOrder.LittleEndian) // x86/ARM PCs, embedded Linux
```

To test serialisation manually:

```csharp
var bytes = telegram.Serialize(ByteOrder.BigEndian);
var copy  = MachineStatus.Deserialize(bytes, ByteOrder.BigEndian);
```

---

## Hand-crafted Definitions (no source generator)

For dynamic or legacy telegrams, build a `TelegramDefinition` manually:

```csharp
var def = new TelegramDefinition
{
    Id     = "LegacyStatus",
    Fields =
    [
        new TelegramField { Name = "MachineId", DataType = S7DataType.Word },
        new TelegramField { Name = "Speed",     DataType = S7DataType.Real },
    ],
};

new TcpPlcClientBuilder()
    .RegisterTelegram(def)
        .WithMessageId(0x0099)
        .WithLength(2 + 2 + 4)   // TelegramId(2) + Word(2) + Real(4)
    .Build();
```

Or with a custom id position and type:

```csharp
var def = new TelegramDefinition
{
    Id                  = "CustomLayout",
    MessageId           = 0x0001_ABCD,
    MessageIdByteOffset = 4,
    MessageIdDataType   = S7DataType.DWord,
    ConfiguredWireSize  = 20,
    LengthByteOffset    = 0,
    LengthDataType      = S7DataType.Word,
};
```

---

## Subscription Model

```csharp
// Typed subscription — uses T.Definition.MessageId for matching
IDisposable sub = client.Subscribe<MachineStatus>(msg => { /* ... */ });

// With remote address and port
IDisposable sub2 = client.Subscribe<MachineStatus>((msg, address, port) =>
{
    Console.WriteLine($"From {address}:{port} — machine {msg.MachineId}");
});

// Unsubscribe
sub.Dispose();

// Raw/untyped events
client.TelegramReceived          += (_, e) => { /* e.RawPayload */ };
client.UnknownTelegramReceived   += (_, e) => { /* e.RawPayload */ };
client.ConnectionStateChanged    += (_, e) => Console.WriteLine(e.IsConnected);
client.RawBytesSent              += (_, e) => { /* e.Bytes */ };
client.RawBytesReceived          += (_, e) => { /* e.Bytes */ };
```

---

## Dispatch Priority

When a message arrives, the connection dispatches it using the following priority:

1. **TelegramId match** — the value read at `Definition.MessageIdByteOffset` (using `Definition.MessageIdDataType`) equals `Definition.MessageId` → deserialized and fired
2. **Size-based fallback** — `MessageId == 0` and payload length matches `Definition.EffectiveWireSize` → deserialized and fired
3. **Unknown** — fires `UnknownTelegramReceived`

---

## Reconnection (TCP client)

The `TcpPlcClient` reconnects automatically:

```csharp
new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithReconnectInterval(TimeSpan.FromSeconds(5))
    .WithTimeout(TimeSpan.FromSeconds(10))
    // ...
    .Build();
```

---

## Lifecycle

```csharp
await client.StartAsync();   // connect / start listening
await client.StopAsync();    // disconnect / stop
await client.DisposeAsync(); // stop + release resources
```

---

## Requirements

- .NET 10+ (uses `static abstract` interface members)
- C# 13+
