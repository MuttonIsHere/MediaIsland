using System.IO;

namespace MediaIsland.Helpers;

/// <summary>
/// 把 SMTC 的 AUMID 拆成可用于模糊匹配进程路径的标识变体，并为候选进程打分。
/// </summary>
/// <remarks>
/// 1.0.8.0 靠「全进程扫描 + 变体模糊匹配可执行文件路径」定位播放器，因此
/// <c>cn.toside.music.desktop</c> 这类反向域名式 AUMID 能对上实际进程
/// <c>lx-music-desktop.exe</c>；1.1.0.0 起改为要求 AUMID 去扩展名后恰好等于进程名，
/// 这类应用与多进程结构的网易云音乐都会落空。此处恢复变体匹配，并额外过滤过短变体
/// （如 <c>cn</c> / <c>net</c>）避免误命中无关进程。
/// </remarks>
public static class MediaSourceProcessMatcher
{
    /// <summary>变体最短长度，低于此长度的片段区分度不足，容易误匹配。</summary>
    private const int MinVariantLength = 3;

    private static readonly char[] Separators = ['.', '!', '_'];

    private static readonly string[] NoiseSegments = ["com", "github", "exe"];

    /// <summary>文件名与 AUMID 基名完全一致时的加分，确保优先选中主进程。</summary>
    private const int ExactNameScore = 100;

    /// <summary>拥有主窗口时的加分，主程序通常有窗口，辅助进程通常没有。</summary>
    private const int MainWindowScore = 10;

    /// <summary>
    /// 额外命中一个标识变体时的加分。
    /// </summary>
    /// <remarks>
    /// 只看「最长命中」会栽在共用厂商名的 AUMID 上：<c>Tencent.QQMusic</c> 拆出的
    /// <c>Tencent</c> 与 <c>QQMusic</c> 等长，而微信的安装路径
    /// <c>...\Tencent\WeChat\WeChat.exe</c> 同样含 <c>Tencent</c>，两者同分（7 + 10 窗口），
    /// 谁先被枚举谁赢，于是 QQ 音乐的会话被判给了微信。
    /// 命中段越多说明越贴合这个 AUMID（QQ 音乐路径同时含 Tencent 与 QQMusic），
    /// 因此按命中数量加权，把「只撞上厂商名」的候选压下去。
    /// </remarks>
    private const int AdditionalVariantScore = 15;

    /// <summary>
    /// 生成用于匹配进程路径的标识变体，按长度降序排列（长的更具体，优先匹配）。
    /// </summary>
    public static IReadOnlyList<string> GetIdentifierVariants(string sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return [];
        }

        var variants = new List<string>();
        foreach (var segment in sourceApp.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var variant = segment;
            foreach (var noise in NoiseSegments)
            {
                variant = variant.Replace(noise, string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            variant = variant.Trim();
            if (variant.Length >= MinVariantLength)
            {
                variants.Add(variant);
            }
        }

        // 原始 AUMID 兜底，保证至少有一个变体可用。
        if (sourceApp.Trim().Length >= MinVariantLength)
        {
            variants.Add(sourceApp.Trim());
        }

        return variants
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(variant => variant.Length)
            .ToList();
    }

    /// <summary>
    /// 取 AUMID 的「产品段」——最后一个有区分度的标识段。
    /// </summary>
    /// <remarks>
    /// <c>Tencent.QQMusic</c> 的产品段是 <c>QQMusic</c>，<c>Tencent</c> 只是厂商目录。
    /// 只用厂商段匹配会出事：微信在 <c>D:\Tencent\Weixin\</c>、企业微信在
    /// <c>...\Tencent\WXWork\</c>、连 WorkBuddy 自带的 <c>editor_sdk.exe</c> 都在
    /// <c>...\@tencent\...</c> 下——实测有 18 个进程同时命中 <c>Tencent</c> 且同分，
    /// 播放器不在时，谁先被枚举到谁就成了封面来源，图标因此被别的应用顶掉。
    /// </remarks>
    public static string? GetProductToken(string sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return null;
        }

        var segments = sourceApp
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment =>
            {
                var variant = segment;
                foreach (var noise in NoiseSegments)
                {
                    variant = variant.Replace(noise, string.Empty, StringComparison.OrdinalIgnoreCase);
                }

                return variant.Trim();
            })
            .Where(variant => variant.Length >= MinVariantLength)
            .ToList();

        return segments.Count == 0 ? null : segments[^1];
    }

    /// <summary>候选路径是否命中了产品段；产品段未知时一律放行。</summary>
    public static bool MatchesProductToken(string path, string? productToken)
    {
        if (string.IsNullOrWhiteSpace(productToken))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(path) &&
               path.Contains(productToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 只按进程名打分，不触碰进程路径。用于在昂贵的路径解析前廉价筛选候选。
    /// </summary>
    public static int ScoreProcessName(
        string sourceApp,
        string processName,
        bool hasMainWindow,
        IReadOnlyList<string> variants)
    {
        return ScoreText(sourceApp, processName, processName, hasMainWindow, variants);
    }

    /// <summary>
    /// 为候选进程打分，0 表示不匹配。分数越高越可能是播放器本体。
    /// </summary>
    public static int ScoreCandidate(
        string sourceApp,
        string processPath,
        bool hasMainWindow,
        IReadOnlyList<string> variants)
    {
        return ScoreText(sourceApp, processPath, processPath, hasMainWindow, variants);
    }

    private static int ScoreText(
        string sourceApp,
        string haystack,
        string nameSource,
        bool hasMainWindow,
        IReadOnlyList<string> variants)
    {
        if (string.IsNullOrWhiteSpace(haystack) || variants.Count == 0)
        {
            return 0;
        }

        var matched = variants
            .Where(variant => haystack.Contains(variant, StringComparison.OrdinalIgnoreCase))
            .Select(variant => variant.Length)
            .ToList();
        if (matched.Count == 0)
        {
            return 0;
        }

        var score = matched.Max()
                    + (matched.Count - 1) * AdditionalVariantScore;
        if (hasMainWindow)
        {
            score += MainWindowScore;
        }

        if (IsExactNameMatch(sourceApp, nameSource, variants))
        {
            score += ExactNameScore;
        }

        return score;
    }

    /// <summary>
    /// 进程名与 AUMID 的「产品段」完全一致。
    /// </summary>
    /// <remarks>
    /// 只比 <see cref="Path.GetFileNameWithoutExtension(string)"/> 是不够的：
    /// <c>Tencent.QQMusic</c> 取文件名会得到 <c>Tencent</c>，于是真正的
    /// <c>QQMusic.exe</c> 拿不到这个加分。
    /// <para>
    /// 现实里还有更阴的情况：QQ 音乐内置了微信小程序运行时，
    /// <c>...\AppData\Roaming\Tencent\QQMusic\wmpf\...\WeChatAppEx.exe</c>
    /// 的路径同时含 <c>Tencent</c> 与 <c>QQMusic</c> 两段，纯按命中段数甚至能压过本体。
    /// 因此这里以「进程名 == 某段标识」为最高优先级，把辅助进程挡在外面。
    /// </para>
    /// </remarks>
    private static bool IsExactNameMatch(
        string sourceApp,
        string nameSource,
        IReadOnlyList<string> variants)
    {
        var processName = Path.GetFileNameWithoutExtension(nameSource);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        if (string.Equals(
                Path.GetFileNameWithoutExtension(sourceApp),
                processName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return variants.Any(variant =>
            !variant.Contains('.', StringComparison.Ordinal) &&
            string.Equals(variant, processName, StringComparison.OrdinalIgnoreCase));
    }
}
