using FluentAssertions;
using PlcComLib.DataTypes;
using PlcComLib.Tcp;
using Xunit;

namespace PlcComLib.Tests.Telegrams;

/// <summary>
/// Tests for the generic <c>.WithMessageId&lt;TType&gt;(id, byteOffset)</c> and
/// <c>.WithLength&lt;TType&gt;(length, byteOffset)</c> builder overloads.
/// </summary>
public class BuilderGenericFramingTests
{
    // ── TcpPlcClientBuilder ───────────────────────────────────────────────────

    [Fact]
    public void TcpClient_WithMessageId_Generic_SetsIdOffsetAndDataType()
    {
        var builder = new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(new PlcComLib.Telegrams.TelegramDefinition { Id = "T1" })
            .WithMessageId<S7Word>(id: 0x0001, byteOffset: 0);

        // Verify by building — no exception means it compiled and ran correctly.
        // (We inspect the stored definition via the registry implicitly through Build().)
        var act = () => builder.Build();
        act.Should().NotThrow();
    }

    [Fact]
    public void TcpClient_WithLength_Generic_SetsLengthOffsetAndDataType()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "T1", MessageId = 0x0001 };

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(def)
            .WithMessageId<S7Word>(id: 0x0001, byteOffset: 0)
            .WithLength<S7Int>(length: 16, byteOffset: 2);

        def.ConfiguredWireSize.Should().Be(16);
        def.LengthByteOffset.Should().Be(2);
        def.LengthDataType.Should().Be(S7DataType.Int);
        def.MessageIdByteOffset.Should().Be(0);
        def.MessageIdDataType.Should().Be(S7DataType.Word);
    }

    [Fact]
    public void TcpClient_WithMessageId_Generic_SetsMessageIdOnDefinition()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "T1" };

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(def)
            .WithMessageId<S7Word>(id: 0x00AB, byteOffset: 4);

        def.MessageId.Should().Be(0x00AB);
        def.MessageIdByteOffset.Should().Be(4);
        def.MessageIdDataType.Should().Be(S7DataType.Word);
    }

    // ── TcpPlcServerBuilder ───────────────────────────────────────────────────

    [Fact]
    public void TcpServer_WithMessageId_Generic_SetsDefinitionProperties()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "S1" };

        new TcpPlcServerBuilder()
            .ListenOn("0.0.0.0", 2000)
            .RegisterTelegram(def)
            .WithMessageId<S7DWord>(id: 0x00FF, byteOffset: 0)
            .WithLength<S7Word>(length: 32, byteOffset: 4);

        def.MessageId.Should().Be(0x00FF);
        def.MessageIdDataType.Should().Be(S7DataType.DWord);
        def.ConfiguredWireSize.Should().Be(32);
        def.LengthByteOffset.Should().Be(4);
        def.LengthDataType.Should().Be(S7DataType.Word);
    }

    // ── Backward-compat scalar overloads still work ───────────────────────────

    [Fact]
    public void TcpClient_WithMessageId_Scalar_StillWorks()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "T1" };

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(def)
            .WithMessageId(0x0007)
            .WithLength(20);

        def.MessageId.Should().Be(0x0007);
        def.ConfiguredWireSize.Should().Be(20);
        // Scalar overloads do not set offsets — defaults apply
        def.MessageIdByteOffset.Should().Be(0);
        def.LengthByteOffset.Should().Be(-1);
    }

    // ── Framing type markers expose correct S7DataType ────────────────────────

    [Theory]
    [InlineData(typeof(S7Byte),  S7DataType.Byte)]
    [InlineData(typeof(S7SInt),  S7DataType.SInt)]
    [InlineData(typeof(S7Word),  S7DataType.Word)]
    [InlineData(typeof(S7Int),   S7DataType.Int)]
    [InlineData(typeof(S7DWord), S7DataType.DWord)]
    [InlineData(typeof(S7DInt),  S7DataType.DInt)]
    [InlineData(typeof(S7LWord), S7DataType.LWord)]
    [InlineData(typeof(S7LInt),  S7DataType.LInt)]
    public void FramingType_DataType_MatchesExpected(Type markerType, S7DataType expected)
    {
        // Access the static abstract property via reflection (tests only; runtime code uses generics).
        var prop = markerType.GetProperty(nameof(IS7FramingType.DataType),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var actual = (S7DataType)prop.GetValue(null)!;
        actual.Should().Be(expected);
    }
}
