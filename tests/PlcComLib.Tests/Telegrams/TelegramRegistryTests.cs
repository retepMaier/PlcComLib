using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Telegrams;

public class TelegramRegistryTests
{
    private static TelegramDefinition MakeDef(string id, ushort messageId = 0) =>
        new()
        {
            Id        = id,
            MessageId = messageId,
            Fields    = [new TelegramField { Name = "Value", DataType = S7DataType.Word }],
        };

    [Fact]
    public void Register_And_Get_ReturnsDefinition()
    {
        var registry = new TelegramRegistry();
        registry.Register(MakeDef("SensorData"));
        var def = registry.Get("SensorData");
        def.Id.Should().Be("SensorData");
    }

    [Fact]
    public void Register_IsCaseInsensitive()
    {
        var registry = new TelegramRegistry();
        registry.Register(MakeDef("SensorData"));
        registry.Get("sensordata").Id.Should().Be("SensorData");
    }

    [Fact]
    public void Register_Overwrites_ExistingDefinition()
    {
        var registry = new TelegramRegistry();
        registry.Register(MakeDef("Dup"));
        registry.Register(new TelegramDefinition { Id = "Dup", MessageId = 0x0005, Fields = [] });
        registry.Get("Dup").MessageId.Should().Be(0x0005);
    }

    [Fact]
    public void Get_ThrowsForUnknownId()
    {
        var registry = new TelegramRegistry();
        var act = () => registry.Get("Unknown");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void TryGet_ReturnsFalseForUnknownId()
    {
        var registry = new TelegramRegistry();
        registry.TryGet("X", out var def).Should().BeFalse();
        def.Should().BeNull();
    }

    [Fact]
    public void Register_ThrowsForEmptyId()
    {
        var registry = new TelegramRegistry();
        var act = () => registry.Register(new TelegramDefinition { Id = "" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Definitions_ContainsAllRegistered()
    {
        var registry = new TelegramRegistry();
        registry.Register(MakeDef("A", 0x0001));
        registry.Register(MakeDef("B", 0x0002));
        registry.Definitions.Should().HaveCount(2);
        registry.Definitions.Should().Contain(d => d.Id == "A");
        registry.Definitions.Should().Contain(d => d.Id == "B");
    }

    [Fact]
    public void Definition_Stores_MessageId()
    {
        var registry = new TelegramRegistry();
        registry.Register(MakeDef("Typed", messageId: 0x00FF));
        registry.Get("Typed").MessageId.Should().Be(0x00FF);
    }
}
