using MediaIsland.Services.Media;
using Xunit;

namespace MediaIsland.Tests.Media;

/// <summary>
/// 启动参数与快捷方式判定的纯逻辑测试。
///
/// 真正的进程重启 / 快捷方式写入需要真实的网易云和桌面权限，这里只守住"参数拼得对不对、要不要改"这两条——
/// 它们是"最后一公里"体验里最容易悄悄坏掉的部分。
/// </summary>
public sealed class NetEaseLaunchHelperTests
{
    private const int Port = 9223;

    [Fact]
    public void BuildDebugArguments_WithoutExistingArguments_ProducesBothFlags()
    {
        var arguments = NetEaseLaunchHelper.BuildDebugArguments(string.Empty, Port);

        Assert.Equal("--remote-debugging-address=127.0.0.1 --remote-debugging-port=9223", arguments);
    }

    [Fact]
    public void BuildDebugArguments_KeepsUserArgumentsInFront()
    {
        var arguments = NetEaseLaunchHelper.BuildDebugArguments("  --disable-gpu  ", Port);

        Assert.Equal("--disable-gpu --remote-debugging-address=127.0.0.1 --remote-debugging-port=9223", arguments);
    }

    [Fact]
    public void ShortcutNeedsPatch_WhenArgumentsAreMissing_ReturnsTrue()
    {
        Assert.True(NetEaseLaunchHelper.ShortcutNeedsPatch(null, Port));
        Assert.True(NetEaseLaunchHelper.ShortcutNeedsPatch(string.Empty, Port));
    }

    [Fact]
    public void ShortcutNeedsPatch_WhenOnlyAddressIsSet_ReturnsTrue()
    {
        Assert.True(NetEaseLaunchHelper.ShortcutNeedsPatch("--remote-debugging-address=127.0.0.1", Port));
    }

    [Fact]
    public void ShortcutNeedsPatch_WhenPortAlreadyPresent_ReturnsFalse()
    {
        Assert.False(NetEaseLaunchHelper.ShortcutNeedsPatch("--remote-debugging-port=9223", Port));
        Assert.False(NetEaseLaunchHelper.ShortcutNeedsPatch("--foo --remote-debugging-port=9223 --bar", Port));
    }

    [Fact]
    public void ShortcutNeedsPatch_IsCaseInsensitive()
    {
        Assert.False(NetEaseLaunchHelper.ShortcutNeedsPatch("--REMOTE-DEBUGGING-PORT=9223", Port));
    }

    [Fact]
    public void ShortcutNeedsPatch_WhenOtherPortIsPresent_ReturnsTrue()
    {
        Assert.True(NetEaseLaunchHelper.ShortcutNeedsPatch("--remote-debugging-port=9222", Port));
    }
}
