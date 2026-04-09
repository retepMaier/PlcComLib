using PlcComLib.DataTypes;
using PlcComLib.Playground.Telg;
using PlcComLib.Tcp;

Console.WriteLine("Hello, World!");



var client = new TcpPlcClientBuilder()
    .ConnectTo("10.80.1.196", 4000)
    .WithNoDelay(true)
    .WithByteOrder(ByteOrder.BigEndian)
    .RegisterTelegram<MachineStatus>()
        .WithMessageId<S7Int>(id: 1, byteOffset: 0)
        .WithLength<S7Int>(length: 8, byteOffset: 2)
    .Build();










// Subscribe to incoming typed telegrams
client.Subscribe<MachineStatus>(msg =>
{
    //Console.WriteLine($"Machine {msg.MachineId}: speed={msg.CurrentSpeed}, running={msg.IsRunning}");
});


client.RawBytesReceived += (sender, bytes) =>
{
    Console.WriteLine($"Received {bytes.Data} bytes: {BitConverter.ToString(bytes.Data)}");
};


client.UnknownTelegramReceived += (sender, raw) =>
{
    Console.WriteLine($"Received unknown telegram with {raw} bytes: {BitConverter.ToString(raw.Payload)}");
};

client.TelegramReceived += (sender, raw) =>
{

};


await client.StartAsync();


await client.SendAsync<MachineStatus>(new MachineStatus
{
    TlgId = 1,
    TlgLength = 12,
    Speed = 123.45f,
});



Console.ReadKey();