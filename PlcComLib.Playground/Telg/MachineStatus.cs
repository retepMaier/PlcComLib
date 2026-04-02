using PlcComLib.DataTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Playground.Telg;

public class MachineStatus : S7TelegramBase<MachineStatus>
{
    public S7Int TlgId { get; set; } = 0;
    public S7Int TlgLength { get; set; } = 0;
    public S7Word MachineId { get; set; } = 0;
    public S7Int CurrentSpeed { get; set; } = 0;
    public S7Bool IsRunning { get; set; } = false;
    public S7Real Temperature { get; set; } = 0f;
}
