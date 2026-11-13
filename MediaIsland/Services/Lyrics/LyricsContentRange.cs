using MediaIsland.Services.Lyrics.Models;

namespace MediaIsland.Services.Lyrics;

/// <summary>
/// 歌词内容的时间范围计算与越界守卫。
///
/// LRC 歌词可能不完整（例如歌 3 分钟、最后一句只有 1 分钟），
/// 播放位置越过最后一句后必须停留在最后一句，不能因为越界把歌词"重来"回第一句。
///
/// 注意：不能直接用最后一句的 <see cref="LyricsLine.EndTime"/> 判断内容结束——
/// <see cref="LyricsDocumentNormalizer"/> 会用 metadata.Duration 把末句 EndTime 补齐到曲尾，
/// 这里取"EndTime 与 StartTime 的较大值"，对未补齐的文档也能给出正确边界。
/// </summary>
public static class LyricsContentRange
{
    /// <summary>
    /// 歌词内容的结束时间（最后一句的结束时间戳）。无有效行时返回 <see cref="TimeSpan.Zero"/>。
    /// </summary>
    public static TimeSpan GetContentEnd(LyricsDocument lyrics)
    {
        var end = TimeSpan.Zero;
        foreach (var line in lyrics.Lines)
        {
            var lineEnd = line.EndTime > line.StartTime ? line.EndTime : line.StartTime;
            if (lineEnd > end)
            {
                end = lineEnd;
            }
        }

        return end;
    }

    /// <summary>
    /// 越界守卫：位置超过歌词末句后夹到末句，让 UI 稳定停留在最后一句。
    /// </summary>
    public static TimeSpan ClampToContent(LyricsDocument lyrics, TimeSpan position)
    {
        var contentEnd = GetContentEnd(lyrics);
        return contentEnd > TimeSpan.Zero && position > contentEnd ? contentEnd : position;
    }
}
