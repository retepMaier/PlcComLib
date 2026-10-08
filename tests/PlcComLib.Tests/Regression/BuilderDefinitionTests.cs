using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Tcp;
using PlcComLib.Udp;

namespace PlcComLib.Tests.Regression;

/// <summary>Bugs 3, 4 and 5: selector offsets, WithLength validation and per-connection definitions.</summary>
public class BuilderDefinitionTests
{
    [Fact]
    public void Selector_UsesPlcOffset_NotSumOfFieldSizes()
    {
        var builder = new TcpPlcClientBuilder()
            .RegisterTelegram<PaddedIdTelegram>()
                .WithMessageId(5, (PaddedIdTelegram t) => t.TlgId);

        var def = builder.Registry.Get(nameof(PaddedIdTelegram));
        def.MessageIdByteOffset.Should().Be(2, "the INT after a BYTE starts at the next even byte");

        // The id is found where the serializer actually writes it.
        var bytes = new PaddedIdTelegram { TlgId = 5 }.Serialize();
        PlcComLib.Framing.TelegramIdFramer.ReadId(bytes, def.MessageIdByteOffset, def.MessageIdDataType).Should().Be(5);
    }

    [Fact]
    public void ReadmeTelegram_IsFourteenBytes()
        => ReadmeMachineStatus.WireSize.Should().Be(14);

    [Fact]
    public void WithLength_RejectsLengthThatDiffersFromTypedWireSize()
    {
        var act = () => new TcpPlcClientBuilder()
            .RegisterTelegram<ReadmeMachineStatus>()
                .WithMessageId(1, (ReadmeMachineStatus t) => t.TlgId)
                .WithLength(12, (ReadmeMachineStatus t) => t.TlgLength);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*14*");
    }

    [Fact]
    public void WithLength_AcceptsTypedWireSize()
    {
        var builder = new TcpPlcClientBuilder()
            .RegisterTelegram<ReadmeMachineStatus>()
                .WithMessageId(1, (ReadmeMachineStatus t) => t.TlgId)
                .WithLength(ReadmeMachineStatus.WireSize, (ReadmeMachineStatus t) => t.TlgLength);

        builder.Registry.Get(nameof(ReadmeMachineStatus)).ConfiguredWireSize.Should().Be(14);
    }

    [Fact]
    public void Registering_DoesNotModifySharedDefinition()
    {
        var before = ReadmeMachineStatus.Definition;
        long originalId = before.MessageId;

        var a = new TcpPlcClientBuilder().RegisterTelegram<ReadmeMachineStatus>()
            .WithMessageId(1, (ReadmeMachineStatus t) => t.TlgId);
        var b = new UdpPlcServerBuilder().RegisterTelegram<ReadmeMachineStatus>()
            .WithMessageId<PlcComLib.Core.PlcTypes.S7DWord>(2, 6);

        a.Registry.Get(nameof(ReadmeMachineStatus)).MessageId.Should().Be(1);
        b.Registry.Get(nameof(ReadmeMachineStatus)).MessageId.Should().Be(2);
        b.Registry.Get(nameof(ReadmeMachineStatus)).MessageIdDataType.Should().Be(S7DataType.DWord);
        ReadmeMachineStatus.Definition.MessageId.Should().Be(originalId);
        ReadmeMachineStatus.Definition.MessageIdDataType.Should().Be(S7DataType.Word);
    }

    [Fact]
    public void Subscribe_ForUnregisteredType_Throws()
    {
        var client = new TcpPlcClientBuilder().Build();
        var act = () => client.Subscribe<ReadmeMachineStatus>(_ => { });
        act.Should().Throw<InvalidOperationException>().WithMessage("*RegisterTelegram<ReadmeMachineStatus>*");
    }
}
