# MediaIsland

> [!IMPORTANT]
> ## 这是社区维护版（v1.2.2）
>
> 上游 [bywhite0/MediaIsland](https://github.com/bywhite0/MediaIsland) 自 2026 年 8 月后未再更新
> （最后发版 `1.1.2.1` / 2026-08-05，最后提交 2026-09-26），
> 相关修复已向上游提交 PR [#47](https://github.com/bywhite0/MediaIsland/pull/47)，但暂未被处理。
>
> 本仓库（`MuttonIsHere/MediaIsland`）在原作者代码基础上打包发布修复版，供需要的人直接使用。

## 📦 下载安装

先确认你的 ClassIsland 版本（`设置 → 关于`），再选对应的包：

| 你的 ClassIsland | 下载 | 说明 |
| --- | --- | --- |
| **2.1.0.1（官方稳定版）** ✅ 推荐 | [`v1.2.2-CE-CI2101`](https://github.com/MuttonIsHere/MediaIsland/releases/tag/v1.2.2-CE-CI2101) | `net8.0` + `apiVersion 2.1.0.1`，**专为 2.1.0.1 编译** |
| 2.2 预览版 / 自编译宿主 | [`1.2.0`](https://github.com/MuttonIsHere/MediaIsland/releases/tag/1.2.0) | `net10.0` + `apiVersion 2.2.0.0` |

| 操作 | 步骤 |
| --- | --- |
| **安装** | 把 `.cipx` 的后缀改成 `.zip`，解压到 `<ClassIsland 安装目录>\data\Plugins\bywhite.mediaisland.ce\`（`manifest.yml` 必须直接位于该目录下），然后重启 ClassIsland |
| **校验** | SHA256 / MD5 见 [v1.2.2-CE-CI2101 Release 说明](https://github.com/MuttonIsHere/MediaIsland/releases/tag/v1.2.2-CE-CI2101)、[1.2.0 checksums](https://github.com/MuttonIsHere/MediaIsland/releases/download/1.2.0/checksums.md) |

> [!TIP]
> 已安装旧版（`1.1.x` / `1.2.0`）的用户，直接装新包覆盖即可，设置会保留；
> 插件 `id` 是 `bywhite.mediaisland.ce`，不会出现两个 MediaIsland。

> [!WARNING]
> **别用「设置 → 插件 → 从文件安装」装这个包。**
>
> 如果机器上装了 MINI ClassIsland 集控增强组件（`org.miniopensource.miniclassisland.extendmanagement`），
> 它的 `SelfProtectionService` 会用 `FileSystemWatcher` 盯着 `data\Cache\PluginPackages\*.cipx`，
> 并调用 `CipxPackageSshVerifier.InspectAndVerifyCipxPackage()` 验签。
> 只要读取这个包时抛异常（最常见是安装流程正占用文件，`IOException: being used by another process`），
> 该方法的 `catch` 会**无条件**返回「这是一枚待验签的包」，调用方于是 `File.Delete` 把它删掉。
>
> 紧接着 ClassIsland 的安装器解压失败，而这条失败只写进 Console、**不进日志文件**，
> 于是表现为：**插件列表里完全没有、日志里连一行报错都没有**，俗称隐身。
>
> 手动解压不经过这个暂存目录，也就完全绕开了它。

> [!IMPORTANT]
> **为什么单独出了一个 2.1.0.1 兼容版？**
> 1.2.0 面向 `net10.0` + `PluginSdk 2.1.1.1` + `apiVersion 2.2.0.0`，官方稳定版 2.1.0.1（`net8.0`）会直接拒载。
> 修好清单后还有个更隐蔽的坑：`.axaml` 从未被编译进 DLL（插件 SDK 只是传递引用 Avalonia，
> 构建目标没导入）→ 每个组件在 `InitializeComponent()` 抛 `No precompiled XAML found` 并被宿主吞掉，
> 表现为**组件完全不显示、设置页打不开**。
> 2.1.0.1 兼容版补上直接 Avalonia 引用，并把 `FASettingsExpander`→`SettingsExpander`、
> `PlaceholderText`→`Watermark`、`SKSamplingOptions`→`SKFilterQuality` 等 API 降级改好。

## ✨ 1.2.0 修复内容

- **暂停 / 恢复后歌词位置回退** —— 恢复播放时歌词不再跳回之前的位置
- **单曲循环冻结** —— 单曲循环时歌词不再卡在第一行
- **网易云音乐精确同步（新增）** —— 网易云不上报 SMTC 进度（时间轴一直为 0），
  本版通过 CEF DevTools（默认端口 `9223`）读取页面里的真实播放时间，歌词**逐秒对齐**，
  不再依赖「按进度条比例推算」
- **逐字高亮（KTV 模式）** —— 修复多段歌词的取值错误
- **全局时间轴偏移** —— 可在设置里统一微调提前/延后

> [!WARNING]
> 网易云精确同步**必须通过插件生成的按钮或快捷方式启动网易云**（带 `--remote-debugging-port=9223`）。
> 用原快捷方式启动会**自动降级**为比例推算，歌词会慢慢漂移。
> 详细说明见下方「网易云精确同步」一节。

## 🔗 与上游的关系

- 本仓库是 [bywhite0/MediaIsland](https://github.com/bywhite0/MediaIsland) 的 fork
- 修复代码位于分支 `dev/ci-v2`（以及 `release/1.2.0`）
- ClassIsland 2.1.0.1 兼容适配位于分支 `compat/classisland-2.1.0.1`（tag `v1.2.2-CE-CI2101`）
- 打包用 `Tools/Pack-Cipx.ps1`（会先清掉 `bin\...\publish\` 残留，避免旧 DLL 被一起压进包）
- 上游 PR：<https://github.com/bywhite0/MediaIsland/pull/47>（4 commits，13 文件，+2445/−19，mergeable clean）
- **若上游恢复维护并合入**，建议切回上游版本；本仓库届时会停止更新
- 遇到问题欢迎在 [Issues](https://github.com/MuttonIsHere/MediaIsland/issues) 反馈

---

# 原版说明

MediaIsland 是一款 [ClassIsland](https://classisland.tech) 插件，用于在 ClassIsland 主界面上显示 Windows [SMTC](https://learn.microsoft.com/en-us/uwp/api/windows.media.systemmediatransportcontrols) 媒体信息。

> [!IMPORTANT]
>
> 本分支面向 ClassIsland 2.0 版本。ClassIsland 1.x 版本请看 `master` 分支。

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
