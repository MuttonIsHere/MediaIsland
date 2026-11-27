# MediaIsland（社区维护版）

在 ClassIsland 的主界面上显示**正在播放的音乐**：封面、歌名、歌手，以及跟着节拍走的**逐字歌词**。

> **这个仓库是 [bywhite0/MediaIsland](https://github.com/bywhite0/MediaIsland) 的社区维护 fork。**
> 插件原作者是 **bywhite0（白影White）**，歌词同步、音源适配、UI 全部出自他手。
> 我们只是在他停更之后补上了 ClassIsland 2.1.0.1 的兼容和若干修复。
> **如果你觉得这个插件好用，请去给原作者点一个 Star** —— 那才是真正该被点亮的仓库。

---

## 一、先看这张表：你该下哪个包

| 你的 ClassIsland 版本 | 用哪个包 | 说明 |
| --- | --- | --- |
| **2.1.0.1（官方稳定版）** | [`v1.2.2`](https://github.com/MuttonIsHere/MediaIsland/releases/latest) | **就是给这个版本做的**，`net8.0` + `apiVersion 2.1.0.1` |
| 2.2 预览版 / 自己编译的宿主 | [`1.2.0`](https://github.com/MuttonIsHere/MediaIsland/releases/tag/1.2.0) | `net10.0` + `apiVersion 2.2.0.0` |

怎么查自己的版本：打开 ClassIsland → `设置` → `关于`，看那串版本号。

> 装错包会怎样？宿主会直接拒载，插件列表里看不到它，也不会报错。

## 二、安装：三步，不需要懂技术

1. **下载** Release 里的 `.cipx` 文件（例如 `MediaIsland-CE-1.2.2-CI2101.cipx`）。
2. **改后缀**：把文件名最后的 `.cipx` 改成 `.zip`，然后解压。
3. **放进插件目录**：把解压出来的所有文件（包括 `manifest.yml`）放到
   ```
   <你的 ClassIsland 安装目录>\data\Plugins\bywhite.mediaisland.ce\
   ```
   注意：`manifest.yml` 必须**直接**躺在这个目录里，不要再套一层文件夹。
4. **重启 ClassIsland**。

找不到 ClassIsland 装在哪？右键桌面/开始菜单里的 ClassIsland 快捷方式 → `打开文件所在的位置`，
上一级目录里应该有个 `data` 文件夹。

### 为什么不能点「从文件安装」

ClassIsland 自带 `设置 → 插件 → 从文件安装`，但这个包**不要**用它，原因是别的插件会捣乱：

装了 **MINI ClassIsland 集控增强组件**的机器上，它会用文件监视器盯着
`data\Cache\PluginPackages\*.cipx` 并逐个验签。只要读取这个包时抛出异常
（最常见的是安装流程正占用文件），它的异常分支就会把这个包**直接删掉**，
随后 ClassIsland 的安装器解压失败——而这条失败只写进控制台、**不进日志**。

结果就是：插件列表里没有、日志里也没有任何报错，纯隐身。
手动解压不经过那个暂存目录，也就绕开了它。

### 装好了怎么确认

打开 `data\Logs\` 里最新的日志，搜索 `MediaIsland`，应该能看到：

```
此次会话已加载插件:…,MediaIsland(bywhite.mediaisland.ce,1.2.2),…
MediaIsland.Services.PluginStartupLog|MediaIsland v1.2.2 已加载
```

然后在 ClassIsland 的 `设置 → 组件` 里，能搜到「正在播放」「实时歌词」等组件，拖到主界面行里即可。

## 三、常见问题

**岛上什么都不显示？**
先确认组件已经加到主界面行里（`设置 → 组件` → 拖进去）。如果加了还是不显示，
大概率是 `.axaml` 没编译进 DLL——那是 1.2.0 及更早版本的问题，升级到本仓库的 2.1.0.1 兼容版即可。

**放歌了但组件是空的？**
组件只在系统能读到「媒体会话」时才显示。部分播放器不向 Windows 上报 SMTC（例如某些网页播放器），
这种情况插件拿不到任何信息，会安静地隐藏自己，不会报错。

**来源图标/名字显示成别的应用？**
Windows 给的 AUMID 有时很脏（比如 QQ 音乐内置小程序运行时会带出一堆 `WeChatAppEx.exe`）。
从 1.2.2 起，插件的来源解析会优先认进程名，并对常见播放器做显式映射，
认不出来的标识也只会显示成「正在播放」，不会把 GUID 甩到大屏上。

**歌词不动 / 一直慢半拍？**
见下一节。网易云音乐需要特殊启动方式。

**网易云精确同步怎么开？**
网易云不上报播放进度，必须让它带着调试端口启动。插件设置页
`设置 → 歌词 → 网易云精确进度（DevTools）` 里有按钮：

| 按钮 | 作用 |
| --- | --- |
| 检测 | 探测 `9223` 端口是否可连，并显示当前读到的进度 |
| 启动网易云（精确同步版） | 网易云没开时，直接带参数启动 |
| 重启网易云以精确同步 | 结束正在运行的网易云，再带参数启动（当前歌曲会从头播放） |
| 生成桌面快捷方式 | 在桌面新建「网易云音乐（精确同步）」，不动你原有的快捷方式 |
| 为开始菜单/任务栏快捷方式加参数 | 给已有快捷方式补上调试参数，原文件备份成 `*.lnk.bak-时间` |

> 必须通过上述按钮或它生成的快捷方式启动网易云，否则插件读不到真实进度，
> 会**自动降级**为「按进度条比例推算」，歌词会慢慢漂移。

降级顺序：**DevTools 精确进度 → 按进度条比例推算 → 纯计时**。看不懂日志时，直接看设置页的状态行。

## 四、这个版本做了什么

**1.2.3（当前）**

- **来源图标不再串台**：`Tencent.QQMusic` 里 `Tencent` 只是厂商目录，微信在 `D:\Tencent\Weixin\`、
  企业微信在 `...\Tencent\WXWork\`，实测 18 个进程同分；QQ 音乐没起来时图标就会被别人顶掉。
  现在要求**产品段必须命中**（`QQMusic`），没有合格候选就不显示图标；缓存也加了 TTL，
  播放器后启动能在下一首歌自动纠正。

**1.2.2**

- **来源显示修正**：修好「QQ 音乐显示成微信」——QQ 音乐内置微信小程序运行时，
  其 `WeChatAppEx.exe` 的路径同时含 `Tencent` 与 `QQMusic`，旧打分与本体同分，
  谁先被枚举谁赢。现在按「命中标识段数量」加权，并让**进程名精确匹配**优先。
- **来源名称兜底**：常见播放器（QQ 音乐、网易云、Spotify、酷狗、酷我、Apple Music、
  foobar2000、PotPlayer、VLC、LX Music、AMLL…）统一显示友好名称；
  认不出的 AUMID / GUID 一律洗成「正在播放」，界面不再出现底层标识。
- **不再卡主线程**：设置页里「重启网易云」「启动网易云」「生成/修补快捷方式」原本在 UI 线程
  同步执行（含 1.5 秒等待与 Shell COM 调用），现在全部移到线程池；
  音频采集停止改为有上限的等待，禁用插件时不会冻住界面。
- **解析失败不再污染缓存**：来源解析一旦抛异常，会把错误 Task 永久留在缓存里，
  之后每次读取都重新抛。现在失败会回退成基础信息并记一条 Warn。

**1.2.1（ClassIsland 2.1.0.1 兼容版）**

- 降级到 `net8.0` + `PluginSdk 2.1.0.1` + `apiVersion 2.1.0.1`（1.2.0 是 net10，2.1.0.1 直接拒载）
- 补上 Avalonia 直接引用，让 `.axaml` 真正被编译（否则组件 `InitializeComponent()` 抛
  `No precompiled XAML found`，宿主吞掉后表现为组件凭空消失）
- API 降级适配：`FASettingsExpander` → `SettingsExpander`、`PlaceholderText` → `Watermark`、
  `SKSamplingOptions` → `SKFilterQuality`

**1.2.0**

- 暂停/恢复后歌词不再回退、单曲循环不再卡最后一句
- 新增网易云 DevTools 精确同步（逐秒对齐）
- 逐字高亮修复、全局时间轴偏移

## 五、组件一览

| 组件 | 作用 |
| --- | --- |
| 正在播放 | 封面 + 歌名 + 歌手 + 来源徽标，可自定义显示项 |
| 正在播放(简) | 精简版 |
| 实时歌词 | 逐字/逐行歌词，支持翻译与音译 |
| 音频频谱 | 各频段强弱，六种样式 |
| 音频波形 | 示波器式波形 |
| 音频能量 | 随音量起伏的图形 |
| 音频电平 | 音量 + 左右声像 |

> 音频类组件会实时采集系统声音做分析，**占用较高**，低性能设备请谨慎启用。

## 六、从源码构建

```powershell
dotnet build .\MediaIsland\MediaIsland.csproj -c Release `
  -p:TtmlNativeManifest= -p:AudioNativeManifest= -p:NuGetAudit=false

# 打包成 .cipx（会先清掉 bin 目录里残留的 publish\，避免把上一轮产物压进包）
powershell -ExecutionPolicy Bypass -File .\Tools\Pack-Cipx.ps1
```

需要 .NET 8 SDK；音频/歌词的原生库已随 `native/` 提供预编译产物，
没有 Rust 工具链也能构建（上面的 `-p:...NativeManifest=` 就是跳过 cargo）。

## 七、与上游的关系

- 本仓库是 [bywhite0/MediaIsland](https://github.com/bywhite0/MediaIsland) 的 fork
- 歌词与播放器适配的修复位于 `dev/ci-v2`、`release/1.2.0`
- ClassIsland 2.1.0.1 兼容适配位于 `compat/classisland-2.1.0.1`
- 已向上游提交 PR [#47](https://github.com/bywhite0/MediaIsland/pull/47)，尚未处理
- **若上游恢复维护，建议切回上游版本**，本仓库届时停止更新
- 问题反馈：[Issues](https://github.com/MuttonIsHere/MediaIsland/issues)

## 八、致谢与许可

感谢 **bywhite0（白影White）** 写出这个插件，并允许社区在其基础上继续维护。

本项目使用了以下第三方库：

- [ClassIsland.PluginSdk](https://www.nuget.org/packages/ClassIsland.PluginSdk)
- [WindowsAPICodePackShell](https://www.nuget.org/packages/WindowsAPICodePackShell)
- [Dubya.WindowsMediaController](https://www.nuget.org/packages/Dubya.WindowsMediaController)

QQ 音乐 QRC 与酷狗 KRC 的解密算法移植自 [Lyricify.Lyrics.Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)（Apache-2.0），
歌词中制作人员与版权信息的识别规则移植自 [lyric-kit](https://github.com/SPlayer-Dev/lyric-kit)（AGPL-3.0），
详见 `MediaIsland/ThirdPartyLicenses.txt`。

本项目基于 **GNU Affero General Public License v3.0** 许可；
部分代码来自 [DubyaDude/WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)（MIT）
与 [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout)（GPL-3.0）。
