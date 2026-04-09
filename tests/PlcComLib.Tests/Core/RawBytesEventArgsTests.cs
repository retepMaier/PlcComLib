using FluentAssertions;
using PlcComLib.Core;
using PlcComLib.Core.Events;
using PlcComLib.DataTypes;
using PlcComLib.Telegrams;
using Xunit;

namespace PlcComLib.Tests.Core;

public class RawBytesEventArgsTests
{
    [Fact]
    public void Constructor_SetsData_RemoteAddress_Port()
    {
        byte[] data = [0x01, 0x02, 0x03];
        var args = new RawBytesEventArgs(data, "192.168.1.10", 2000);

        args.Data.Should().Equal(data);
        args.RemoteAddress.Should().Be("192.168.1.10");
        args.Port.Should().Be(2000);
    }

    [Fact]
    public void Constructor_DefaultsRemoteAddress_AndPort_ToEmpty()
    {
        var args = new RawBytesEventArgs([0xFF]);
        args.RemoteAddress.Should().Be(string.Empty);
        args.Port.Should().Be(0);
    }

    [Fact]
    public void Constructor_ThrowsOnNullData()
    {
        var act = () => new RawBytesEventArgs(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

public class TelegramReceivedEventArgsTests
{
    private static Telegram MakeTelegram() =>
        new(new TelegramDefinition { Id = "T1", Fields = [] });

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var telegram = MakeTelegram();
        byte[] raw = [0x00, 0x01, 0xAA];
        var args = new TelegramReceivedEventArgs(telegram, raw, "10.0.0.1", 5000);

        args.Telegram.Should().BeSameAs(telegram);
        args.RawPayload.Should().Equal(raw);
        args.RemoteAddress.Should().Be("10.0.0.1");
        args.Port.Should().Be(5000);
    }

    [Fact]
    public void Constructor_DefaultsRemoteAddress_AndPort_ToEmpty()
    {
        var args = new TelegramReceivedEventArgs(MakeTelegram());
        args.RemoteAddress.Should().Be(string.Empty);
        args.Port.Should().Be(0);
    }

    [Fact]
    public void Constructor_NullRawPayload_DefaultsToEmptyArray()
    {
        var args = new TelegramReceivedEventArgs(MakeTelegram(), null);
        args.RawPayload.Should().BeEmpty();
    }
}
