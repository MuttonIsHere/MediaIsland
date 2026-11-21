# MediaIsland CE v1.2.1（ClassIsland 2.1.0.1 兼容版）

> `compat/classisland-2.1.0.1` 分支的重新打包版，替换 `v1.2.1-CE-CI2101`。
>
> 上一版的 `.cipx` 里混进了 `publish/` 子目录，还带着一份更早构建的 `MediaIsland.dll`
> （整包 29 个条目、3.2 MB）。本版修正为干净的 16 个条目。

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
此次会话已加载插件:…,MediaIsland(bywhite.mediaisland.ce,1.2.1),…
MediaIsland.Services.PluginStartupLog|MediaIsland v1.2.1 已加载
```

## 本版改了什么

- **打包**：`CleanStalePublishDir` 目标会在 `CreateCipx` 之前清掉 `bin\<配置>\<TFM>\publish\`，
  避免上一轮 `dotnet publish` 的残留被一起压进包（这正是上一版包里出现两份 DLL 的原因）。
- **插件 id** 固定为 `bywhite.mediaisland.ce`，与安装目录、设置目录一致，不会再装出两份 MediaIsland。
- **版本号**对齐到 1.2.1（此前 `manifest.yml` 是 1.2.1、程序集还是 1.2.0）。
- 新增 `Tools/Pack-Cipx.ps1` 打包脚本，打包后直接打印 SHA256 / MD5。

## 校验

包体积约 1.6 MB、16 个条目。SHA256 / MD5 见本 Release 附件 `checksums.md`：

```
unzip -l MediaIsland.cipx    # 应为 16 files，且不含 publish/ 前缀
```

## 环境要求

- ClassIsland **2.1.0.1**（`net8.0`，`apiVersion 2.1.0.1`）；2.2 预览版请用 `1.2.0` 那个包。
- Windows 10 Build 17763 (1809) 或以上。
- 网易云精确同步需要通过插件提供的按钮/快捷方式启动网易云（带 `--remote-debugging-port=9223`）。
