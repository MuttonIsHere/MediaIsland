using MediaIsland.Helpers;
using Xunit;

namespace MediaIsland.Tests.Media;

public class MediaSourceProcessMatcherTests
{
    [Fact]
    public void GetIdentifierVariants_ReverseDomainAumid_SplitsIntoUsableSegments()
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants("cn.toside.music.desktop");

        Assert.Contains("toside", variants);
        Assert.Contains("music", variants);
        Assert.Contains("desktop", variants);
    }

    [Fact]
    public void GetIdentifierVariants_DropsSegmentsTooShortToBeDistinctive()
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants("cn.toside.music.desktop");

        Assert.DoesNotContain("cn", variants);
    }

    [Fact]
    public void GetIdentifierVariants_StripsNoiseSegments()
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants("com.github.player.exe");

        Assert.DoesNotContain("com", variants);
        Assert.DoesNotContain("github", variants);
        Assert.DoesNotContain("exe", variants);
        Assert.Contains("player", variants);
    }

    [Fact]
    public void GetIdentifierVariants_OrdersLongestFirst()
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants("cn.toside.music.desktop");

        var lengths = variants.Select(variant => variant.Length).ToList();
        Assert.Equal(lengths.OrderByDescending(length => length), lengths);
    }

    [Fact]
    public void GetIdentifierVariants_AlwaysKeepsOriginalAumidAsFallback()
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants("SomeOpaqueId");

        Assert.Contains("SomeOpaqueId", variants);
    }

    [Fact]
    public void GetIdentifierVariants_SplitsPackagedAumidOnBang()
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(
            "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");

        Assert.Contains("Spotify", variants);
        Assert.Contains("SpotifyMusic", variants);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public void GetIdentifierVariants_EmptyOrTooShortInput_ReturnsNoVariants(string sourceApp)
    {
        Assert.Empty(MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp));
    }

    [Fact]
    public void ScoreCandidate_LxMusicProcess_MatchesReverseDomainAumid()
    {
        const string sourceApp = "cn.toside.music.desktop";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        var score = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Program Files\lx-music-desktop\lx-music-desktop.exe",
            hasMainWindow: true,
            variants);

        Assert.True(score > 0);
    }

    [Fact]
    public void ScoreCandidate_UnrelatedProcess_DoesNotMatch()
    {
        const string sourceApp = "cn.toside.music.desktop";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        var score = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Windows\System32\notepad.exe",
            hasMainWindow: true,
            variants);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreCandidate_ExactExecutableName_OutranksPathOnlyMatch()
    {
        const string sourceApp = "cloudmusic.exe";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        var mainScore = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Program Files\cloudmusic\cloudmusic.exe",
            hasMainWindow: false,
            variants);
        var helperScore = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Program Files\cloudmusic\cloudmusic_helper.exe",
            hasMainWindow: true,
            variants);

        Assert.True(mainScore > helperScore);
    }

    [Fact]
    public void ScoreCandidate_MainWindowProcess_OutranksBackgroundProcess()
    {
        const string sourceApp = "net.stevexmh.amllplayer";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);
        const string path = @"C:\Apps\amllplayer\amllplayer.exe";

        var windowed = MediaSourceProcessMatcher.ScoreCandidate(sourceApp, path, true, variants);
        var background = MediaSourceProcessMatcher.ScoreCandidate(sourceApp, path, false, variants);

        Assert.True(windowed > background);
    }

    [Fact]
    public void ScoreCandidate_MatchIsCaseInsensitive()
    {
        const string sourceApp = "cloudmusic.exe";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        var score = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Program Files\CloudMusic\CloudMusic.exe",
            hasMainWindow: true,
            variants);

        Assert.True(score > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ScoreCandidate_EmptyPath_ReturnsZero(string processPath)
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants("cloudmusic.exe");

        Assert.Equal(0, MediaSourceProcessMatcher.ScoreCandidate(
            "cloudmusic.exe",
            processPath,
            hasMainWindow: true,
            variants));
    }

    [Fact]
    public void ScoreProcessName_MatchesWithoutTouchingPath()
    {
        const string sourceApp = "cn.toside.music.desktop";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        Assert.True(MediaSourceProcessMatcher.ScoreProcessName(
            sourceApp, "lx-music-desktop", hasMainWindow: true, variants) > 0);
    }

    [Fact]
    public void ScoreProcessName_UnrelatedProcessName_ReturnsZero()
    {
        const string sourceApp = "cn.toside.music.desktop";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        Assert.Equal(0, MediaSourceProcessMatcher.ScoreProcessName(
            sourceApp, "svchost", hasMainWindow: false, variants));
    }

    [Fact]
    public void ScoreProcessName_ExactProcessName_OutranksPartialMatch()
    {
        const string sourceApp = "cloudmusic.exe";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        var exact = MediaSourceProcessMatcher.ScoreProcessName(
            sourceApp, "cloudmusic", hasMainWindow: false, variants);
        var partial = MediaSourceProcessMatcher.ScoreProcessName(
            sourceApp, "cloudmusic_helper", hasMainWindow: true, variants);

        Assert.True(exact > partial);
    }

    [Fact]
    public void ScoreProcessName_EmptyName_ReturnsZero()
    {
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants("cloudmusic.exe");

        Assert.Equal(0, MediaSourceProcessMatcher.ScoreProcessName(
            "cloudmusic.exe", string.Empty, hasMainWindow: true, variants));
    }

    [Fact]
    public void ScoreCandidate_SharedVendorPrefix_DoesNotStealAnotherAppSession()
    {
        // 现场复现：QQ 音乐的 AUMID 是 Tencent.QQMusic，微信装在 ...\Tencent\WeChat\ 下，
        // 两者路径都含 "Tencent"。旧打分只看最长命中，两者同分，微信靠枚举顺序赢，
        // 大屏上于是把 QQ 音乐显示成「微信」。
        const string sourceApp = "Tencent.QQMusic";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        var qqMusic = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Program Files (x86)\Tencent\QQMusic\QQMusic.exe",
            hasMainWindow: false,
            variants);
        var weChat = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Program Files\Tencent\WeChat\WeChat.exe",
            hasMainWindow: true,
            variants);

        Assert.True(qqMusic > 0);
        Assert.True(qqMusic > weChat, $"QQ 音乐得分 {qqMusic} 应高于微信 {weChat}");
    }

    [Fact]
    public void ScoreCandidate_MoreMatchedSegments_OutranksVendorOnlyMatch()
    {
        const string sourceApp = "Tencent.QQMusic";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        // 同为腾讯系，但只有 QQMusic 目录名同时命中 Tencent 与 QQMusic 两段。
        var dedicated = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp, @"C:\Tencent\QQMusic\QQMusic.exe", false, variants);
        var vendorOnly = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp, @"C:\Tencent\WeGame\WeGame.exe", true, variants);

        Assert.True(dedicated > vendorOnly);
    }

    [Fact]
    public void ScoreCandidate_EmbeddedWeChatHelper_LosesToActualExecutable()
    {
        // 现场实测：QQ 音乐内置微信小程序运行时，WeChatAppEx.exe 的路径同时含
        // Tencent 与 QQMusic 两段，比本体 D:\QQMusic\QQMusic.exe 命中更多；
        // 但只有本体的进程名与标识段完全一致，必须优先。
        const string sourceApp = "Tencent.QQMusic";
        var variants = MediaSourceProcessMatcher.GetIdentifierVariants(sourceApp);

        var actual = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp, @"D:\QQMusic\QQMusic.exe", hasMainWindow: true, variants);
        var helper = MediaSourceProcessMatcher.ScoreCandidate(
            sourceApp,
            @"C:\Users\x\AppData\Roaming\Tencent\QQMusic\wmpf\2.2.3\WeChatAppEx.exe",
            hasMainWindow: false,
            variants);

        Assert.True(actual > helper, $"本体得分 {actual} 应高于 WMPF 辅助进程 {helper}");
    }

    [Fact]
    public void ScoreCandidate_NoVariants_ReturnsZero()
    {
        Assert.Equal(0, MediaSourceProcessMatcher.ScoreCandidate(
            "a",
            @"C:\Windows\System32\notepad.exe",
            hasMainWindow: true,
            []));
    }
}
