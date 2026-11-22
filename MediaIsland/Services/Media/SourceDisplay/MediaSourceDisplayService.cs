using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MediaIsland.Models;
using MediaIsland.Services.Media.Platform;
using Microsoft.Extensions.Logging;

namespace MediaIsland.Services.Media.SourceDisplay;

public sealed class MediaSourceDisplayService(
    Plugin plugin,
    MediaPlatformProviderResolver providerResolver,
    ILogger<MediaSourceDisplayService> logger) : IMediaSourceDisplayService
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNameMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cloudmusic"] = "网易云音乐",
            ["cloudmusic.exe"] = "网易云音乐",
            // QQ 音乐的 SMTC AUMID 是 Tencent.QQMusic。它和微信共用 "Tencent" 这一层
            // 安装目录，进程匹配曾把会话判给 ...\Tencent\WeChat\WeChat.exe，
            // 大屏上于是显示成「微信」。这里显式钉死显示名，不再依赖进程猜测。
            ["Tencent.QQMusic"] = "QQ音乐",
            ["qqmusic.exe"] = "QQ音乐",
            ["cn.toside.music.desktop"] = "LX Music",
            ["net.stevexmh.amllplayer"] = "AMLL Player",
            ["spotify.exe"] = "Spotify",
            ["SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"] = "Spotify",
            ["Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"] = "Media Player",
            ["MSEdge"] = "Microsoft Edge",
            ["msedge.exe"] = "Microsoft Edge",
            ["chrome.exe"] = "Chrome",
            ["foobar2000.exe"] = "foobar2000",
            ["PotPlayerMini64.exe"] = "PotPlayer",
            ["PotPlayerMini.exe"] = "PotPlayer",
            ["vlc.exe"] = "VLC",
            ["MusicBee.exe"] = "MusicBee",
            ["AIMP.exe"] = "AIMP",
            ["kugou.exe"] = "酷狗音乐",
            ["kwmusic.exe"] = "酷我音乐"
        };

    /// <summary>
    /// 标识里出现这些片段就认为是某个已知播放器。
    /// </summary>
    /// <remarks>
    /// AUMID 的形态太杂（反域名、打包应用、可执行文件名），穷举不现实；
    /// 用「包含」规则兜住同一款应用的不同写法，顺序自上而下匹配。
    /// </remarks>
    private static readonly (string Needle, string DisplayName)[] FuzzyNameRules =
    [
        ("qqmusic", "QQ音乐"),
        ("cloudmusic", "网易云音乐"),
        ("spotify", "Spotify"),
        ("kugou", "酷狗音乐"),
        ("kwmusic", "酷我音乐"),
        ("kuwo", "酷我音乐"),
        ("migu", "咪咕音乐"),
        ("applemusic", "Apple Music"),
        ("zunemusic", "Media Player"),
        ("musicbee", "MusicBee"),
        ("foobar", "foobar2000"),
        ("potplayer", "PotPlayer"),
        ("vlc", "VLC"),
        ("aimp", "AIMP"),
        ("amllplayer", "AMLL Player"),
        ("lx-music", "LX Music"),
        ("lxmusic", "LX Music"),
        ("bilibili", "哔哩哔哩"),
        ("youtube", "YouTube"),
        ("msedge", "Microsoft Edge"),
        ("wechat", "微信"),
        ("weixin", "微信")
    ];

    private static readonly IReadOnlyDictionary<string, Uri> BundledIconMap =
        new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase)
        {
            ["cn.toside.music.desktop"] = new("avares://MediaIsland/Assets/SourceAppIcons/cn.toside.music.desktop.png"),
            ["net.stevexmh.amllplayer"] = new("avares://MediaIsland/Assets/SourceAppIcons/net.stevexmh.amllplayer.png")
        };

    private readonly ConcurrentDictionary<CacheKey, Task<MediaSourceDisplayInfo>> _cache = new();

    public Task<MediaSourceDisplayInfo> ResolveAsync(
        string sourceApp,
        CancellationToken cancellationToken = default)
    {
        var source = FindMediaSource(sourceApp);
        var iconPath = source?.IconPath;
        var customDisplayName = source?.CustomDisplayName;
        var cacheKey = new CacheKey(sourceApp, iconPath, customDisplayName);
        return _cache.GetOrAdd(
            cacheKey,
            _ => ResolveCoreAsync(sourceApp, iconPath, customDisplayName, cancellationToken));
    }

    public void Invalidate(string sourceApp)
    {
        foreach (var key in _cache.Keys.Where(key => string.Equals(key.SourceApp, sourceApp, StringComparison.OrdinalIgnoreCase)))
        {
            _cache.TryRemove(key, out _);
        }
    }

    private async Task<MediaSourceDisplayInfo> ResolveCoreAsync(
        string sourceApp,
        string? iconPath,
        string? customDisplayName,
        CancellationToken cancellationToken)
    {
        MediaSourceDisplayInfo info;
        try
        {
            info = await ResolveCoreInnerAsync(sourceApp, iconPath, customDisplayName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 这个 Task 会被塞进 _cache；一旦带异常留在字典里，之后每次 await 都会重新抛，
            // 该播放源就永久变成错误态。这里吞掉并回退成能显示的最基本信息。
            logger.LogWarning(ex, "[媒体] 解析播放源信息失败，回退为基础信息：{SourceApp}", sourceApp);
            info = new MediaSourceDisplayInfo(
                sourceApp,
                MediaSourceDisplayNamePrettifier.Prettify(sourceApp),
                null,
                MediaSourceDisplayKind.Unknown);
        }

        var normalized = MediaSourceDisplayNamePrettifier.Normalize(info.DisplayName, sourceApp);
        if (!string.Equals(normalized, info.DisplayName, StringComparison.Ordinal))
        {
            info = info with { DisplayName = normalized };
        }

        // 排查「来源显示成别的应用」时，这一行是唯一能对上号的证据：
        // AUMID 是 Windows 给的原值，显示名是本插件最终画到组件上的东西。
        logger.LogInformation(
            "[媒体] 来源解析：AUMID={SourceApp} 显示名={DisplayName} 类型={Kind} 图标={HasIcon}",
            sourceApp,
            info.DisplayName,
            info.Kind,
            info.Icon != null);
        return info;
    }

    private async Task<MediaSourceDisplayInfo> ResolveCoreInnerAsync(
        string sourceApp,
        string? iconPath,
        string? customDisplayName,
        CancellationToken cancellationToken)
    {
        var mapping = ResolveMappedDisplayName(sourceApp);
        var displayName = MediaSourceDisplayNameResolver.Resolve(mapping, customDisplayName);

        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var userIcon = LoadFileIcon(iconPath);
            if (userIcon != null)
            {
                return new MediaSourceDisplayInfo(sourceApp, displayName, userIcon, MediaSourceDisplayKind.UserConfigured);
            }
        }

        var platformInfo = await ResolvePlatformInfoAsync(sourceApp, cancellationToken);
        displayName = MediaSourceDisplayNameResolver.Resolve(
            mapping,
            customDisplayName,
            platformInfo?.DisplayName);
        if (platformInfo is { Icon: not null })
        {
            return new MediaSourceDisplayInfo(
                sourceApp,
                displayName,
                platformInfo.Icon,
                MediaSourceDisplayKind.Platform);
        }

        var bundledIcon = LoadBundledIcon(sourceApp);
        if (bundledIcon != null)
        {
            return new MediaSourceDisplayInfo(
                sourceApp,
                displayName,
                bundledIcon,
                MediaSourceDisplayKind.Bundled);
        }

        return new MediaSourceDisplayInfo(
            sourceApp,
            displayName,
            null,
            displayName == sourceApp ? MediaSourceDisplayKind.Unknown : MediaSourceDisplayKind.Mapping);
    }

    private async Task<MediaSourceInfo?> ResolvePlatformInfoAsync(
        string sourceApp,
        CancellationToken cancellationToken)
    {
        try
        {
            return await providerResolver.Resolve().SourceInfoProvider.ResolveAsync(sourceApp, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[媒体] 解析播放源信息失败：{SourceApp}", sourceApp);
            return null;
        }
    }

    private Bitmap? LoadFileIcon(string iconPath)
    {
        try
        {
            if (!File.Exists(iconPath))
            {
                return null;
            }

            using var stream = File.OpenRead(iconPath);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[媒体] 加载自定义播放源图标失败：{IconPath}", iconPath);
            return null;
        }
    }

    private Bitmap? LoadBundledIcon(string sourceApp)
    {
        try
        {
            if (!BundledIconMap.TryGetValue(sourceApp, out var iconUri))
            {
                return null;
            }

            using var stream = AssetLoader.Open(iconUri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[媒体] 加载内置播放源图标失败：{SourceApp}", sourceApp);
            return null;
        }
    }

    private MediaSource? FindMediaSource(string sourceApp)
    {
        return plugin.Settings.MediaSourceList.FirstOrDefault(source =>
            source != null &&
            string.Equals(source.Source, sourceApp, StringComparison.OrdinalIgnoreCase));
    }

    private static MappedDisplayName ResolveMappedDisplayName(string sourceApp)
    {
        if (DisplayNameMap.TryGetValue(sourceApp, out var displayName))
        {
            return new MappedDisplayName(displayName, true);
        }

        var lower = sourceApp.ToLowerInvariant();
        foreach (var (needle, name) in FuzzyNameRules)
        {
            if (lower.Contains(needle, StringComparison.Ordinal))
            {
                return new MappedDisplayName(name, true);
            }
        }

        // .exe 形式的 AUMID 去掉扩展名只是兜底，进程自身的描述名更准确，不算显式映射。
        if (sourceApp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new MappedDisplayName(Path.GetFileNameWithoutExtension(sourceApp), false);
        }

        // 认不出来的标识也要先洗一遍，别把 GUID / 完整 AUMID 丢给 UI。
        return new MappedDisplayName(MediaSourceDisplayNamePrettifier.Prettify(sourceApp), false);
    }

    private sealed record CacheKey(string SourceApp, string? IconPath, string? CustomDisplayName);
}

/// <param name="IsExplicit">是否命中内置映射表；命中时应覆盖平台返回的名称。</param>
internal sealed record MappedDisplayName(string Value, bool IsExplicit);

internal static class MediaSourceDisplayNameResolver
{
    internal static string Resolve(
        MappedDisplayName mapping,
        string? customDisplayName,
        string? platformDisplayName = null)
    {
        // 用户自定义 > 内置映射 > 平台解析名 > AUMID 兜底。
        if (!string.IsNullOrWhiteSpace(customDisplayName))
        {
            return customDisplayName;
        }

        if (mapping.IsExplicit)
        {
            return mapping.Value;
        }

        return string.IsNullOrWhiteSpace(platformDisplayName)
            ? mapping.Value
            : platformDisplayName;
    }
}
