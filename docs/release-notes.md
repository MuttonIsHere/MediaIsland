# MediaIsland CE v1.2.3（ClassIsland 2.1.0.1 兼容版）

> 取代 `v1.2.2`。装过旧版的可以直接覆盖，设置不变。
>
> 1.2.1 修「装不上 / 组件不显示」，1.2.2 修运行期崩溃点与打包工具链，
> 1.2.3 修「来源图标被别的腾讯应用顶掉」——纯显示层修复，不动任何播放逻辑。

## ⚠️ 安装方式：手动解压，不要用「从文件安装」

**不要**在 ClassIsland 里点 `设置 → 插件 → 从文件安装`。

如果机器上装了 MINI ClassIsland 集控增强组件
（`org.miniopensource.miniclassisland.extendmanagement`），它的 `SelfProtectionService`
会用 `FileSystemWatcher` 盯着 `data\Cache\PluginPackages\*.cipx` 并逐包验签。
只要读取该包时抛异常（最常见是安装流程正占用文件，`IOException: being used by another process`），
它的 `catch` 会**无条件**返回「这是一枚待验签的包」，调用方随即 `File.Delete` 把包删掉。
紧接着 ClassIsland 的安装器解压失败，而这条失败只写进 Console、**不进日志文件**——
于是现象就是：**插件列表里根本没有、日志里连一行报错都没有**。

正确姿势：

1. 把 `.cipx` 的后缀改成 `.zip`；
2. 解压到 `<ClassIsland 安装目录>\data\Plugins\bywhite.mediaisland.ce\`
   （`manifest.yml` 必须直接位于该目录下，不能再套一层文件夹）；
3. 重启 ClassIsland。

装好的判据（日志里能看到）：

```
此次会话已加载插件:…,MediaIsland(bywhite.mediaisland.ce,1.2.2),…
MediaIsland.Services.PluginStartupLog|MediaIsland v1.2.2 已加载
```

## 本版改了什么

**修：来源图标被微信 / 企业微信顶掉（1.2.3）**

QQ 音乐的 AUMID 是 `Tencent.QQMusic`，其中 `Tencent` 只是**厂商目录**。而
微信在 `D:\Tencent\Weixin\`、企业微信在 `...\Tencent\WXWork\`、连 WorkBuddy 自带的
腾讯文档引擎 `editor_sdk.exe` 都在 `@tencent\` 下——实测**18 个进程同时命中 `Tencent` 且同分**。
QQ 音乐进程还没起来（或会话残留）时，`if (score > bestScore)` 取首个，谁先被枚举谁就是图标，
于是封面旁边出现了企业微信。更糟的是解析结果按 AUMID 缓存一整个会话，错一次就错到底。

现在：

- **产品段必须命中**：`Tencent.QQMusic` 的产品段是 `QQMusic`，候选进程路径里必须含它；
  一个都不合格就**不显示图标**，也不拿别的应用顶上。
- **缓存加 TTL**：有图标缓存 5 分钟，没图标只缓存 20 秒——先开宿主、后开播放器也能自动纠正，不用重启。

**修：QQ 音乐退出时抛 `NullReferenceException`，整条媒体快照被丢弃**

`WindowsSmtcMediaSessionProvider.BuildSnapshotAsync` 直接解引用
`controlSession.GetTimelineProperties()` / `GetPlaybackInfo()` 的返回值。WinRT 在会话销毁过程中
会返回 `null`，于是抛出 NRE 被外层 catch 吞掉，日志里留下一条带堆栈的告警，那一下媒体信息闪断。
现在时间轴退回零、播放状态退回 `Unknown`，**标题和封面照常显示**；`WindowsMediaController` 里同类的
两处解引用也补了空值防护（同仓库 `WindowsSmtcMediaPlaybackController` 早就用了 `?.`，只是这三处漏了）。

**修：QQ 音乐被显示成「微信」**

QQ 音乐内置了微信小程序运行时（WMPF），会拉起若干 `WeChatAppEx.exe`，路径形如
`...\AppData\Roaming\Tencent\QQMusic\wmpf\...\WeChatAppEx.exe` —— 同时含 `Tencent` 与 `QQMusic`。
旧打分只看「最长命中」，它和本体 `D:\QQMusic\QQMusic.exe` 同分（7 + 10 窗口），
谁先被枚举谁赢；而 `WeChatAppEx.exe` 的文件描述恰好是「微信」，大屏上于是写着微信。

现在的判定改成两条：命中标识段越多越可信（每多一段 +15）；**进程名与产品段完全一致**时 +100。
实测本体 117 分、WMPF 辅助进程 22 分。另外 `Tencent.QQMusic` 已加入显式映射，
显示名不再依赖进程猜测。

**修：来源名称里的底层脏数据**

新增 `MediaSourceDisplayNamePrettifier`：打包应用 AUMID（`..._8wekyb3d8bbwe!App`）、
反域名（`cn.toside.music.desktop`）、纯 GUID 都会在显示前清洗成产品名或「正在播放」；
常见播放器（QQ 音乐、网易云、Spotify、酷狗、酷我、Apple Music、foobar2000、PotPlayer、VLC、
LX Music、AMLL…）统一走名称字典。

**修：设置页按钮把 UI 卡住 1.5 秒**

「重启网易云以精确同步」「启动网易云」「生成/修补快捷方式」原本在 UI 线程同步执行
（结束进程 → `Thread.Sleep(1500)` → Shell 启动 / COM 调用）。现在全部移到线程池；
`AudioFrameHub.Dispose()` 里同步等待 `StopAsync` 也改成**有上限的等待**（2 秒），
禁用插件时界面不会冻住。

**修：来源解析失败会永久污染缓存**

`_cache` 存的是 `Task`，一旦该 Task 带着异常完成，之后每次 `await` 都会重新抛，
这个播放源就永久变成错误态。现在解析异常会回退成基础信息并记一条 Warn。

**修：打包脚本两个会误导人的问题**

- `Tools/Pack-Cipx.ps1` 没检查 `dotnet publish` 的退出码。原生进程失败只体现在 `$LASTEXITCODE`，
  `$ErrorActionPreference` 管不到，于是构建失败时脚本会拿上一轮的旧包算哈希、汇报「打包成功」。
- 脚本是不带 BOM 的 UTF-8 且含中文，PowerShell 5.1 会按 ANSI 解码，**直接报语法错误、跑不起来**。已改存 UTF-8 BOM。

**修：`checksums.md` 只有 MD5，但说明里写了「SHA256 / MD5 见附件」**

现在脚本会把 SHA256 一并写进 `checksums.md`，附件与说明一致。

**自带**：`CleanStalePublishDir` 会在 `CreateCipx` 之前清掉 `bin\<配置>\<TFM>\publish\`，
避免上一轮 `dotnet publish` 的残留被一起压进包（1.2.1 的首个包就是因此从 16 个条目膨胀到 29 个）。

## 校验

包体积约 1.6 MB、16 个条目。SHA256 / MD5 见本 Release 附件 `checksums.md`：

```
unzip -l MediaIsland.cipx    # 应为 16 files，且不含 publish/ 前缀
```

## 环境要求

- ClassIsland **2.1.0.1**（`net8.0`，`apiVersion 2.1.0.1`）；2.2 预览版请用 `1.2.0` 那个包。
- Windows 10 Build 17763 (1809) 或以上。
- 网易云精确同步需要通过插件提供的按钮/快捷方式启动网易云（带 `--remote-debugging-port=9223`）。

## 致谢

插件原作者是 [bywhite0（白影White）](https://github.com/bywhite0/MediaIsland)：
歌词同步、音源适配、UI 都是他写的。本仓库只是在他停更后补兼容与修复。
觉得好用的话，请去给原仓库点一个 Star。
