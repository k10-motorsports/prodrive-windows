using System;
using FluentAssertions;
using RaceCorProDrive.Services.Moza;
using Xunit;

namespace RaceCorProDrive.Tests.Moza;

public class MozaPacketBuilderTests
{
    // ── Read packet construction ───────────────────────────────────

    [Fact]
    public void BuildReadPacket_SingleByteCommand_ProducesExpectedFrame()
    {
        // Wheelbase (0x13), FfbStrength (0x14):
        // pre-checksum = 7E 04 21 13 14, sum = 126+4+33+19+20 = 202
        // checksum = (202 + 13) % 256 = 215 = 0xD7
        var packet = MozaPacketBuilder.BuildReadPacket(0x13, 0x14);
        packet.Should().Equal(new byte[] { 0x7E, 0x04, 0x21, 0x13, 0x14, 0xD7 });
    }

    [Fact]
    public void BuildReadPacket_MultiByteCommand_LengthAccountsForEveryByte()
    {
        var packet = MozaPacketBuilder.BuildReadPacket(0x13, new byte[] { 0x01, 0x02 });
        // length = group(1) + device(1) + cmd(2) + checksum(1) = 5
        packet[0].Should().Be(MozaPacketBuilder.StartByte);
        packet[1].Should().Be(0x05);
        packet[2].Should().Be(MozaPacketBuilder.GroupRead);
        packet[3].Should().Be(0x13);
        packet[4].Should().Be(0x01);
        packet[5].Should().Be(0x02);
        packet.Length.Should().Be(7); // start + length + 5 body bytes
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    public void BuildReadPacket_EmptyOrNullCommand_Throws(byte[]? commandId)
    {
        var act = () => MozaPacketBuilder.BuildReadPacket(0x13, commandId!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildReadPacket_LengthOverflow_Throws()
    {
        // 9-byte command → length = 1+1+9+1 = 12, exceeds max of 11
        var act = () => MozaPacketBuilder.BuildReadPacket(0x13, new byte[9]);
        act.Should().Throw<ArgumentException>();
    }

    // ── Write packet construction ──────────────────────────────────

    [Fact]
    public void BuildWritePacket_SingleByteValue_ProducesExpectedFrame()
    {
        // Wheelbase (0x13), FfbStrength (0x14), value 0x32:
        // pre-checksum = 7E 05 22 13 14 32, sum = 126+5+34+19+20+50 = 254
        // checksum = (254 + 13) % 256 = 267 % 256 = 11 = 0x0B
        var packet = MozaPacketBuilder.BuildWritePacket(0x13, 0x14, (byte)0x32);
        packet.Should().Equal(new byte[] { 0x7E, 0x05, 0x22, 0x13, 0x14, 0x32, 0x0B });
    }

    [Fact]
    public void BuildWritePacket_UInt16Value_IsBigEndian()
    {
        // 500 = 0x01F4 → high byte 0x01, low byte 0xF4
        // pre-checksum = 7E 06 22 13 19 01 F4, sum = 126+6+34+19+25+1+244 = 455
        // checksum = (455 + 13) % 256 = 468 % 256 = 212 = 0xD4
        var packet = MozaPacketBuilder.BuildWritePacket(0x13, 0x19, (ushort)500);
        packet.Should().Equal(new byte[] { 0x7E, 0x06, 0x22, 0x13, 0x19, 0x01, 0xF4, 0xD4 });
    }

    [Fact]
    public void BuildWritePacket_NullPayload_Throws()
    {
        var act = () => MozaPacketBuilder.BuildWritePacket(0x13, new byte[] { 0x14 }, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildWritePacket_LengthOverflow_Throws()
    {
        // 1-byte command + 8-byte payload → length = 12, exceeds max of 11
        var act = () => MozaPacketBuilder.BuildWritePacket(0x13, new byte[] { 0x01 }, new byte[8]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildWritePacket_LengthAtMax_DoesNotThrow()
    {
        // 1-byte command + 7-byte payload → length = 11 (maximum allowed)
        var act = () => MozaPacketBuilder.BuildWritePacket(0x13, new byte[] { 0x01 }, new byte[7]);
        act.Should().NotThrow();
    }

    // ── Checksum ───────────────────────────────────────────────────

    [Fact]
    public void CalculateChecksum_AppliesMagicConstant()
    {
        // The magic byte (13) is THE bit most likely to silently regress —
        // a wrong checksum means devices accept nothing without erroring.
        var sumOfBytes = new byte[] { 0x01, 0x02, 0x03 };
        // expected = (1 + 2 + 3 + 13) % 256 = 19
        MozaPacketBuilder.CalculateChecksum(sumOfBytes).Should().Be(19);
    }

    [Fact]
    public void CalculateChecksum_WrapsAt256()
    {
        var bytes = new byte[] { 0xFF, 0xFF };
        // (255 + 255 + 13) % 256 = 523 % 256 = 11
        MozaPacketBuilder.CalculateChecksum(bytes).Should().Be(11);
    }

    [Fact]
    public void CalculateChecksum_EmptyInput_ReturnsMagic()
    {
        MozaPacketBuilder.CalculateChecksum(Array.Empty<byte>())
            .Should().Be(MozaPacketBuilder.ChecksumMagic);
    }

    // ── Big-endian helpers ─────────────────────────────────────────

    [Theory]
    [InlineData((ushort)0, new byte[] { 0x00, 0x00 })]
    [InlineData((ushort)1, new byte[] { 0x00, 0x01 })]
    [InlineData((ushort)256, new byte[] { 0x01, 0x00 })]
    [InlineData((ushort)500, new byte[] { 0x01, 0xF4 })]
    [InlineData((ushort)65535, new byte[] { 0xFF, 0xFF })]
    public void ToBigEndian16_ProducesCorrectBytes(ushort value, byte[] expected)
    {
        MozaPacketBuilder.ToBigEndian16(value).Should().Equal(expected);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)500)]
    [InlineData((ushort)1080)]   // wheelbase angle limit
    [InlineData((ushort)65535)]
    public void BigEndian16_RoundTrips(ushort value)
    {
        var bytes = MozaPacketBuilder.ToBigEndian16(value);
        MozaPacketBuilder.FromBigEndian16(bytes).Should().Be(value);
    }

    [Fact]
    public void FromBigEndian16_RespectsOffset()
    {
        var bytes = new byte[] { 0xAA, 0x01, 0xF4, 0xBB };
        MozaPacketBuilder.FromBigEndian16(bytes, 1).Should().Be(500);
    }

    [Theory]
    [InlineData(new byte[] { 0x01 }, 0)]
    [InlineData(new byte[] { 0x01, 0x02 }, 1)]
    [InlineData(new byte[0], 0)]
    public void FromBigEndian16_ShortBuffer_Throws(byte[] data, int offset)
    {
        var act = () => MozaPacketBuilder.FromBigEndian16(data, offset);
        act.Should().Throw<ArgumentException>();
    }
}
