using FluentAssertions;
using PlcComLib.Core.PlcTypes;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Telegrams;

public class TelegramSerializerTests
{
    private static TelegramDefinition BuildDefinition() => new()
    {
        Id = "TestTelegram",
        Name = "Test",
        Fields =
        [
            new TelegramField { Name = "Status", DataType = S7DataType.Word },
            new TelegramField { Name = "Temperature", DataType = S7DataType.Real },
            new TelegramField { Name = "Label", DataType = S7DataType.S7String, MaxStringLength = 10 }
        ]
    };

    [Fact]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var def = BuildDefinition();
        var telegram = new Telegram(def);
        telegram.SetValue("Status", (ushort)42);
        telegram.SetValue("Temperature", 23.5f);
        telegram.SetValue("Label", new S7String("Hello", 10));

        var bytes = TelegramSerializer.Serialize(telegram);
        bytes.Length.Should().Be(def.TotalWireSize);

        var restored = TelegramSerializer.Deserialize(def, bytes);
        restored.GetValue<ushort>("Status").Should().Be(42);
        ((float)restored.GetValue("Temperature")).Should().BeApproximately(23.5f, 0.001f);
        restored.GetValue<S7String>("Label").Value.Should().Be("Hello");
    }

    [Fact]
    public void TotalWireSize_IsCorrect()
    {
        var def = BuildDefinition();
        def.TotalWireSize.Should().Be(18);
    }

    [Fact]
    public void Deserialize_ThrowsOnShortBuffer()
    {
        var def = BuildDefinition();
        var act = () => TelegramSerializer.Deserialize(def, new byte[5]);
        act.Should().Throw<InvalidDataException>();
    }
}
