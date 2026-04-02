# PlcComLib

A .NET library for strongly-typed, high-performance communication with Siemens S7 PLCs (and any compatible device) over TCP or UDP.

Telegram fields are declared as **S7 value structs** — the type carries both the S7 wire semantics and the .NET value. No source generator, no attributes, no reflection on hot paths.

---

## Features

- **S7 value structs** — declare properties as `S7Word`, `S7Int`, `S7Real`, `S7String<L10>`, … The type *is* the annotation. Assign and read with plain .NET values via implicit operators.
- **Zero hot-path overhead** — `S7TelegramBase<T>` compiles per-property delegates once at first use (Expression trees + static generic class). Every subsequent Serialize/Deserialize call runs pre-compiled delegates with no runtime reflection and no boxing of the telegram instance.
- **Fluent builder API** — configure connections, register telegram types, and set TelegramId + wire-size in a readable chain.
- **Flexible TelegramId types** — `.WithMessageId<TType>(id, byteOffset)` accepts any S7 framing struct; `id` is `long` so any numeric literal passes without a cast.
- **Multiple transports** — TCP (with auto-reconnect) and UDP, both with client and server variants.
- **Flexible framing** — default `TelegramIdFramer` reads the TelegramId from the configured byte offset and data type; `LengthFramer` for payloads with an embedded length; or plug in your own `IMessageFramer`.
- **Configurable byte order** — `BigEndian` (Siemens S7 default) or `LittleEndian`, set once on the connection builder.

---

## Quick Start

### 1. Define a typed telegram

Inherit from `S7TelegramBase<T>` and declare properties using the S7 value structs. No attributes, no `partial` keyword.

```csharp
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

public class MachineStatus : S7TelegramBase<MachineStatus>
{
    public S7Word  MachineId    { get; set; } = 0;
    public S7Real  CurrentSpeed { get; set; } = 0f;
    public S7Bool  IsRunning    { get; set; } = false;
    public S7Real  Temperature  { get; set; } = 0f;
}
```

Fields are serialized **in declaration order**. The field type determines the wire format; the .NET value is accessed via implicit operators:

```csharp
var status = new MachineStatus();

// Assign .NET values — implicit operators do the conversion
status.MachineId    = 42;        // ushort → S7Word
status.CurrentSpeed = 1500.0f;   // float  → S7Real
status.IsRunning    = true;       // bool   → S7Bool
status.Temperature  = 78.3f;

// Read as .NET values — implicit operators again
ushort id    = status.MachineId;     // S7Word  → ushort
float  speed = status.CurrentSpeed;  // S7Real  → float
bool   run   = status.IsRunning;     // S7Bool  → bool
```

| Inherited member | Description |
|---|---|
| `MachineStatus.WireSize` | Total wire size: sum of all declared S7 field widths |
| `MachineStatus.Definition` | `TelegramDefinition` with field list; `MessageId` set at registration time |
| `MachineStatus.Deserialize(span, byteOrder)` | Creates and populates a new instance from raw bytes |
| `instance.Serialize(byteOrder)` | Serializes to `byte[]` |
| `instance.TelegramId` | Returns `(ushort)Definition.MessageId` (set by the builder) |

### 2. Create a TCP client

```csharp
using PlcComLib.Tcp;

var client = new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithByteOrder(ByteOrder.BigEndian)          // Siemens S7 default
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)                   // id value on the wire
        .WithLength(MachineStatus.WireSize)      // used by TelegramIdFramer for framing
    .RegisterTelegram<SensorReading>()
        .WithMessageId(0x0002)
        .WithLength(SensorReading.WireSize)
    .Build();

// Subscribe to incoming typed telegrams
client.Subscribe<MachineStatus>(msg =>
{
    Console.WriteLine($"Machine {(ushort)msg.MachineId}: speed={(float)msg.CurrentSpeed}");
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
    Console.WriteLine($"Received from machine {(ushort)msg.MachineId}");
});

await server.StartAsync();
```

---

## S7 Value Struct Types

### Fixed-size types

Declare a property with the S7 struct type. Assign and read with the corresponding .NET primitive — the implicit operators handle conversion transparently.

| Property type | .NET type | Wire bytes | S7 type |
|---|---|---|---|
| `S7Bool` | `bool` | 1 | BOOL |
| `S7Byte` | `byte` | 1 | BYTE / USINT |
| `S7SInt` | `sbyte` | 1 | SINT |
| `S7Char` | `char` | 1 | CHAR |
| `S7Word` | `ushort` | 2 | WORD / UINT |
| `S7Int` | `short` | 2 | INT |
| `S7WChar` | `char` | 2 | WCHAR |
| `S7DWord` | `uint` | 4 | DWORD / UDINT |
| `S7DInt` | `int` | 4 | DINT |
| `S7Real` | `float` | 4 | REAL |
| `S7LWord` | `ulong` | 8 | LWORD / ULINT |
| `S7LInt` | `long` | 8 | LINT |
| `S7LReal` | `double` | 8 | LREAL |
| `S7DateAndTime` | `DateTime` | 8 | DATE_AND_TIME (BCD) |

### Variable-length types

The maximum length is encoded as a **type argument** — a small struct implementing `IS7Length`. Common lengths are pre-defined (`L1`, `L2`, `L4`, `L8`, `L10`, `L12`, `L16`, `L20`, `L24`, `L32`, `L40`, `L48`, `L50`, `L64`, `L80`, `L100`, `L128`, `L160`, `L200`, `L254`). For a custom length, define your own:

```csharp
public struct L42 : IS7Length { public static int Value => 42; }
```

| Property type | .NET type | Wire bytes | S7 type |
|---|---|---|---|
| `S7String<TLen>` | `string` | 2 + TLen | STRING |
| `S7WString<TLen>` | `string` | 4 + TLen×2 | WSTRING |
| `S7Raw<TLen>` | `byte[]` | TLen | raw bytes |
| `S7CharArray<TLen>` | `char[]` | TLen | fixed CHAR array |

```csharp
public class ProductTelegram : S7TelegramBase<ProductTelegram>
{
    public S7Word         ProductId   { get; set; } = 0;
    public S7String<L32>  Name        { get; set; } = "";     // max 32 chars, wire = 34 bytes
    public S7WString<L20> Description { get; set; } = "";     // max 20 chars, wire = 44 bytes
    public S7Raw<L16>     Checksum    { get; set; } = new byte[16];
    public S7CharArray<L8> Tag        { get; set; } = new char[8];
}

// Assign strings directly:
var t = new ProductTelegram { Name = "Widget A", Description = "Premium widget" };

// Read back as string:
string name = t.Name;   // implicit S7String<L32> → string
```

---

## How Serialization Works

`S7TelegramBase<TSelf>` delegates to `S7TelegramReflector<TSelf>`, a static generic class that:

1. **Runs once** (in its static constructor) per concrete telegram type.
2. **Discovers properties** via reflection: all public read/write properties whose type implements `IS7FramingType`, ordered by source-declaration order (`MetadataToken`).
3. **Compiles delegates** via `System.Linq.Expressions`: a `Func<T, TPrimitive>` getter and `Action<T, TPrimitive>` setter for each field. The getter and setter use the S7 struct's implicit operators so there is no boxing of the telegram instance.
4. **Stores the delegates** in a private `FieldPlan[]` array alongside pre-computed offsets.

On every subsequent `Serialize` / `Deserialize` call:
- `Serialize` allocates one `byte[]` and iterates the plan, calling each compiled serialize delegate.
- `Deserialize` rents a `byte[]` from `ArrayPool<byte>.Shared` (no allocation), iterates the plan calling each compiled deserialize delegate, then returns the array to the pool.

---

## Registration Fluent API

```csharp
builder
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)               // id at default offset 0, Word type
        .WithLength(MachineStatus.WireSize)  // TelegramIdFramer frame size
    .RegisterTelegram<AlarmTelegram>()
        .WithMessageId(0x0010)
        .WithLength(AlarmTelegram.WireSize)
    .Build();
```

| Method | Description |
|---|---|
| `RegisterTelegram<T>()` | Registers `T.Definition` (from the base class) in the registry |
| `RegisterTelegram(def)` | Registers a hand-crafted `TelegramDefinition` |
| `.WithMessageId(id)` | Sets the TelegramId. `id` is `long` — any numeric type accepted |
| `.WithMessageId<TType>(id, byteOffset)` | Sets id, byte offset, and wire data type. `id` is `long` |
| `.WithLength(size)` | Sets the expected wire size |
| `.WithLength<TType>(length, byteOffset)` | Sets wire size, length-field offset, and data type |

> **Order matters:** `.WithMessageId()` and `.WithLength()` always apply to the *most recently* called `RegisterTelegram()`.

---

## Flexible TelegramId and Length Types

Both `.WithMessageId<TType>` and `.WithLength<TType>` accept any `IS7FramingType` struct as `<TType>`, which determines the byte width and signedness used to read the field from the wire.

```csharp
// 2-byte Word id at offset 0, 2-byte Int length at offset 2
.RegisterTelegram<T>()
    .WithMessageId<S7Word>(id: 0x0001, byteOffset: 0)
    .WithLength<S7Int>    (length: 18,  byteOffset: 2)

// 4-byte DWord id at offset 0
.RegisterTelegram<T>()
    .WithMessageId<S7DWord>(id: 0xDEAD_BEEF, byteOffset: 0)

// 8-byte LInt id
.RegisterTelegram<T>()
    .WithMessageId<S7LInt>(id: 0x0102_0304_0506_0708L, byteOffset: 0)
```

### Framing type reference

| Type | Wire bytes | Signed | S7 name |
|---|---|---|---|
| `S7Byte` | 1 | No | BYTE |
| `S7SInt` | 1 | Yes | SINT |
| `S7Word` | 2 | No | WORD |
| `S7Int` | 2 | Yes | INT |
| `S7DWord` | 4 | No | DWORD |
| `S7DInt` | 4 | Yes | DINT |
| `S7LWord` | 8 | No | LWORD |
| `S7LInt` | 8 | Yes | LINT |

---

## Wire Format

Every serialised telegram contains exactly the fields declared on the class, in declaration order:

```
[Field 0][Field 1]…[Field N]
```

No implicit header is prepended. The TelegramId field (if present on the wire) must be declared explicitly as a property if it appears in the payload, or its position/type is configured via `.WithMessageId<TType>(id, byteOffset)`.

---

## Framing

### Default: `TelegramIdFramer`

Reads the TelegramId from the configured byte offset and data type, looks up the registered wire size, and waits until that many bytes are buffered before dispatching.

```csharp
// 4-byte DWord id at offset 0, 2-byte Int length at offset 4
.RegisterTelegram<MachineStatus>()
    .WithMessageId<S7DWord>(id: 0x0001_0002, byteOffset: 0)
    .WithLength<S7Int>     (length: 22,      byteOffset: 4)
```

### `LengthFramer` (embedded-length format)

Use when the device embeds a total-length field at bytes 2–3:

```
[TelegramId: UInt16 (2 bytes)][TotalLength: UInt16 (2 bytes)][Data fields…]
```

```csharp
new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithLengthFramer()
    .RegisterTelegram<MachineStatus>().WithMessageId(0x0001).WithLength(22)
    .Build();
```

### Custom framer

```csharp
new TcpPlcClientBuilder()
    .WithFramer(new MyStxEtxFramer())
    .Build();
```

---

## UDP

```csharp
// Client
var udpClient = new UdpPlcClientBuilder()
    .SendTo("192.168.1.100", 5000)
    .WithByteOrder(ByteOrder.BigEndian)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)
        .WithLength(MachineStatus.WireSize)
    .Build();

// Server
var udpServer = new UdpPlcServerBuilder()
    .ListenOn("0.0.0.0", 5000)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId(0x0001)
        .WithLength(MachineStatus.WireSize)
    .Build();
```

---

## Byte Order

Set once on the builder; applies to both the TelegramId field and all multi-byte data fields:

```csharp
.WithByteOrder(ByteOrder.BigEndian)    // Siemens S7 PLCs (default)
.WithByteOrder(ByteOrder.LittleEndian) // x86/ARM PCs, embedded Linux
```

Manual serialisation:

```csharp
var bytes = telegram.Serialize(ByteOrder.BigEndian);
var copy  = MachineStatus.Deserialize(bytes, ByteOrder.BigEndian);
```

---

## Hand-crafted Definitions (no S7TelegramBase)

For dynamic or legacy telegrams without a fixed class, build a `TelegramDefinition` manually:

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
        .WithLength(2 + 4)
    .Build();
```

---

## Subscription Model

```csharp
// Typed subscription — matched by Definition.MessageId
IDisposable sub = client.Subscribe<MachineStatus>(msg => { /* ... */ });

// With remote address and port (useful on servers)
IDisposable sub2 = client.Subscribe<MachineStatus>((msg, address, port) =>
{
    Console.WriteLine($"From {address}:{port} — machine {(ushort)msg.MachineId}");
});

// Unsubscribe
sub.Dispose();

// Raw / untyped events
client.TelegramReceived        += (_, e) => { /* e.RawPayload */ };
client.UnknownTelegramReceived += (_, e) => { /* e.RawPayload */ };
client.ConnectionStateChanged  += (_, e) => Console.WriteLine(e.IsConnected);
client.RawBytesSent            += (_, e) => { /* e.Bytes */ };
client.RawBytesReceived        += (_, e) => { /* e.Bytes */ };
```

---

## Dispatch Priority

When a message arrives the connection dispatches it in this order:

1. **TelegramId match** — value at `Definition.MessageIdByteOffset` (read as `Definition.MessageIdDataType`) equals `Definition.MessageId` → deserialised and fired.
2. **Size-based fallback** — `MessageId == 0` and payload length matches `Definition.EffectiveWireSize` → deserialised and fired.
3. **Unknown** — fires `UnknownTelegramReceived`.

---

## Reconnection (TCP client)

```csharp
new TcpPlcClientBuilder()
    .ConnectTo("192.168.1.100", 2000)
    .WithReconnectInterval(TimeSpan.FromSeconds(5))
    .WithTimeout(TimeSpan.FromSeconds(10))
    .WithNoDelay()           // recommended: avoids Nagle coalescing delays
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

- .NET 10+
- C# 13+ (uses `static abstract` interface members and primary constructors)
