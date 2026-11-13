using System.Diagnostics;
using MediaIsland.Services.Media;

namespace MediaIsland.Services.Lyrics;

/// <summary>
/// 一次时钟更新的判定结果，供歌词组件决定是否需要重置滚动。
/// </summary>
public enum LyricsClockTransition
{
    /// <summary>普通更新，位置可信。</summary>
    None,

    /// <summary>切换到了另一首歌。</summary>
    TrackChanged,

    /// <summary>播放器把位置报回了 0 / 回退了，但歌曲没变（暂停后恢复、时间轴陈旧），已保留原进度。</summary>
    PositionRestored,

    /// <summary>播放中位置从结尾跳回开头（单曲循环重头开始），需要强制重置歌词滚动。</summary>
    RestartDetected
}

/// <summary>
/// 把 SMTC 的播放位置映射成可连续外推的时钟。
///
/// 设计要点：
/// 1) 用 <see cref="Stopwatch"/> 高精度时间戳外推，配合 PlaybackRate 得到毫秒级当前位置；
/// 2) 小幅上报抖动（&lt; 250ms）不重设基准；
/// 3) 暂停/停止后恢复时，播放器把位置报回 0 不能把进度清零；
/// 4) 单曲循环时位置从曲尾跳回开头 → 判定重启；
/// 5) 回绕边界取 max(曲目时长, 歌词末句)，避免歌词不全或时长上报偏短时提前"重来"；
/// 6) **盲跑模式**：某些播放器（实测网易云）经 SMTC 上报的时长恒为 0，
///    或者播放中位置长时间不变。此时彻底抛弃 SMTC 的位置，纯用本地 Stopwatch 推进，
///    直到位置重新开始变化再平滑重锚。
/// </summary>
public sealed class LyricsPlaybackClock
{
    /// <summary>位置比当前外推值回退超过该秒数才视为"异常回退"。</summary>
    private const double BackwardToleranceSeconds = 3.0;

    /// <summary>位置落在曲首该秒数内视为"回到开头"。</summary>
    private const double NearStartSeconds = 1.5;

    /// <summary>上报位置与惯性外推值相差小于该秒数时，视为抖动，不重设基准。</summary>
    private const double RebaseToleranceSeconds = 0.25;

    /// <summary>小于该时长的回绕边界视为不可信（直播、刚好开始的曲目）。</summary>
    private const double MinimumLoopableDurationSeconds = 15.0;

    /// <summary>播放倍速的合理区间，防止异常上报把外推速度带飞。</summary>
    private const double MinimumPlaybackRate = 0.25;

    /// <summary>播放倍速的合理区间上限。</summary>
    private const double MaximumPlaybackRate = 4.0;

    /// <summary>播放中位置保持不变的时长超过该阈值，判定为"时间轴不再更新"，进入盲跑。</summary>
    private const double StalePositionSeconds = 2.0;

    private readonly object _gate = new();
    private TimeSpan _basePosition;
    private long _baseTimestamp = Stopwatch.GetTimestamp();
    private bool _isPlaying;
    private double _playbackRate = 1.0;
    private TimeSpan _duration;
    private TimeSpan _contentEnd;
    private string? _identity;
    private MediaPlaybackState _state = MediaPlaybackState.Unknown;
    private TimeSpan? _lastSmcPosition;
    private long _smcStableSince = Stopwatch.GetTimestamp();
    private bool _blind;

    /// <summary>最近一次 <see cref="Update"/> 的判定结果。</summary>
    public LyricsClockTransition LastTransition { get; private set; }

    /// <summary>当前是否处于盲跑（不使用 SMTC 位置，纯本地计时）。</summary>
    public bool IsEstimatingPosition
    {
        get
        {
            lock (_gate)
            {
                return _blind;
            }
        }
    }

    /// <summary>
    /// 歌词内容真正结束的时间戳（最后一句的结束时间）。用于两件事：
    /// 1) 回绕边界不得早于它，避免歌词不全 / 时长上报偏短时提前"重来"；
    /// 2) 组件可以把渲染位置钉在末句，超界时停在最后一句。
    /// </summary>
    public void SetContentEnd(TimeSpan contentEnd)
    {
        lock (_gate)
        {
            _contentEnd = contentEnd > TimeSpan.Zero ? contentEnd : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// 用外部测量到的真实位置强制重锚（例如网易云进度条的像素比例换算结果）。
    ///
    /// 背景：部分播放器（实测网易云）经 SMTC 上报的时间轴恒为 0，盲跑时钟会持续累积误差；
    /// 拖拽进度条、切歌时更是完全失去参考。此时用一次独立测量把基准拉回真实位置，
    /// 之后仍由本地 Stopwatch 平滑外推。
    /// </summary>
    /// <returns>本次重锚造成的位移，调用方可据此判断是否属于"剧烈跳变"。</returns>
    public TimeSpan Reanchor(TimeSpan position)
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var current = ComputePositionUnlocked();
            var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;

            var boundary = GetWrapBoundaryUnlocked();
            if (boundary > TimeSpan.Zero && target > boundary)
            {
                target = boundary;
            }

            _basePosition = target;
            _baseTimestamp = now;
            // 注意：不要清空 _lastSmcPosition。下一次 SMTC 上报仍可能是卡住的 0，
            // 保持"位置没变"的判定，才不会把它误当成"循环重启"而丢回第一句。
            LastTransition = LyricsClockTransition.None;
            return target - current;
        }
    }

    public void Update(MediaInfo info)
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var identity = BuildIdentity(info);
            var state = info.PlaybackInfo.PlaybackState;
            var incoming = info.Position > TimeSpan.Zero ? info.Position : TimeSpan.Zero;
            var duration = info.Duration > TimeSpan.Zero ? info.Duration : TimeSpan.Zero;
            var lastState = _state;
            var current = ComputePositionUnlocked();
            var sameTrack = _identity is not null && string.Equals(_identity, identity, StringComparison.Ordinal);

            var transition = LyricsClockTransition.None;
            var rebase = incoming;

            if (!sameTrack)
            {
                transition = LyricsClockTransition.TrackChanged;
                _identity = identity;
                _duration = duration;
                _lastSmcPosition = incoming;
                _smcStableSince = now;
                _blind = false;
            }
            else
            {
                if (duration > TimeSpan.Zero)
                {
                    _duration = duration;
                }

                var playing = state == MediaPlaybackState.Playing;
                var positionChanged = !_lastSmcPosition.HasValue || incoming != _lastSmcPosition.Value;
                if (positionChanged)
                {
                    _lastSmcPosition = incoming;
                    _smcStableSince = now;
                    _blind = false;
                }
                else if (playing &&
                         Stopwatch.GetElapsedTime(_smcStableSince).TotalSeconds >= StalePositionSeconds)
                {
                    // 位置一直不变：播放器的时间轴已经不更新了（网易云等），
                    // 此时 SMTC 的 position 不可信，进入盲跑。
                    _blind = true;
                }

                var wasInactive = lastState is MediaPlaybackState.Paused or MediaPlaybackState.Stopped;
                var backward = incoming + TimeSpan.FromSeconds(BackwardToleranceSeconds) < current;
                var nearStart = incoming <= TimeSpan.FromSeconds(NearStartSeconds);
                var drift = incoming - current;
                var smallDrift = Math.Abs(drift.TotalSeconds) < RebaseToleranceSeconds;

                if (playing && !positionChanged)
                {
                    // 盲跑核心：这一次上报没有任何新位置信息，保留当前基准，
                    // 让本地 Stopwatch 继续推进，绝不因为"卡住的 0"把歌词重置。
                    rebase = current;
                }
                else if (playing && wasInactive && (backward || nearStart))
                {
                    // Bug 1：暂停/停止后恢复播放，播放器常把位置报回 0（或报旧快照）。
                    // 同一首歌没有换曲，沿用暂停前已经推进的进度，避免歌词跳回第一句。
                    rebase = current;
                    transition = LyricsClockTransition.PositionRestored;
                }
                else if (!playing && backward && nearStart)
                {
                    // 暂停瞬间时间轴可能是陈旧值（0），同样保留当前进度。
                    rebase = current;
                    transition = LyricsClockTransition.PositionRestored;
                }
                else if (playing && backward && nearStart)
                {
                    // Bug 2：播放中位置从曲尾跳回开头，判定为单曲循环重新开始。
                    transition = LyricsClockTransition.RestartDetected;
                }
                else if (playing && lastState == MediaPlaybackState.Playing && smallDrift)
                {
                    // 需求 3：惯性外推。SMTC 每次上报都有几十到几百毫秒抖动，
                    // 小幅偏差不重设基准，让时钟连续推进而不是每来一次事件就"跳一下"。
                    rebase = current;
                }
            }

            _basePosition = rebase;
            _baseTimestamp = now;
            _isPlaying = state == MediaPlaybackState.Playing;
            _playbackRate = info.PlaybackInfo.PlaybackRate is > 0
                ? Math.Clamp(info.PlaybackInfo.PlaybackRate.Value, MinimumPlaybackRate, MaximumPlaybackRate)
                : 1.0;
            _state = state;
            LastTransition = transition;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _basePosition = TimeSpan.Zero;
            _baseTimestamp = Stopwatch.GetTimestamp();
            _isPlaying = false;
            _playbackRate = 1.0;
            _duration = TimeSpan.Zero;
            _contentEnd = TimeSpan.Zero;
            _identity = null;
            _state = MediaPlaybackState.Unknown;
            _lastSmcPosition = null;
            _smcStableSince = Stopwatch.GetTimestamp();
            _blind = false;
            LastTransition = LyricsClockTransition.None;
        }
    }

    public TimeSpan GetCurrentPosition()
    {
        lock (_gate)
        {
            return ComputePositionUnlocked();
        }
    }

    /// <summary>回绕/夹紧边界：曲目时长与歌词末句时间戳取大值。</summary>
    private TimeSpan GetWrapBoundaryUnlocked()
    {
        // 没有可信的曲目时长（SMTC 常见：网易云/浏览器上报 0）时不做回绕。
        // 否则歌词末句之后的一小段尾奏就会让位置归零，歌词"重来"回第一句。
        // 这类播放器的单曲循环由 RestartDetected（位置跳回开头 + 播放中）负责。
        if (_duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return _contentEnd > _duration ? _contentEnd : _duration;
    }

    private TimeSpan ComputePositionUnlocked()
    {
        if (!_isPlaying)
        {
            return ClampPosition(_basePosition);
        }

        var elapsed = Stopwatch.GetElapsedTime(_baseTimestamp);
        var position = _basePosition + TimeSpan.FromTicks((long)(elapsed.Ticks * _playbackRate));
        var boundary = GetWrapBoundaryUnlocked();
        if (boundary > TimeSpan.FromSeconds(MinimumLoopableDurationSeconds) && position >= boundary)
        {
            // 单曲循环兜底：SMTC 在循环重启时可能完全不发事件，外推位置会一路越过结尾。
            // 这里按边界回绕，让歌词重新从第一句开始滚。
            // 边界取 max(曲目时长, 歌词末句)，因此歌词不全 / 时长偏短时不会提前重来。
            var wrapped = TimeSpan.FromTicks(position.Ticks % boundary.Ticks);
            _basePosition = wrapped;
            _baseTimestamp = Stopwatch.GetTimestamp();
            position = wrapped;
        }

        return ClampPosition(position);
    }

    private TimeSpan ClampPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var boundary = GetWrapBoundaryUnlocked();
        if (boundary > TimeSpan.Zero && position > boundary)
        {
            return boundary;
        }

        return position;
    }

    private static string BuildIdentity(MediaInfo info)
    {
        return string.Concat(
            info.SourceApp,
            "\u0001",
            info.Title,
            "\u0001",
            info.Artist,
            "\u0001",
            info.AlbumTitle);
    }

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _isPlaying;
            }
        }
    }
}
