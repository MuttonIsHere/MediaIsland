using MediaIsland.Services.Media.SourceDisplay;
using Xunit;

namespace MediaIsland.Tests.Media;

public class MediaSourceDisplayNameResolverTests
{
    private static MappedDisplayName Explicit(string value) => new(value, true);

    private static MappedDisplayName Fallback(string value) => new(value, false);

    [Fact]
    public void Resolve_CustomNameWinsOverEverything()
    {
        Assert.Equal(
            "工作音乐",
            MediaSourceDisplayNameResolver.Resolve(Explicit("Spotify"), "工作音乐", "Spotify for Windows"));
        Assert.Equal(
            "工作音乐",
            MediaSourceDisplayNameResolver.Resolve(Fallback("cloudmusic"), "工作音乐", "CloudMusic"));
    }

    [Fact]
    public void Resolve_ExplicitMappingWinsOverPlatformName()
    {
        // 网易云音乐进程描述名是 "CloudMusic"，内置映射应覆盖它。
        Assert.Equal(
            "网易云音乐",
            MediaSourceDisplayNameResolver.Resolve(Explicit("网易云音乐"), null, "CloudMusic"));
    }

    [Fact]
    public void Resolve_FallbackMapping_UsesPlatformNameWhenAvailable()
    {
        Assert.Equal(
            "Spotify for Windows",
            MediaSourceDisplayNameResolver.Resolve(Fallback("Spotify"), null, "Spotify for Windows"));
    }

    [Fact]
    public void Resolve_NoPlatformName_FallsBackToMapping()
    {
        Assert.Equal(
            "Spotify",
            MediaSourceDisplayNameResolver.Resolve(Fallback("Spotify"), " ", null));
        Assert.Equal(
            "Spotify",
            MediaSourceDisplayNameResolver.Resolve(Fallback("Spotify"), null, "   "));
    }
}

/// <summary>
/// 「不让用户看到底层脏数据」这条规则的回归：打包应用 AUMID、反域名、GUID 都要洗成人话。
/// </summary>
public class MediaSourceDisplayNamePrettifierTests
{
    [Theory]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("cn.toside.music.desktop", "desktop")]
    [InlineData("Tencent.QQMusic", "QQMusic")]
    [InlineData("cloudmusic.exe", "cloudmusic")]
    public void Prettify_IdentifierLikeValues_ReduceToProductSegment(string sourceApp, string expected)
    {
        Assert.Equal(expected, MediaSourceDisplayNamePrettifier.Prettify(sourceApp));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("3f2a1b9c-8d4e-4f6a-9b2c-1d3e5f7a9b0c")]
    [InlineData("{3f2a1b9c-8d4e-4f6a-9b2c-1d3e5f7a9b0c}")]
    public void Prettify_OpaqueValues_FallBackToGenericLabel(string sourceApp)
    {
        Assert.Equal(MediaSourceDisplayNamePrettifier.FallbackDisplayName, MediaSourceDisplayNamePrettifier.Prettify(sourceApp));
    }

    [Theory]
    [InlineData("QQ音乐")]
    [InlineData("网易云音乐")]
    [InlineData("SPlayer Next")]
    [InlineData("Media Player")]
    public void Normalize_HumanReadableNames_AreLeftUntouched(string displayName)
    {
        Assert.Equal(displayName, MediaSourceDisplayNamePrettifier.Normalize(displayName));
    }

    [Fact]
    public void Normalize_RawAumid_IsCleanedInsteadOfShownRaw()
    {
        Assert.Equal("QQMusic", MediaSourceDisplayNamePrettifier.Normalize("Tencent.QQMusic"));
        Assert.Equal("Media Player", MediaSourceDisplayNamePrettifier.Normalize("Media Player"));
        Assert.Equal(
            MediaSourceDisplayNamePrettifier.FallbackDisplayName,
            MediaSourceDisplayNamePrettifier.Normalize("3f2a1b9c-8d4e-4f6a-9b2c-1d3e5f7a9b0c"));
    }
}
