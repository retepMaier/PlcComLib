using PlcComLib.Core.PlcTypes;
using PlcComLib.Telegrams;

namespace PlcComLib.Playground.Telg;

public class MachineStatus : S7TelegramBase<MachineStatus>
{
    public S7Int TlgId { get; set; } = 11;
    public S7Int TlgLength { get; set; } = 8;    
    public S7Real Speed { get; set; } = 0f;
}
