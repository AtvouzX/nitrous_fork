using System;
using System.Collections.Generic;
using Nitrous.Enums;
using Nitrous.Helpers;
using Xunit;

namespace Nitrous.Tests;

public class AcerEcHidProtocolTests
{
    [Fact]
    public void BuildRequest_ConstructsValidHeaderAndPayload()
    {
        byte[] data = { 0x11, 0x22, 0x33 };
        byte[] request = AcerEcHidProtocol.BuildRequest(0x0001, 0x02, data);

        Assert.Equal(AcerEcHidProtocol.MinimumReportLength, request.Length);
        Assert.Equal(AcerEcHidProtocol.ReportId, request[0]);
        Assert.Equal((byte)(AcerEcHidProtocol.RequestHeader & 0xFF), request[1]);
        Assert.Equal((byte)(AcerEcHidProtocol.RequestHeader >> 8), request[2]);
        Assert.Equal(0x01, request[3]);
        Assert.Equal(0x00, request[4]);
        Assert.Equal(0x02, request[5]);
        Assert.Equal(0x11, request[6]);
        Assert.Equal(0x22, request[7]);
        Assert.Equal(0x33, request[8]);
    }

    [Fact]
    public void DecodeReply_ValidAndInvalidReports()
    {
        // Invalid: too short or wrong report id
        Assert.Null(AcerEcHidProtocol.DecodeReply(new byte[] { 0xA0, 0x01, 0x02 }));
        Assert.Null(AcerEcHidProtocol.DecodeReply(new byte[] { 0x00, 0x00, 0xE0, 0x01, 0x00, 0x00 }));

        // Valid: 0xA0, then status 0xE000, command 0x0001
        var report = new byte[65];
        report[0] = 0xA0;
        report[1] = 0x00;
        report[2] = 0xE0; // Status: 0xE000
        report[3] = 0x01;
        report[4] = 0x00; // Command: 0x0001
        report[5] = 0x55; // Data

        var reply = AcerEcHidProtocol.DecodeReply(report);
        Assert.NotNull(reply);
        Assert.Equal(0xE000, reply.Value.Status);
        Assert.Equal(0x0001, reply.Value.Command);
        Assert.Equal(0x55, reply.Value.Data[4]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(0, 0)]
    public void ModesFor_ReturnsExpectedList(ushort capability, int expectedCount)
    {
        var modes = AcerEcHidProtocol.ModesFor(capability);
        Assert.Equal(expectedCount, modes.Count);
    }

    [Fact]
    public void ValueFor_And_ModeFromValue_Roundtrip()
    {
        var modes = new[] { PowerProfile.Quiet, PowerProfile.Balanced, PowerProfile.Performance, PowerProfile.Turbo };

        // Acer encoding counts down from end of list
        Assert.Equal((byte)3, AcerEcHidProtocol.ValueFor(modes, PowerProfile.Quiet));
        Assert.Equal((byte)2, AcerEcHidProtocol.ValueFor(modes, PowerProfile.Balanced));
        Assert.Equal((byte)1, AcerEcHidProtocol.ValueFor(modes, PowerProfile.Performance));
        Assert.Equal((byte)0, AcerEcHidProtocol.ValueFor(modes, PowerProfile.Turbo));

        Assert.Equal(PowerProfile.Quiet, AcerEcHidProtocol.ModeFromValue(modes, 3));
        Assert.Equal(PowerProfile.Balanced, AcerEcHidProtocol.ModeFromValue(modes, 2));
        Assert.Equal(PowerProfile.Performance, AcerEcHidProtocol.ModeFromValue(modes, 1));
        Assert.Equal(PowerProfile.Turbo, AcerEcHidProtocol.ModeFromValue(modes, 0));
        Assert.Null(AcerEcHidProtocol.ModeFromValue(modes, 4));
    }

    [Fact]
    public void BacklightTimeout_EncodeAndDecode()
    {
        var encoded = AcerEcHidProtocol.EncodeBacklightTimeout(100, 30);
        Assert.Equal(7, encoded.Length);
        Assert.Equal(AcerEcHidProtocol.KeyboardBacklight, encoded[0]);

        // Construct synthetic reply data
        var replyData = new byte[20];
        replyData[5] = AcerEcHidProtocol.KeyboardBacklight;
        replyData[8] = 100;
        replyData[9] = 0;
        replyData[10] = 30;
        replyData[11] = 0;

        var decoded = AcerEcHidProtocol.DecodeBacklightTimeout(replyData);
        Assert.NotNull(decoded);
        Assert.Equal(100, decoded.Value.Brightness);
        Assert.Equal(30, decoded.Value.TimeoutSeconds);
    }
}
