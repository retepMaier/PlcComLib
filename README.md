# PlcComLib

> Strongly-typed, high-performance TCP/UDP communication with Siemens S7 PLCs and compatible devices — for .NET 10.

[![.NET](https://img.shields.io/badge/.NET-10%2B-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-13%2B-239120?logo=csharp)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Declare telegram fields as **S7 value structs** — the type carries both the S7 wire semantics and the .NET value. No source generator, no attributes, no reflection on hot paths.

---

## ✨ Features

| | |
|---|---|
| 🧱 **S7 value structs** | Declare properties as `S7Word`, `S7Int`, `S7Real`, `S7String<L32>`, … The type *is* the annotation. |
| ⚡ **Zero hot-path overhead** | Expression-tree delegates are compiled once per type. Every subsequent call is pure pre-compiled code — no reflection, no boxing. |
| 🔗 **Fluent builder API** | Configure connections, register telegram types, and set TelegramId + wire-size in a clean, readable chain. |
| 🔀 **Flexible TelegramId types** | `.WithMessageId<TType>(id, byteOffset)` accepts any S7 framing struct; `id` is `long` so any numeric literal passes without a cast. |
| 🌐 **Multiple transports** | TCP (with auto-reconnect) and UDP — both as client and server. |
| 🖼️ **Flexible framing** | `TelegramIdFramer` (default), or plug in your own `IMessageFramer`. |
| ↔️ **Configurable byte order** | `BigEndian` (Siemens S7 default) or `LittleEndian`, set once on the builder. |
| 🏭 **S7 PLC layout** | `.ToDataBlock()` serialises to an `S7DataBlock` with PLC-accurate byte offsets (even-byte rule + bool-packing). |

---

## 📦 Quick Start

### 1 · Define a typed telegram

Inherit from `S7TelegramBase<T>` and declare properties using the S7 value structs. No attributes, no `partial` keyword — fields are serialised **in declaration order**.

```csharp
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

public class MachineStatus : S7TelegramBase<MachineStatus>
{
    public S7Int   TlgId        { get; set; } = 0;
    public S7Int   TlgLength    { get; set; } = 0;
    public S7Word  MachineId    { get; set; } = 0;
    public S7Int   CurrentSpeed { get; set; } = 0;
    public S7Bool  IsRunning    { get; set; } = false;
    public S7Real  Temperature  { get; set; } = 0f;
}
```

Values are read and written via implicit operators — no casts required:

```csharp
var status = new MachineStatus
{
    TlgId        = 1,
    TlgLength    = 12,
    MachineId    = 42,
    CurrentSpeed = 1500,
    IsRunning    = true,
    Temperature  = 78.3f,
};

ushort id    = status.MachineId;     // S7Word  → ushort
short  speed = status.CurrentSpeed;  // S7Int   → short
bool   run   = status.IsRunning;     // S7Bool  → bool
float  temp  = status.Temperature;   // S7Real  → float
```

**Inherited static members:**

| Member | Description |
|---|---|
| `MachineStatus.WireSize` | Total wire size — sum of all declared S7 field widths |
| `MachineStatus.Definition` | `TelegramDefinition` with field list; `MessageId` set at registration time |
| `MachineStatus.Deserialize(span, byteOrder)` | Creates and populates a new instance from raw bytes |
| `instance.Serialize(byteOrder)` | Serialises the instance to `byte[]` |
| `instance.TelegramId` | Returns `(ushort)Definition.MessageId` |
| `instance.ToDataBlock(byteOrder)` | Serialises to an `S7DataBlock` with named, PLC-offset-indexed field access |

---

### 2 · TCP client

```csharp
using PlcComLib.Tcp;

var client = new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithByteOrder(ByteOrder.BigEndian)   // Siemens S7 default
    .WithNoDelay()                        // avoids Nagle coalescing delays
    .RegisterTelegram<MachineStatus>()
        .WithMessageId<S7Int>(id: 1,  byteOffset: 0)
        .WithLength<S7Int>   (length: 12, byteOffset: 2)
    .Build();

client.Subscribe<MachineStatus>(msg =>
    Console.WriteLine($"Machine {(ushort)msg.MachineId}: speed={(short)msg.CurrentSpeed}"));

await client.StartAsync();
```

---

### 3 · Send a typed telegram

```csharp
var status = new MachineStatus { MachineId = 42, CurrentSpeed = 1500, IsRunning = true };
await client.SendAsync(status);
```

---

### 4 · TCP server

```csharp
var server = new TcpPlcServerBuilder()
    .ListenOn("0.0.0.0", 2000)
    .WithMaxConnections(20)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId<S7Int>(id: 1,  byteOffset: 0)
        .WithLength<S7Int>   (length: 12, byteOffset: 2)
    .Build();

server.Subscribe<MachineStatus>((msg, address, port) =>
    Console.WriteLine($"From {address}:{port} — machine {(ushort)msg.MachineId}"));

await server.StartAsync();

// Unicast reply to a specific client — by Guid or by IP + port
await server.SendToAsync(clientId, status);
await server.SendToAsync("192.168.1.50", 54321, status);
```

---

### 5 · UDP client / server

```csharp
var udpClient = new UdpPlcClientBuilder()
    .SendTo("192.168.1.100", 5000)
    .WithByteOrder(ByteOrder.BigEndian)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId<S7Int>(id: 1, byteOffset: 0)
        .WithLength<S7Int>(length: 12, byteOffset: 2)
    .Build();

var udpServer = new UdpPlcServerBuilder()
    .ListenOn("0.0.0.0", 5000)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId<S7Int>(id: 1, byteOffset: 0)
        .WithLength<S7Int>(length: 12, byteOffset: 2)
    .Build();
```

---

## 🏭 Siemens S7 Data Block Example

The following C# class mirrors a typical TIA Portal / STEP 7 data block exactly. `S7Layout` (used by `.ToDataBlock()`) applies the two mandatory S7 DB layout rules automatically:

- **Even-byte rule** — any multi-byte field must start at an even offset.
- **Bool-packing** — consecutive `BOOL` fields share a byte (up to 8 per byte).

```csharp
// ┌─────────────────────────────────────────────────────────────────┐
// │  TIA Portal equivalent  –  DATA_BLOCK "DB_ConveyorLine"         │
// │                                                                 │
// │  TlgId      : INT;          // DBW  0  (2 bytes)                │
// │  TlgLength  : INT;          // DBW  2  (2 bytes)                │
// │  MachineId  : WORD;         // DBW  4  (2 bytes)                │
// │  Speed      : INT;          // DBW  6  (2 bytes)                │
// │  IsRunning  : BOOL;         // DBX  8.0                         │
// │  Faulted    : BOOL;         // DBX  8.1  (bit-packed)           │
// │  // 8.2–8.7 unused; 9.0 padding (even-byte rule for REAL)       │
// │  Temperature: REAL;         // DBD 10  (4 bytes)                │
// │  Label      : STRING[20];   // DBB 14  (22 bytes = 2 hdr + 20)  │
// │                             // Total PLC size: 36 bytes         │
// └─────────────────────────────────────────────────────────────────┘

public class ConveyorLineTelegram : S7TelegramBase<ConveyorLineTelegram>
{
    public S7Int           TlgId       { get; set; } = 0;
    public S7Int           TlgLength   { get; set; } = 0;
    public S7Word          MachineId   { get; set; } = 0;
    public S7Int           Speed       { get; set; } = 0;
    public S7Bool          IsRunning   { get; set; } = false;
    public S7Bool          Faulted     { get; set; } = false;
    public S7Real          Temperature { get; set; } = 0f;
    public S7String<L20>   Label       { get; set; } = "";
}
```

Serialise to a PLC-aligned data block and inspect each field by name:

```csharp
var telegram = new ConveyorLineTelegram
{
    TlgId       = 1,
    TlgLength   = 36,
    MachineId   = 42,
    Speed       = 1500,
    IsRunning   = true,
    Faulted     = false,
    Temperature = 78.3f,
    Label       = "Line A",
};

S7DataBlock db = telegram.ToDataBlock(ByteOrder.BigEndian);

Console.WriteLine($"Total bytes : {db.Length}");          // 36
Console.WriteLine($"DBW 0 (TlgId)      : {db.OffsetOf("TlgId")}");       // 0
Console.WriteLine($"DBW 4 (MachineId)  : {db.OffsetOf("MachineId")}");   // 4
Console.WriteLine($"DBX 8.0 (IsRunning): {db.OffsetOf("IsRunning")}");   // 8
Console.WriteLine($"DBD 10 (Temp)      : {db.OffsetOf("Temperature")}"); // 10
Console.WriteLine($"DBB 14 (Label)     : {db.OffsetOf("Label")}");       // 14
Console.WriteLine($"Hex dump: {db}");
```

Register it on a connection — the TelegramId is the `S7Int` at offset 0, and the length field is the `S7Int` at offset 2:

```csharp
var client = new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithByteOrder(ByteOrder.BigEndian)
    .WithNoDelay()
    .RegisterTelegram<ConveyorLineTelegram>()
        .WithMessageId<S7Int>(id: 1,  byteOffset: 0)
        .WithLength<S7Int>   (length: 36, byteOffset: 2)
    .Build();
```

---

## 🧩 S7 Value Struct Reference

### Fixed-size types

| Property type | .NET type | Wire bytes | S7 / TIA Portal type |
|---|:---:|:---:|---|
| `S7Bool` | `bool` | 1 | `BOOL` |
| `S7Byte` | `byte` | 1 | `BYTE` / `USINT` |
| `S7SInt` | `sbyte` | 1 | `SINT` |
| `S7Char` | `char` | 1 | `CHAR` |
| `S7Word` | `ushort` | 2 | `WORD` / `UINT` |
| `S7Int` | `short` | 2 | `INT` |
| `S7WChar` | `char` | 2 | `WCHAR` |
| `S7DWord` | `uint` | 4 | `DWORD` / `UDINT` |
| `S7DInt` | `int` | 4 | `DINT` |
| `S7Real` | `float` | 4 | `REAL` |
| `S7LWord` | `ulong` | 8 | `LWORD` / `ULINT` |
| `S7LInt` | `long` | 8 | `LINT` |
| `S7LReal` | `double` | 8 | `LREAL` |
| `S7DateAndTime` | `DateTime` | 8 | `DATE_AND_TIME` (BCD) |

### Variable-length types

The maximum length is baked into the **type argument** — a small struct implementing `IS7Length`.

Pre-defined lengths: `L1 L2 L4 L8 L10 L12 L16 L20 L24 L32 L40 L48 L50 L64 L80 L100 L128 L160 L200 L254`

For a custom length:

```csharp
public struct L42 : IS7Length { public static int Value => 42; }
```

| Property type | .NET type | Wire bytes | S7 / TIA Portal type |
|---|:---:|:---:|---|
| `S7String<TLen>` | `string` | `2 + TLen` | `STRING[TLen]` |
| `S7WString<TLen>` | `string` | `4 + TLen×2` | `WSTRING[TLen]` |
| `S7Raw<TLen>` | `byte[]` | `TLen` | raw byte array |
| `S7CharArray<TLen>` | `char[]` | `TLen` | fixed `CHAR` array |

```csharp
public class ProductTelegram : S7TelegramBase<ProductTelegram>
{
    public S7Word           ProductId   { get; set; } = 0;
    public S7String<L32>    Name        { get; set; } = "";         // wire = 34 bytes
    public S7WString<L20>   Description { get; set; } = "";         // wire = 44 bytes
    public S7Raw<L16>       Checksum    { get; set; } = new byte[16];
    public S7CharArray<L8>  Tag         { get; set; } = new char[8];
}
```

---

## 🔧 Fluent Builder API Reference

### `TcpPlcClientBuilder`

| Method | Default | Description |
|---|---|---|
| `.ConnectTo(host, port)` | `"127.0.0.1"`, `2000` | Remote host and TCP port to connect to |
| `.WithReconnectInterval(interval)` | `5 s` | Delay between automatic reconnection attempts |
| `.WithTimeout(timeout)` | `10 s` | Send and receive socket timeout |
| `.WithNoDelay(noDelay = true)` | `false` | Disables Nagle's algorithm (TCP_NODELAY). Strongly recommended for PLCs |
| `.WithReceiveBufferSize(size)` | `0` (OS default) | Sets SO_RCVBUF. Increase for high-throughput burst connections |
| `.WithSendBufferSize(size)` | `0` (OS default) | Sets SO_SNDBUF. Increase for high-throughput sending |
| `.WithByteOrder(byteOrder)` | `BigEndian` | Byte order for all multi-byte fields. Use `BigEndian` for Siemens S7 |
| `.WithLogger(logger)` | `null` | Attaches an `ILogger<TcpPlcClient>` for structured diagnostic output |
| `.RegisterTelegram<T>()` | — | Registers a typed telegram definition from `T.Definition` |
| `.RegisterTelegram(def)` | — | Registers a hand-crafted `TelegramDefinition` |
| `.WithMessageId<TType>(id, byteOffset)` | — | Sets the TelegramId, byte offset, and wire type for the last registered telegram |
| `.WithLength<TType>(length, byteOffset)` | — | Sets the expected wire size and optional length-field validation |
| `.Build()` | — | Returns a configured `TcpPlcClient` |

### `TcpPlcServerBuilder`

| Method | Default | Description |
|---|---|---|
| `.ListenOn(host, port)` | `"0.0.0.0"`, `2000` | Local address and TCP port to bind to |
| `.WithMaxConnections(max)` | `10` | Maximum concurrent client connections |
| `.WithTimeout(timeout)` | `10 s` | Per-client send/receive socket timeout |
| `.WithNoDelay(noDelay = true)` | `false` | Disables Nagle's algorithm on each accepted socket |
| `.WithReceiveBufferSize(size)` | `0` (OS default) | Sets SO_RCVBUF per accepted client |
| `.WithSendBufferSize(size)` | `0` (OS default) | Sets SO_SNDBUF per accepted client |
| `.WithByteOrder(byteOrder)` | `BigEndian` | Byte order for all multi-byte fields |
| `.WithLogger(logger)` | `null` | Attaches an `ILogger<TcpPlcServer>` |
| `.RegisterTelegram<T>()` | — | Registers a typed telegram definition |
| `.RegisterTelegram(def)` | — | Registers a hand-crafted `TelegramDefinition` |
| `.WithMessageId<TType>(id, byteOffset)` | — | Sets the TelegramId for the last registered telegram |
| `.WithLength<TType>(length, byteOffset)` | — | Sets the expected wire size and optional length-field validation |
| `.Build()` | — | Returns a configured `TcpPlcServer` |

### `UdpPlcClientBuilder`

| Method | Default | Description |
|---|---|---|
| `.SendTo(host, port)` | `"127.0.0.1"`, `2000` | Remote host and UDP port to send datagrams to |
| `.WithTimeout(timeout)` | `5 s` | Socket-level send/receive timeout |
| `.WithReceiveBufferSize(size)` | `0` (OS default) | Sets SO_RCVBUF. Increase to reduce datagram loss under burst load |
| `.WithSendBufferSize(size)` | `0` (OS default) | Sets SO_SNDBUF |
| `.WithByteOrder(byteOrder)` | `BigEndian` | Byte order for all multi-byte fields |
| `.WithLogger(logger)` | `null` | Attaches an `ILogger<UdpPlcClient>` |
| `.RegisterTelegram<T>()` | — | Registers a typed telegram definition |
| `.RegisterTelegram(def)` | — | Registers a hand-crafted `TelegramDefinition` |
| `.WithMessageId<TType>(id, byteOffset)` | — | Sets the TelegramId for the last registered telegram |
| `.WithLength<TType>(length, byteOffset)` | — | Sets the expected wire size and optional length-field validation |
| `.Build()` | — | Returns a configured `UdpPlcClient` |

### `UdpPlcServerBuilder`

| Method | Default | Description |
|---|---|---|
| `.ListenOn(host, port)` | `"0.0.0.0"`, `2000` | Local IP address and UDP port to listen on |
| `.WithReceiveBufferSize(size)` | `0` (OS default) | Sets SO_RCVBUF. Increase to reduce datagram loss under burst load |
| `.WithSendBufferSize(size)` | `0` (OS default) | Sets SO_SNDBUF |
| `.WithByteOrder(byteOrder)` | `BigEndian` | Byte order for all multi-byte fields |
| `.WithLogger(logger)` | `null` | Attaches an `ILogger<UdpPlcServer>` |
| `.RegisterTelegram<T>()` | — | Registers a typed telegram definition |
| `.RegisterTelegram(def)` | — | Registers a hand-crafted `TelegramDefinition` |
| `.WithMessageId<TType>(id, byteOffset)` | — | Sets the TelegramId for the last registered telegram |
| `.WithLength<TType>(length, byteOffset)` | — | Sets the expected wire size and optional length-field validation |
| `.Build()` | — | Returns a configured `UdpPlcServer` |

> **Registration order matters:** `.WithMessageId()` and `.WithLength()` always apply to the *most recently* called `RegisterTelegram()`.

---

## 🆔 TelegramId & Length Type Reference

`.WithMessageId<TType>` and `.WithLength<TType>` accept any `IS7FramingType` struct. The type determines the byte width and signedness used to read the field from the wire.

| `TType` | Wire bytes | Signed | S7 name |
|---|:---:|:---:|---|
| `S7Byte` | 1 | No | `BYTE` |
| `S7SInt` | 1 | Yes | `SINT` |
| `S7Word` | 2 | No | `WORD` |
| `S7Int` | 2 | Yes | `INT` |
| `S7DWord` | 4 | No | `DWORD` |
| `S7DInt` | 4 | Yes | `DINT` |
| `S7LWord` | 8 | No | `LWORD` |
| `S7LInt` | 8 | Yes | `LINT` |

```csharp
// 2-byte signed INT id at offset 0, 2-byte INT length at offset 2 (common S7 layout)
.RegisterTelegram<T>()
    .WithMessageId<S7Int>(id: 1,            byteOffset: 0)
    .WithLength<S7Int>   (length: 36,       byteOffset: 2)

// 4-byte DWORD id at offset 0
.RegisterTelegram<T>()
    .WithMessageId<S7DWord>(id: 0xDEAD_BEEF, byteOffset: 0)

// 8-byte LINT id
.RegisterTelegram<T>()
    .WithMessageId<S7LInt>(id: 0x0102_0304_0506_0708L, byteOffset: 0)
```

---

## 📡 Events & Subscriptions

```csharp
// ── Typed subscriptions (matched by TelegramId) ───────────────────────────────

IDisposable sub = client.Subscribe<MachineStatus>(msg =>
{
    Console.WriteLine($"Speed: {(short)msg.CurrentSpeed}");
});

IDisposable sub2 = client.Subscribe<MachineStatus>((msg, address, port) =>
{
    Console.WriteLine($"From {address}:{port}");
});

sub.Dispose(); // unsubscribe

// ── Raw / diagnostic events ───────────────────────────────────────────────────

client.TelegramReceived        += (_, e) => { /* e.Telegram, e.RawPayload, e.RemoteAddress, e.Port */ };
client.UnknownTelegramReceived += (_, e) => { /* e.Payload, e.CandidateTelegramId */ };
client.ConnectionStateChanged  += (_, e) => Console.WriteLine($"Connected: {e.IsConnected} ({e.Reason})");
client.RawBytesSent            += (_, e) => { /* e.Data, e.RemoteAddress, e.Port */ };
client.RawBytesReceived        += (_, e) => { /* e.Data, e.RemoteAddress, e.Port */ };
```

The server additionally supports unicast send:

```csharp
// Send to all connected clients (broadcast)
await server.SendAsync(status);

// TCP — unicast to a specific client by Guid
await server.SendToAsync(clientId, status);

// TCP — unicast to a specific client by remote IP + port
await server.SendToAsync("192.168.1.50", 54321, status);

// UDP — send to a specific endpoint by IPEndPoint
await udpServer.SendToAsync(remoteEndpoint, status);

// UDP — send to a specific endpoint by IP + port
await udpServer.SendToAsync("192.168.1.50", 5000, status);
```

---

## 📬 Dispatch Priority

When a frame arrives, the connection dispatches it in this order:

1. **TelegramId match** — the value at `Definition.MessageIdByteOffset` (read as `Definition.MessageIdDataType`) equals `Definition.MessageId` → deserialised and fired via `TelegramReceived`.
2. **Size-based fallback** — `MessageId == 0` and payload length matches `Definition.EffectiveWireSize` → deserialised and fired.
3. **Unknown** — no match; fires `UnknownTelegramReceived`.

---

## 🖼️ Wire Format

Fields are serialised exactly as declared, in declaration order, with no implicit header:

```
┌──────────┬──────────┬──────────┬──────────┬─────┐
│ Field 0  │ Field 1  │ Field 2  │    …     │  N  │
└──────────┴──────────┴──────────┴──────────┴─────┘
```

If the TelegramId is part of the payload (as in the Siemens S7 layout above), declare it as the first property and configure its position via `.WithMessageId<TType>(id, byteOffset)`.

---

## ⚙️ Byte Order

Set once on the builder; applies to both the TelegramId field and all multi-byte data fields:

```csharp
.WithByteOrder(ByteOrder.BigEndian)    // Siemens S7 PLCs (default)
.WithByteOrder(ByteOrder.LittleEndian) // x86/ARM PCs, embedded Linux
```

Manual serialisation and deserialisation:

```csharp
byte[]        bytes = telegram.Serialize(ByteOrder.BigEndian);
MachineStatus copy  = MachineStatus.Deserialize(bytes, ByteOrder.BigEndian);
```

---

## 🛠️ Hand-crafted Definitions

For dynamic or legacy telegrams without a fixed class, build a `TelegramDefinition` manually:

```csharp
var def = new TelegramDefinition
{
    Id   = "LegacyStatus",
    Fields =
    [
        new TelegramField { Name = "MachineId", DataType = S7DataType.Word },
        new TelegramField { Name = "Speed",     DataType = S7DataType.Real },
    ],
};

new TcpPlcClientBuilder()
    .RegisterTelegram(def)
        .WithMessageId(0x0099)
        .WithLength(2 + 4)
    .Build();
```

---

## ♻️ Lifecycle

```csharp
await connection.StartAsync();   // connect / start listening
await connection.StopAsync();    // disconnect / stop gracefully
await connection.DisposeAsync(); // stop + release all resources
```

---

## 🔬 How Serialisation Works

`S7TelegramBase<TSelf>` delegates to `S7TelegramReflector<TSelf>`, a **static generic class** that:

1. **Runs once** (static constructor) per concrete telegram type.
2. **Discovers properties** — all public read/write properties whose type implements `IS7FramingType`, ordered by declaration order (`MetadataToken`).
3. **Compiles delegates** via `System.Linq.Expressions` — a `Func<T, TPrimitive>` getter and `Action<T, TPrimitive>` setter per field, using the S7 struct's implicit operators. No boxing of the telegram instance.
4. **Stores the plan** — `FieldPlan[]` with pre-computed offsets, used by every subsequent call.

| Call | Allocation |
|---|---|
| `Serialize()` | One `byte[]` |
| `Deserialize()` | Rents from `ArrayPool<byte>.Shared` — no allocation |

---

## 📋 Requirements

- **.NET 10+**
- **C# 13+** — uses `static abstract` interface members and primary constructors
