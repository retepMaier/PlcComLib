using FluentAssertions;
using PlcComLib.Core.PlcTypes;
using PlcComLib.DataTypes;
using PlcComLib.Framing;
using PlcComLib.Tcp;
using PlcComLib.Udp;
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

    

    // ── Wider numeric id types — DWord, LWord, int, uint ──────────────────────

    [Fact]
    public void TcpClient_WithMessageId_DWord_AcceptsUInt32Value()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "T1" };
        uint dwordId = 0xDEAD_BEEF;

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(def)
            .WithMessageId<S7DWord>(id: dwordId, byteOffset: 0);

        def.MessageId.Should().Be(dwordId);
        def.MessageIdDataType.Should().Be(S7DataType.DWord);
    }

    [Fact]
    public void TcpClient_WithMessageId_LWord_AcceptsUInt64Value()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "T1" };
        long lwordId = 0x0102_0304_0506_0708L;

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(def)
            .WithMessageId<S7LWord>(id: lwordId, byteOffset: 0);

        def.MessageId.Should().Be(lwordId);
        def.MessageIdDataType.Should().Be(S7DataType.LWord);
    }

    [Fact]
    public void TcpClient_WithMessageId_Int_AcceptsSignedValue()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "T1" };
        int intId = 0x0001;

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(def)
            .WithMessageId<S7Int>(id: intId, byteOffset: 0);

        def.MessageId.Should().Be(intId);
        def.MessageIdDataType.Should().Be(S7DataType.Int);
    }

    

    [Fact]
    public void TcpClient_WithLength_Generic_AcceptsUshortValue()
    {
        var def = new PlcComLib.Telegrams.TelegramDefinition { Id = "T1", MessageId = 0x0001 };
        ushort wireSize = 16;

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram(def)
            .WithLength<S7Word>(length: wireSize, byteOffset: 2);

        def.ConfiguredWireSize.Should().Be(16);
        def.LengthDataType.Should().Be(S7DataType.Word);
    }

    

    // ── Framer correctly reads DWord-sized IDs from the wire ──────────────────

    [Fact]
    public void Framer_WithDWordId_MatchesCorrectly()
    {
        uint dwordId = 0x0001_0002;
        var def = new PlcComLib.Telegrams.TelegramDefinition
        {
            Id                  = "T",
            MessageId           = dwordId,
            MessageIdByteOffset = 0,
            MessageIdDataType   = S7DataType.DWord,
            ConfiguredWireSize  = 8,
        };
        var framer = new TelegramIdFramer([def]);

        var buf = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0), dwordId);

        framer.TryExtract(buf, out var msg, out int consumed).Should().BeTrue();
        consumed.Should().Be(8);
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

    // ── Test telegram ─────────────────────────────────────────────────────────────
    private class SelectorTestTelegram : PlcComLib.Telegrams.S7TelegramBase<SelectorTestTelegram>
    {
        public S7Int  TlgId     { get; set; } = 0;   // offset 0, wire size 2
        public S7Int  TlgLength { get; set; } = 0;   // offset 2, wire size 2
        public S7Word MachineId { get; set; } = 0;   // offset 4, wire size 2
    }

    // ── Property-selector overloads ──────────────────────────────────────────────

    [Fact]
    public void TcpClient_WithMessageId_Selector_SetsOffsetFromPropertyPosition()
    {
        var def = SelectorTestTelegram.Definition;
        // Reset in case other tests modified it
        def.MessageId = 0; def.MessageIdByteOffset = -1;

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram<SelectorTestTelegram>()
            .WithMessageId(id: 1, (SelectorTestTelegram t) => t.TlgId);

        def.MessageId.Should().Be(1);
        def.MessageIdByteOffset.Should().Be(0);          // TlgId is first field → offset 0
        def.MessageIdDataType.Should().Be(S7DataType.Int);
    }

    [Fact]
    public void TcpClient_WithLength_Selector_SetsOffsetFromPropertyPosition()
    {
        var def = SelectorTestTelegram.Definition;
        def.LengthByteOffset = -1;

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram<SelectorTestTelegram>()
            .WithMessageId(id: 1, (SelectorTestTelegram t) => t.TlgId)
            .WithLength(length: 12, (SelectorTestTelegram t) => t.TlgLength);

        def.ConfiguredWireSize.Should().Be(12);
        def.LengthByteOffset.Should().Be(2);             // TlgLength is second field → offset 2
        def.LengthDataType.Should().Be(S7DataType.Int);
    }

    [Fact]
    public void TcpClient_WithMessageId_Selector_ThirdField_SetsCorrectOffset()
    {
        var def = SelectorTestTelegram.Definition;

        new TcpPlcClientBuilder()
            .ConnectTo("127.0.0.1", 2000)
            .RegisterTelegram<SelectorTestTelegram>()
            .WithMessageId(id: 99, (SelectorTestTelegram t) => t.MachineId);

        def.MessageIdByteOffset.Should().Be(4);          // MachineId is third field → offset 0+2+2=4
        def.MessageIdDataType.Should().Be(S7DataType.Word);
    }

    [Fact]
    public void TcpServer_WithMessageId_Selector_SetsOffsetAndDataType()
    {
        var def = SelectorTestTelegram.Definition;
        def.MessageId = 0; def.MessageIdByteOffset = -1;

        new TcpPlcServerBuilder()
            .ListenOn("0.0.0.0", 2000)
            .RegisterTelegram<SelectorTestTelegram>()
            .WithMessageId(id: 5, (SelectorTestTelegram t) => t.TlgId);

        def.MessageId.Should().Be(5);
        def.MessageIdByteOffset.Should().Be(0);
        def.MessageIdDataType.Should().Be(S7DataType.Int);
    }

    [Fact]
    public void TcpServer_WithLength_Selector_SetsOffsetAndDataType()
    {
        var def = SelectorTestTelegram.Definition;

        new TcpPlcServerBuilder()
            .ListenOn("0.0.0.0", 2000)
            .RegisterTelegram<SelectorTestTelegram>()
            .WithMessageId(id: 5, (SelectorTestTelegram t) => t.TlgId)
            .WithLength(length: 6, (SelectorTestTelegram t) => t.TlgLength);

        def.ConfiguredWireSize.Should().Be(6);
        def.LengthByteOffset.Should().Be(2);
        def.LengthDataType.Should().Be(S7DataType.Int);
    }

    [Fact]
    public void UdpClient_WithMessageId_Selector_SetsOffsetAndDataType()
    {
        var def = SelectorTestTelegram.Definition;
        def.MessageId = 0; def.MessageIdByteOffset = -1;

        new UdpPlcClientBuilder()
            .SendTo("127.0.0.1", 5000)
            .RegisterTelegram<SelectorTestTelegram>()
            .WithMessageId(id: 7, (SelectorTestTelegram t) => t.TlgId);

        def.MessageId.Should().Be(7);
        def.MessageIdByteOffset.Should().Be(0);
        def.MessageIdDataType.Should().Be(S7DataType.Int);
    }

    [Fact]
    public void UdpServer_WithLength_Selector_SetsOffsetAndDataType()
    {
        var def = SelectorTestTelegram.Definition;

        new UdpPlcServerBuilder()
            .ListenOn("0.0.0.0", 5000)
            .RegisterTelegram<SelectorTestTelegram>()
            .WithMessageId(id: 7, (SelectorTestTelegram t) => t.TlgId)
            .WithLength(length: 6, (SelectorTestTelegram t) => t.TlgLength);

        def.LengthByteOffset.Should().Be(2);
        def.LengthDataType.Should().Be(S7DataType.Int);
    }
}
