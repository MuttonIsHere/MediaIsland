# MediaIsland

MediaIsland 是一款 [ClassIsland](https://classisland.tech) 插件，用于在 ClassIsland 主界面上显示 Windows [SMTC](https://learn.microsoft.com/en-us/uwp/api/windows.media.systemmediatransportcontrols) 媒体信息。

> [!IMPORTANT]
>
> 本分支面向 ClassIsland 2.0 版本。请切换到 `master` 分支以查看面向 ClassIsland 1.x 的版本。

## 组件

**正在播放**

![截图](./Assets/screenshot.png)

显示正在播放的媒体信息，可按需调整显示内容。

![设置截图](./Assets/npc_settings.png)

<details>
  <summary>更多使用方式...</summary>
    搭配 <img src="https://github.com/LiPolymer.png" alt="LiPolymer" style="width:16px" /><a href="https://github.com/LiPolymer">LiPolymer</a> 佬的 <a href="https://github.com/LiPolymer/ExtraIsland">ExtraIsland</a> 的【当前活动】组件，并启用「启用歌词」功能：
    <img src="Assets/screenshot_with_ei.png" alt="搭配ei使用截图" />
  </details>

**正在播放(简)**

![截图](./Assets/screenshot_snpc.png)

简略显示正在播放的媒体信息，可调整显示样式。

![设置截图](./Assets/snpc_settings.png)

**实时歌词**

![截图](./Assets/screenshot_lyrics.gif)

根据当前播放媒体搜索并显示同步歌词。

![设置截图](./Assets/lc_settings.png)

### 网易云精确同步（可选，推荐）

网易云音乐**不会**通过 Windows SMTC 上报播放进度（时间轴一直是 0），所以歌词没法跟着走。
本插件改为直接读取网易云客户端页面里的真实进度（CEF DevTools，默认端口 `9223`），歌词能和播放**逐秒对齐**。

> [!WARNING]
>
> **要启用「网易云精确同步」，必须通过本插件提供的按钮或本插件生成的快捷方式启动网易云**（即带上
> `--remote-debugging-address=127.0.0.1 --remote-debugging-port=9223`）。
> 网易云只有在**启动时**才会打开这个调试端口，运行起来之后无法再打开；
> 直接用任务栏、开始菜单或桌面上的原快捷方式启动时，插件读不到进度，会**自动降级**为「按进度条比例推算」，
> 歌词会慢慢漂移（换歌、跳转、暂停后更明显）。

操作路径：`设置 → 歌词 → 网易云精确进度（DevTools）`，然后按需点：

| 按钮 | 作用 |
| --- | --- |
| 检测 | 探测 9223 端口能不能连上，并显示当前读到的进度 |
| 启动网易云（精确同步版） | 网易云没开时，直接带参数启动；已经开着但没带参数时会先问一句要不要重启 |
| 重启网易云以精确同步 | 结束正在运行的网易云，再带参数启动（当前歌曲会从头播放） |
| 生成桌面快捷方式 | 在桌面新建「网易云音乐（精确同步）」，以后用它启动即可（不动你原有的快捷方式） |
| 为开始菜单/任务栏快捷方式加参数（先备份） | 给开始菜单、任务栏固定区的快捷方式补上调试参数，原文件备份成 `*.lnk.bak-时间` |

降级顺序：**DevTools 精确进度 → 按进度条比例推算 → 纯计时（盲跑）**。看不懂日志时，可以直接看设置页的状态行。


> [!IMPORTANT]
>
> 本插件的内存占用较大，低性能设备请谨慎使用。

> [!NOTE]
> 
> 在较新的 Windows 版本中，使用部分应用可能无法正常获取播放器信息。

## 要求

本插件需要 Windows 10 Build 17763 (1809) 或以上版本。

## 开发计划 & 已知问题

请移步 [Discussion](https://github.com/bywhite0/MediaIsland/discussions/10)

## 致谢

本项目使用了以下第三方库：

- [ClassIsland.PluginSdk](https://www.nuget.org/packages/ClassIsland.PluginSdk)
- [WindowsAPICodePackShell](https://www.nuget.org/packages/WindowsAPICodePackShell)
- [Dubya.WindowsMediaController](https://www.nuget.org/packages/Dubya.WindowsMediaController)

QQ 音乐 QRC 与酷狗 KRC 的解密算法移植自 [Lyricify.Lyrics.Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)（Apache-2.0），详见 `MediaIsland/ThirdPartyLicenses.txt`。
歌词中制作人员与版权信息的识别规则移植自 [lyric-kit](https://github.com/SPlayer-Dev/lyric-kit)（AGPL-3.0），详见 `MediaIsland/ThirdPartyLicenses.txt`。

## 许可

[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2Fbywhite0%2FMediaIsland.svg?type=shield&issueType=license)](https://app.fossa.com/projects/git%2Bgithub.com%2Fbywhite0%2FMediaIsland?ref=badge_shield&issueType=license)

本项目基于 GNU Affero General Public License v3.0 许可。

本项目部分使用了 [DubyaDude/WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController) 的代码，其内容仍基于 MIT 许可。

本项目部分使用了 [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout) 的代码，其内容仍基于 GNU General Public License v3.0 许可。
