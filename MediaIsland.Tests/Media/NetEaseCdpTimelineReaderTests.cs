using System;
using MediaIsland.Services.Media;
using Xunit;

namespace MediaIsland.Tests.Media;

/// <summary>
/// DevTools 进度文本的解析测试。
///
/// 这些字符串是网易云页面里真实读到的形状：
/// <c>"01:31 / 01:53|96.5%"</c>（当前时间 / 总时长 | 进度百分比），
/// 解析错一位，歌词就会整体偏几十秒，所以逐条钉住。
/// </summary>
public class NetEaseCdpTimelineReaderTests
{
    [Fact]
    public void TryParseProgress_WithRealPayload_ReadsPositionAndDuration()
    {
        var ok = NetEaseCdpTimelineReader.TryParseProgress(
            "01:31 / 01:53|96.5%", out var timeline, out var reason);

        Assert.True(ok, reason);
        Assert.Equal(TimeSpan.FromSeconds(91), timeline.Position);
        Assert.Equal(TimeSpan.FromSeconds(113), timeline.Duration);
        Assert.True(timeline.HasExactTime);
        Assert.InRange(timeline.Ratio, 0.960, 0.970);
    }

    [Fact]
    public void TryParseProgress_WithLongTrack_ParsesHourComponent()
    {
        var ok = NetEaseCdpTimelineReader.TryParseProgress(
            "1:02:03 / 1:30:00|68.9%", out var timeline, out var reason);

        Assert.True(ok, reason);
        Assert.Equal(new TimeSpan(1, 2, 3), timeline.Position);
        Assert.Equal(new TimeSpan(1, 30, 0), timeline.Duration);
        Assert.True(timeline.HasExactTime);
    }

    [Fact]
    public void TryParseProgress_WithPercentOnly_StillReturnsRatio()
    {
        var ok = NetEaseCdpTimelineReader.TryParseProgress("|34.5%", out var timeline, out var reason);

        Assert.True(ok, reason);
        Assert.False(timeline.HasExactTime);
        Assert.Equal(TimeSpan.Zero, timeline.Duration);
        Assert.InRange(timeline.Ratio, 0.344, 0.346);
    }

    [Fact]
    public void TryParseProgress_WithoutProgressBar_Fails()
    {
        Assert.False(NetEaseCdpTimelineReader.TryParseProgress(string.Empty, out _, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));

        Assert.False(NetEaseCdpTimelineReader.TryParseProgress("|", out _, out _));
        Assert.False(NetEaseCdpTimelineReader.TryParseProgress("垃圾数据", out _, out _));
    }

    [Fact]
    public void TryParseProgress_WhenClockTextIsBroken_FallsBackToPercent()
    {
        var ok = NetEaseCdpTimelineReader.TryParseProgress("abc / def|12.5%", out var timeline, out _);

        Assert.True(ok);
        Assert.False(timeline.HasExactTime);
        Assert.InRange(timeline.Ratio, 0.124, 0.126);
    }

    [Fact]
    public void ParsePercent_HandlesPercentSignAndRawFraction()
    {
        Assert.InRange(NetEaseCdpTimelineReader.ParsePercent("98.4988%"), 0.98498, 0.98499);
        Assert.InRange(NetEaseCdpTimelineReader.ParsePercent("0.5"), 0.005, 0.006);
        Assert.True(double.IsNaN(NetEaseCdpTimelineReader.ParsePercent(string.Empty)));
        Assert.True(double.IsNaN(NetEaseCdpTimelineReader.ParsePercent("abc")));
    }

    [Fact]
    public void TryParseClock_ParsesCommonShapes()
    {
        Assert.True(NetEaseCdpTimelineReader.TryParseClock("00:00", out var zero));
        Assert.Equal(TimeSpan.Zero, zero);

        Assert.True(NetEaseCdpTimelineReader.TryParseClock("05:14", out var five));
        Assert.Equal(TimeSpan.FromSeconds(314), five);

        Assert.True(NetEaseCdpTimelineReader.TryParseClock("1:02:03", out var hour));
        Assert.Equal(new TimeSpan(1, 2, 3), hour);

        Assert.False(NetEaseCdpTimelineReader.TryParseClock("", out _));
        Assert.False(NetEaseCdpTimelineReader.TryParseClock("9", out _));
        Assert.False(NetEaseCdpTimelineReader.TryParseClock("aa:bb", out _));
    }

    [Fact]
    public void TryParseProgress_AtTrackStart_ReportsZeroPosition()
    {
        var ok = NetEaseCdpTimelineReader.TryParseProgress(
            "00:00 / 04:12|0%", out var timeline, out var reason);

        Assert.True(ok, reason);
        Assert.Equal(TimeSpan.Zero, timeline.Position);
        Assert.Equal(TimeSpan.FromSeconds(252), timeline.Duration);
        Assert.True(timeline.HasExactTime);
        Assert.Equal(0d, timeline.Ratio);
    }
}
