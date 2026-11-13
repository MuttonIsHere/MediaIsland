using MediaIsland.Services.Lyrics;
using MediaIsland.Services.Media;
using Xunit;

namespace MediaIsland.Tests.Lyrics;

public class LyricsPlaybackClockTests
{
    private static readonly TimeSpan SongDuration = TimeSpan.FromSeconds(200);

    private static MediaInfo CreateMedia(
        TimeSpan position,
        MediaPlaybackState state,
        string title = "测试歌曲",
        string artist = "测试歌手",
        TimeSpan? duration = null)
    {
        return new MediaInfo(
            "Spotify.exe",
            title,
            artist,
            "测试专辑",
            position,
            duration ?? SongDuration,
            new MediaPlaybackInfo(state),
            null,
            null);
    }

    [Fact]
    public void Update_WhenResumingFromPauseWithZeroPosition_KeepsPreviousProgress()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(90), MediaPlaybackState.Playing));
        clock.Update(CreateMedia(TimeSpan.FromSeconds(90), MediaPlaybackState.Paused));

        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing));

        Assert.Equal(LyricsClockTransition.PositionRestored, clock.LastTransition);
        var position = clock.GetCurrentPosition();
        Assert.True(position >= TimeSpan.FromSeconds(89), $"position={position}");
        Assert.True(position < TimeSpan.FromSeconds(95), $"position={position}");
    }

    [Fact]
    public void Update_WhenPausingWithStaleZeroTimeline_KeepsProgress()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(60), MediaPlaybackState.Playing));

        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Paused));

        Assert.Equal(LyricsClockTransition.PositionRestored, clock.LastTransition);
        Assert.True(clock.GetCurrentPosition() >= TimeSpan.FromSeconds(59));
    }

    [Fact]
    public void Update_WhenLoopRestartsFromEndOfTrack_ReportsRestart()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(199), MediaPlaybackState.Playing));

        clock.Update(CreateMedia(TimeSpan.FromMilliseconds(500), MediaPlaybackState.Playing));

        Assert.Equal(LyricsClockTransition.RestartDetected, clock.LastTransition);
        Assert.True(clock.GetCurrentPosition() < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetCurrentPosition_WrapsAtDuration_WithoutFurtherEvents()
    {
        var clock = new LyricsPlaybackClock();
        var media = new MediaInfo(
            "app",
            "循环歌曲",
            "歌手",
            null,
            TimeSpan.FromSeconds(29),
            TimeSpan.FromSeconds(30),
            new MediaPlaybackInfo(MediaPlaybackState.Playing),
            null,
            null);
        clock.Update(media);

        Thread.Sleep(2500);
        var position = clock.GetCurrentPosition();

        Assert.True(position < TimeSpan.FromSeconds(5), $"position={position}");
    }

    [Fact]
    public void Update_WhenTrackChanges_RebasesToNewTrack()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(150), MediaPlaybackState.Playing, title: "歌A"));

        clock.Update(CreateMedia(TimeSpan.FromSeconds(1), MediaPlaybackState.Playing, title: "歌B"));

        Assert.Equal(LyricsClockTransition.TrackChanged, clock.LastTransition);
        Assert.True(clock.GetCurrentPosition() < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Update_WhenResumingWithCorrectPosition_DoesNotRestore()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(90), MediaPlaybackState.Playing));
        clock.Update(CreateMedia(TimeSpan.FromSeconds(90), MediaPlaybackState.Paused));

        clock.Update(CreateMedia(TimeSpan.FromSeconds(90.2), MediaPlaybackState.Playing));

        Assert.Equal(LyricsClockTransition.None, clock.LastTransition);
        var position = clock.GetCurrentPosition();
        Assert.True(position >= TimeSpan.FromSeconds(89), $"position={position}");
        Assert.True(position < TimeSpan.FromSeconds(92), $"position={position}");
    }

    [Fact]
    public void GetCurrentPosition_DoesNotAdvanceWhilePaused()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(42), MediaPlaybackState.Playing));
        clock.Update(CreateMedia(TimeSpan.FromSeconds(42), MediaPlaybackState.Paused));

        var first = clock.GetCurrentPosition();
        Thread.Sleep(150);
        var second = clock.GetCurrentPosition();

        Assert.Equal(first, second);
        Assert.True(first >= TimeSpan.FromSeconds(41), $"position={first}");
    }

    [Fact]
    public void Reset_ClearsProgressAndIdentity()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(120), MediaPlaybackState.Playing));

        clock.Reset();

        Assert.Equal(TimeSpan.Zero, clock.GetCurrentPosition());
        Assert.False(clock.IsPlaying);
        Assert.Equal(LyricsClockTransition.None, clock.LastTransition);
    }

    [Fact]
    public void Update_StaleZeroPositionWhilePlaying_RunsOnLocalTimerWithoutRestart()
    {
        // 网易云场景：时长 0、位置恒为 0，时间轴完全不更新。
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));

        var restarts = 0;
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(250);
            clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
            if (clock.LastTransition == LyricsClockTransition.RestartDetected) restarts++;
        }

        Assert.Equal(0, restarts);
        Assert.True(clock.IsEstimatingPosition);
        Assert.True(clock.GetCurrentPosition() > TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Update_BlindMode_FreezesWhenPausedAndResumes()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(200);
            clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        }

        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Paused, duration: TimeSpan.Zero));
        var frozen = clock.GetCurrentPosition();
        Thread.Sleep(200);
        Assert.Equal(frozen, clock.GetCurrentPosition());

        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        Thread.Sleep(200);
        Assert.True(clock.GetCurrentPosition() > frozen);
    }

    [Fact]
    public void Update_BlindMode_ReanchorsWhenRealPositionAppears()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(150);
            clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        }

        clock.Update(CreateMedia(TimeSpan.FromSeconds(42), MediaPlaybackState.Playing, duration: TimeSpan.Zero));

        Assert.False(clock.IsEstimatingPosition);
        var position = clock.GetCurrentPosition();
        Assert.True(position >= TimeSpan.FromSeconds(41), $"position={position}");
        Assert.True(position < TimeSpan.FromSeconds(44), $"position={position}");
    }

    [Fact]
    public void Update_TrackChangeWhileBlind_ResetsProgressAndMode()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        Thread.Sleep(200);
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));

        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, title: "另一首歌", duration: TimeSpan.Zero));

        Assert.Equal(LyricsClockTransition.TrackChanged, clock.LastTransition);
        Assert.False(clock.IsEstimatingPosition);
        Assert.True(clock.GetCurrentPosition() < TimeSpan.FromSeconds(1));
    }
    [Fact]
    public void Reanchor_MovesPositionForwardWithoutRestartingLyrics()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(10), MediaPlaybackState.Playing));
        Thread.Sleep(150);

        var delta = clock.Reanchor(TimeSpan.FromSeconds(120));

        Assert.True(delta > TimeSpan.FromSeconds(100), $"delta={delta}");
        Assert.Equal(LyricsClockTransition.None, clock.LastTransition);
        var position = clock.GetCurrentPosition();
        Assert.True(position >= TimeSpan.FromSeconds(119), $"position={position}");
        Assert.True(position < TimeSpan.FromSeconds(122), $"position={position}");
    }

    [Fact]
    public void Reanchor_AfterStuckZeroTimeline_DoesNotReportRestart()
    {
        var clock = new LyricsPlaybackClock();
        // 网易云真实情况：SMTC 时间轴一直是 0，时钟进入盲跑。
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        Thread.Sleep(120);
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));
        clock.Reanchor(TimeSpan.FromSeconds(90));

        // 播放器仍在上报卡住的 0，绝不能因为重锚后再上报 0 就判定"循环重启"。
        clock.Update(CreateMedia(TimeSpan.Zero, MediaPlaybackState.Playing, duration: TimeSpan.Zero));

        Assert.NotEqual(LyricsClockTransition.RestartDetected, clock.LastTransition);
        var position = clock.GetCurrentPosition();
        Assert.True(position >= TimeSpan.FromSeconds(89), $"position={position}");
    }

    [Fact]
    public void Reanchor_ClampsToContentEnd()
    {
        var clock = new LyricsPlaybackClock();
        clock.SetContentEnd(TimeSpan.FromSeconds(150));
        clock.Update(CreateMedia(TimeSpan.FromSeconds(20), MediaPlaybackState.Playing));

        clock.Reanchor(TimeSpan.FromSeconds(500));

        var position = clock.GetCurrentPosition();
        Assert.True(position <= TimeSpan.FromSeconds(151), $"position={position}");
    }

    [Fact]
    public void Reanchor_KeepsLocalTimerRunningAfterwards()
    {
        var clock = new LyricsPlaybackClock();
        clock.Update(CreateMedia(TimeSpan.FromSeconds(5), MediaPlaybackState.Playing));
        clock.Reanchor(TimeSpan.FromSeconds(60));
        Thread.Sleep(250);

        var position = clock.GetCurrentPosition();
        Assert.True(position > TimeSpan.FromSeconds(60.1), $"position={position}");
    }
}
