using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Telegrams;

public class TelegramRegistryTests
{
    private static readonly string SampleJson = """
        {
          "Id": "MachineStatus",
          "Name": "Machine Status Telegram",
          "Fields": [
            { "Name": "MachineId", "DataType": "Word" },
            { "Name": "Speed", "DataType": "Real" },
            { "Name": "Alarm", "DataType": "Bool" }
          ]
        }
        """;

    private static readonly string SampleArrayJson = """
        [
          {
            "Id": "TelegramA",
            "Name": "Telegram A",
            "Fields": [ { "Name": "Value", "DataType": "Int" } ]
          },
          {
            "Id": "TelegramB",
            "Name": "Telegram B",
            "Fields": [ { "Name": "Flag", "DataType": "Bool" } ]
          }
        ]
        """;

    [Fact]
    public void LoadFromJson_RegistersSingleDefinition()
    {
        var registry = new TelegramRegistry();
        registry.LoadFromJson(SampleJson);
        registry.Definitions.Should().HaveCount(1);
        var def = registry.Get("MachineStatus");
        def.Fields.Should().HaveCount(3);
    }

    [Fact]
    public void LoadFromJson_RegistersArrayOfDefinitions()
    {
        var registry = new TelegramRegistry();
        registry.LoadFromJson(SampleArrayJson);
        registry.Definitions.Should().HaveCount(2);
        registry.Get("TelegramA").Should().NotBeNull();
        registry.Get("TelegramB").Should().NotBeNull();
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
    public void LoadFromDirectory_LoadsAllJsonFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "t1.json"), SampleJson.Replace("MachineStatus", "TS1"));
            File.WriteAllText(Path.Combine(dir, "t2.json"), """
                { "Id": "TS2", "Name": "T2", "Fields": [] }
                """);
            var registry = new TelegramRegistry();
            registry.LoadFromDirectory(dir);
            registry.Definitions.Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
