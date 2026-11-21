# MediaIsland CE v1.2.2（ClassIsland 2.1.0.1 兼容版）

> 取代 `v1.2.1-CE-CI2101-r2` 与 `v1.2.1-CE-CI2101`。装过 r2 的可以直接覆盖，设置不变。
>
> 1.2.1 系列修的是「装不上 / 组件不显示」；1.2.2 修的是运行期崩溃点和打包工具链本身。

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

**修：QQ 音乐退出时抛 `NullReferenceException`，整条媒体快照被丢弃**

`WindowsSmtcMediaSessionProvider.BuildSnapshotAsync` 直接解引用
`controlSession.GetTimelineProperties()` / `GetPlaybackInfo()` 的返回值。WinRT 在会话销毁过程中
会返回 `null`，于是抛出 NRE 被外层 catch 吞掉，日志里留下一条带堆栈的告警，那一下媒体信息闪断。
现在时间轴退回零、播放状态退回 `Unknown`，**标题和封面照常显示**；`WindowsMediaController` 里同类的
两处解引用也补了空值防护（同仓库 `WindowsSmtcMediaPlaybackController` 早就用了 `?.`，只是这三处漏了）。

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
