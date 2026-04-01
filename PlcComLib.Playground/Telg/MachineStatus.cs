using System.Buffers.Binary;
using PlcComLib.DataTypes;
using PlcComLib.SourceGenerator;
using PlcComLib.Telegrams;

namespace PlcComLib.Playground.Telg;

[ S7Telegram]
public partial class MachineStatus
{
    [S7Int] public short TlgId { get; set; }
    [S7Int] public short TlgLength { get; set; }
    [S7Int] public short MachineId { get; set; }
    [S7Int] public short CurrentSpeed { get; set; }
    [S7Int] public short IsRunning { get; set; }
    [S7Int] public short Temperature { get; set; }
}
