using System.IO;
using System.Text.RegularExpressions;

namespace MediaIsland.Services.Media.SourceDisplay;

/// <summary>
/// 把 SMTC 的原始标识整理成可以给人看的名字。
/// </summary>
/// <remarks>
/// SMTC 的 <c>SourceAppUserModelId</c> 是给系统看的：打包应用形如
/// <c>Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic</c>，
/// 反域名形如 <c>cn.toside.music.desktop</c>，偶尔还会是纯 GUID。
/// 这些字符串一旦原样画到大屏上，就是「粗糙的底层标识」。
/// 这里负责在最终显示前做最后一次清洗：去掉发布者哈希、取产品段，
/// 认不出来的就退回「正在播放」，而不是把 GUID 甩给用户。
/// </remarks>
internal static partial class MediaSourceDisplayNamePrettifier
{
    /// <summary>认不出来时的兜底文案，绝不显示原始标识。</summary>
    internal const string FallbackDisplayName = "正在播放";

    /// <summary>超过这个长度的标识基本都是 AUMID 拼接产物，不再硬显示。</summary>
    private const int MaxReasonableLength = 32;

    [GeneratedRegex(
        @"^\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?$",
        RegexOptions.Compiled)]
    private static partial Regex GuidPattern();

    /// <summary>判断是否属于「不该直接显示」的原始标识。</summary>
    internal static bool LooksOpaque(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (GuidPattern().IsMatch(trimmed))
        {
            return true;
        }

        if (trimmed.Contains('!', StringComparison.Ordinal))
        {
            return true;
        }

        // 带点又不带空格的基本都是标识（Tencent.QQMusic、cn.toside.music.desktop），
        // 正常应用名不会长这样。
        if (trimmed.Contains('.', StringComparison.Ordinal) &&
            !trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return true;
        }

        // AUMID 里常见的「下划线 + 13 位发布者哈希」。
        if (trimmed.Contains('_', StringComparison.Ordinal) && trimmed.Length > MaxReasonableLength)
        {
            return true;
        }

        return trimmed.Length > MaxReasonableLength;
    }

    /// <summary>把原始标识整理成可读名字；无法整理时返回兜底文案。</summary>
    internal static string Prettify(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp))
        {
            return FallbackDisplayName;
        }

        var value = sourceApp.Trim();

        // 打包应用：只取 "!" 后面的 AppId。
        var bangIndex = value.IndexOf('!', StringComparison.Ordinal);
        if (bangIndex > 0 && bangIndex < value.Length - 1)
        {
            value = value[(bangIndex + 1)..];
        }

        // 去掉 "_8wekyb3d8bbwe" 这类发布者哈希。
        var underscoreIndex = value.IndexOf('_', StringComparison.Ordinal);
        if (underscoreIndex > 0)
        {
            value = value[..underscoreIndex];
        }

        // 先处理可执行文件名：cloudmusic.exe 不该被当成「点号分隔」而取到 exe。
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var exeName = Path.GetFileNameWithoutExtension(value).Trim();
            return exeName.Length == 0 || GuidPattern().IsMatch(exeName)
                ? FallbackDisplayName
                : exeName;
        }

        // 反域名式标识取最后一段（cn.toside.music.desktop → desktop）。
        if (value.Contains('.', StringComparison.Ordinal))
        {
            var lastSegment = value
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                value = lastSegment;
            }
        }

        value = value.Trim();

        if (value.Length == 0 || GuidPattern().IsMatch(value) || value.Length > MaxReasonableLength)
        {
            return FallbackDisplayName;
        }

        return value;
    }

    /// <summary>显示前的最后一道清洗：已经像人话的名字原样放过，其余交给 <see cref="Prettify"/>。</summary>
    internal static string Normalize(string? displayName, string? sourceApp = null)
    {
        if (!LooksOpaque(displayName))
        {
            return displayName!.Trim();
        }

        var prettified = Prettify(displayName);
        if (!string.Equals(prettified, FallbackDisplayName, StringComparison.Ordinal))
        {
            return prettified;
        }

        return Prettify(sourceApp);
    }
}
