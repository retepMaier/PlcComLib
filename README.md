# PlcComLib

A .NET 8 communication library for bidirectional TCP/IP and UDP communication with Siemens SIMATIC PLCs.

## Features

- **TCP Client & Server** – connect to PLCs or accept incoming connections
- **UDP Client & Server** – connectionless datagram communication
- **Automatic Reconnection** – TCP client retries on connection loss
- **Message Framing** – length-prefix (default) and fixed-length framers
- **Telegram Definitions** – declare telegrams in JSON; load at runtime
- **Full S7 Data Type Support** – Bool, Byte, Word, DWord, LWord, SInt, Int, DInt, LInt, USInt, UInt, UDInt, ULInt, Real, LReal, Char, WChar, S7String, S7WString, Date, Time, TimeOfDay, DateAndTime, Raw
- **Big-Endian Byte Swapping** – automatic conversion for all S7 types
- **Thread-Safe** – SemaphoreSlim-guarded sends, ConcurrentDictionary client tracking
- **Structured Logging** – injectable ILogger via Microsoft.Extensions.Logging

## Quick Start

### 1. Define telegrams in JSON

```json
{
  "Id": "MachineStatus",
  "Name": "Machine Status",
  "Fields": [
    { "Name": "MachineId",   "DataType": "Word" },
    { "Name": "Speed",       "DataType": "Real" },
    { "Name": "Temperature", "DataType": "Real" },
    { "Name": "Label",       "DataType": "S7String", "MaxStringLength": 20 }
  ]
}
```

### 2. Load definitions and connect

```csharp
using PlcComLib.Core;
using PlcComLib.Telegrams;
using PlcComLib.Tcp;

var registry = new TelegramRegistry();
registry.LoadFromFile("telegrams/machine_status.json");

var config = new ConnectionConfiguration
{
    Host = "192.168.1.100",
    Port = 2000,
    Mode = ConnectionMode.Client,
    Protocol = ProtocolType.Tcp
};

await using var client = new TcpPlcClient(config, registry);
client.TelegramReceived += (_, e) =>
{
    var speed = e.Telegram.GetValue<float>("Speed");
    Console.WriteLine($"Speed: {speed} rpm");
};
client.ConnectionStateChanged += (_, e) =>
    Console.WriteLine($"Connected: {e.IsConnected} ({e.Reason})");

await client.StartAsync();
```

### 3. Send a telegram

```csharp
var def = registry.Get("SetpointCommand");
var cmd = new Telegram(def);
cmd.SetValue("CommandId", (uint)42);
cmd.SetValue("TargetSpeed", 1500.0f);
cmd.SetValue("TargetTemp", 80.0f);

await client.SendAsync(cmd);
```

## Project Structure

```
src/PlcComLib/
  Core/           - IPlcConnection, ConnectionConfiguration, enums
  DataTypes/      - S7DataType, ByteSwapper, S7String/S7WString, S7TypeConverter
  Framing/        - IMessageFramer, LengthPrefixFramer, FixedLengthFramer
  Telegrams/      - TelegramField, TelegramDefinition, Telegram, TelegramSerializer, TelegramRegistry
  Tcp/            - TcpPlcClient, TcpPlcServer
  Udp/            - UdpPlcClient, UdpPlcServer
  samples/        - Example JSON telegram definitions

tests/PlcComLib.Tests/
  DataTypes/      - ByteSwapperTests, S7StringTests, S7TypeConverterTests
  Framing/        - LengthPrefixFramerTests
  Telegrams/      - TelegramSerializerTests, TelegramRegistryTests
```

## Supported S7 Data Types

| S7 Type      | .NET Type  | Wire Size |
|-------------|------------|-----------|
| Bool        | bool       | 1 byte    |
| Byte/USInt  | byte       | 1 byte    |
| SInt        | sbyte      | 1 byte    |
| Word/UInt   | ushort     | 2 bytes   |
| Int         | short      | 2 bytes   |
| DWord/UDInt | uint       | 4 bytes   |
| DInt        | int        | 4 bytes   |
| LWord/ULInt | ulong      | 8 bytes   |
| LInt        | long       | 8 bytes   |
| Real        | float      | 4 bytes   |
| LReal       | double     | 8 bytes   |
| Char        | char       | 1 byte    |
| WChar       | char       | 2 bytes   |
| S7String    | S7String   | 2+MaxLen  |
| S7WString   | S7WString  | 4+MaxLen*2|
| DateAndTime | DateTime   | 8 bytes   |
| Raw         | byte[]     | variable  |

## License

MIT
