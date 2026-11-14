using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MediaIsland.Services.Media;

/// <summary>
/// 帮网易云带上 DevTools 端口启动的小工具。
///
/// 网易云只有在启动时才会打开 <c>--remote-debugging-port</c>，运行起来之后没有任何办法再打开它，
/// 所以想让 <see cref="NetEaseCdpTimelineReader"/> 拿到精确进度，就必须（重启一次）用它带参数启动。
/// 这个类只负责"找到 cloudmusic.exe"和"带参数启动它"：不改快捷方式、不写注册表、不留任何本机专属路径。
/// </summary>
public static class NetEaseLaunchHelper
{
    private const string ExecutableName = "cloudmusic.exe";

    /// <summary>桌面「精确同步」快捷方式的名字（不含 .lnk）。</summary>
    private const string ShortcutName = "网易云音乐（精确同步）";

    private static int CountProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName("cloudmusic");
            var count = processes.Length;
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return count;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>开始菜单 + 任务栏固定区的快捷方式（不含桌面，桌面上的原快捷方式一律不动）。</summary>
    private static IEnumerable<string> EnumerateInstalledShortcutFiles()
    {
        var roots = new List<string>();
        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    roots.Add(path);
                }
            }
            catch (Exception)
            {
                // 忽略。
            }
        }

        Add(Environment.SpecialFolder.Programs);
        Add(Environment.SpecialFolder.CommonPrograms);

        try
        {
            var pinned = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(pinned))
            {
                var taskbar = Path.Combine(pinned, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                if (Directory.Exists(taskbar))
                {
                    roots.Add(taskbar);
                }
            }
        }
        catch (Exception)
        {
            // 忽略。
        }

        foreach (var root in roots)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static string? ReadLinkTarget(string link)
    {
        return WithShortcut(link, shortcut => shortcut.TargetPath as string);
    }

    private static string? ReadLinkArguments(string link)
    {
        return WithShortcut(link, shortcut => shortcut.Arguments as string);
    }

    private static bool WriteLinkArguments(string link, string? existingArguments, int port)
    {
        // WScript.Shell 只在 Windows 上存在（CA1416：显式守卫，避免平台分析器报警）。
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.Arguments = BuildDebugArguments(existingArguments ?? string.Empty, port);
            shortcut.Save();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static T? WithShortcut<T>(string link, Func<dynamic, T?> read)
    {
        // WScript.Shell 只在 Windows 上存在（CA1416：显式守卫，避免平台分析器报警）。
        if (!OperatingSystem.IsWindows())
        {
            return default;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return default;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            return read(shortcut);
        }
        catch (Exception)
        {
            return default;
        }
    }

    /// <summary>
    /// 找 cloudmusic.exe。顺序：正在运行的进程 → 快捷方式指向（只读） → 默认安装目录。
    /// 都找不到时返回 <see langword="null"/>，调用方应提示用户手动带参数启动。
    /// </summary>
    public static string? FindExecutable()
    {
        foreach (var process in Process.GetProcessesByName("cloudmusic"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (IsExecutable(path))
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // 32 位进程的 MainModule 可能读不到，继续找下一个。
            }
            finally
            {
                process.Dispose();
            }
        }

        var fromShortcut = FindExecutableFromShortcuts();
        if (fromShortcut is not null)
        {
            return fromShortcut;
        }

        foreach (var candidate in EnumerateDefaultInstallPaths())
        {
            if (IsExecutable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 关掉正在跑的网易云，再带上 DevTools 端口重新启动。
    /// 返回值表示是否已经把新进程拉起来（不保证 DevTools 一定可用，调用方还要自行探测）。
    /// </summary>
    public static bool TryRelaunchWithDebugging(int port, out string message)
    {
        if (!IsValidPort(port))
        {
            message = $"端口 {port} 不合法";
            return false;
        }

        var exe = FindExecutable();
        if (exe is null)
        {
            message = "没找到 cloudmusic.exe（请手动用带 --remote-debugging-port 的快捷方式启动网易云）";
            return false;
        }

        var stopped = 0;
        foreach (var process in Process.GetProcessesByName("cloudmusic"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    stopped++;
                }
            }
            catch (Exception)
            {
                // 忽略单个进程的失败。
            }
            finally
            {
                process.Dispose();
            }
        }

        // 等一下让旧进程彻底退出，否则新进程可能复用旧单例、把端口参数丢掉。
        if (stopped > 0)
        {
            System.Threading.Thread.Sleep(1500);
        }

        if (!TryLaunch(exe, port, out var launchError))
        {
            message = launchError;
            return false;
        }

        message = stopped > 0
            ? $"已重启网易云（结束 {stopped} 个进程）并打开调试端口 {port}"
            : $"已启动网易云并打开调试端口 {port}";
        return true;
    }

    /// <summary>
    /// 直接带上 DevTools 端口启动网易云（不结束已有进程）。
    /// 网易云没开的时候用它；已经开着时新进程只会把旧窗口叫出来，那种情况应该用
    /// <see cref="TryRelaunchWithDebugging"/>。
    /// </summary>
    public static bool TryLaunchWithDebugging(int port, out string message)
    {
        if (!IsValidPort(port))
        {
            message = $"端口 {port} 不合法";
            return false;
        }

        var exe = FindExecutable();
        if (exe is null)
        {
            message = "没找到 cloudmusic.exe（请手动用带 --remote-debugging-port 的快捷方式启动网易云）";
            return false;
        }

        if (!TryLaunch(exe, port, out var launchError))
        {
            message = launchError;
            return false;
        }

        message = $"已启动网易云（带调试端口 {port}）";
        return true;
    }

    /// <summary>
    /// 网易云是不是开着、但**没有**带调试端口。设置页据此决定是直接启动还是提示用户重启。
    /// </summary>
    public static bool IsRunningWithoutDebugging(int port, out int processCount)
    {
        processCount = CountProcesses();
        return processCount > 0 && !IsPortListening(port);
    }

    /// <summary>
    /// 在桌面**新建**一个「网易云音乐（精确同步）」快捷方式（只写这一个文件，不动用户的任何原有快捷方式）。
    /// 已存在同名快捷方式时直接更新它，这样重复点击不会堆出一堆副本。
    /// </summary>
    public static bool TryCreateShortcut(int port, out string shortcutPath, out string message)
    {
        shortcutPath = string.Empty;
        if (!IsValidPort(port))
        {
            message = $"端口 {port} 不合法";
            return false;
        }

        var exe = FindExecutable();
        if (exe is null)
        {
            message = "没找到 cloudmusic.exe，无法创建快捷方式";
            return false;
        }

        // 快捷方式靠 WScript.Shell 写，只有 Windows 有（CA1416）。
        if (!OperatingSystem.IsWindows())
        {
            message = "只有 Windows 才能创建桌面快捷方式";
            return false;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
        {
            message = "找不到桌面目录，无法创建快捷方式";
            return false;
        }

        var link = Path.Combine(desktop, $"{ShortcutName}.lnk");
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                message = "当前系统没有 WScript.Shell，无法创建快捷方式";
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = exe;
            shortcut.Arguments = BuildDebugArguments(string.Empty, port);
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
            shortcut.Description = "网易云音乐（带 DevTools 调试端口，供 MediaIsland 精确同步歌词）";
            shortcut.Save();
        }
        catch (Exception ex)
        {
            message = $"创建快捷方式失败：{ex.Message}";
            return false;
        }

        shortcutPath = link;
        message = $"已在桌面创建「{ShortcutName}」，以后用它启动网易云就能精确同步";
        return true;
    }

    /// <summary>
    /// 给**开始菜单 / 任务栏**里指向 cloudmusic.exe 的快捷方式补上调试参数（桌面上的原有快捷方式不动）。
    /// 改之前会把原文件备份成 <c>*.lnk.bak-yyyyMMdd-HHmmss</c>，随时可以还原。
    /// </summary>
    public static bool TryPatchInstalledShortcuts(int port, out string message)
    {
        if (!IsValidPort(port))
        {
            message = $"端口 {port} 不合法";
            return false;
        }

        var patched = new List<string>();
        var already = 0;
        var failed = new List<string>();
        foreach (var link in EnumerateInstalledShortcutFiles())
        {
            try
            {
                if (ReadLinkTarget(link) is not { } target || !IsExecutable(target))
                {
                    continue;
                }

                if (ShortcutNeedsPatch(ReadLinkArguments(link), port))
                {
                    var backup = $"{link}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
                    File.Copy(link, backup, overwrite: true);
                    if (!WriteLinkArguments(link, ReadLinkArguments(link), port))
                    {
                        failed.Add(Path.GetFileName(link));
                        continue;
                    }

                    patched.Add(Path.GetFileName(link));
                }
                else
                {
                    already++;
                }
            }
            catch (Exception)
            {
                failed.Add(Path.GetFileName(link));
            }
        }

        if (patched.Count == 0 && already == 0 && failed.Count == 0)
        {
            message = "没找到指向 cloudmusic.exe 的开始菜单 / 任务栏快捷方式，可用「启动网易云（精确同步版）」或桌面快捷方式";
            return false;
        }

        message = patched.Count > 0
            ? $"已给 {patched.Count} 个快捷方式补上调试端口（原文件已备份）：{string.Join('、', patched)}"
            : "开始菜单 / 任务栏的快捷方式本来就带调试端口参数，不用改";
        if (failed.Count > 0)
        {
            message += $"；{failed.Count} 个没改成功（可能没有权限）：{string.Join('、', failed)}";
        }

        return patched.Count > 0 || failed.Count == 0;
    }

    /// <summary>快捷方式现有参数是否还需要补调试端口。</summary>
    internal static bool ShortcutNeedsPatch(string? existingArguments, int port)
    {
        return existingArguments is null ||
               !existingArguments.Contains($"--remote-debugging-port={port}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>探测本机端口上有没有东西在监听（不做完整握手，只看 TCP 能否连上）。</summary>
    public static bool IsPortListening(int port)
    {
        if (!IsValidPort(port))
        {
            return false;
        }

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var task = client.ConnectAsync("127.0.0.1", port);
            return task.Wait(TimeSpan.FromMilliseconds(400)) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>描述一下当前状态，给设置页显示用。</summary>
    public static string DescribeState(int port)
    {
        var exe = FindExecutable();
        var executableText = exe is null ? "找不到 cloudmusic.exe" : $"网易云：{exe}";
        var portText = IsPortListening(port) ? $"调试端口 {port} 在监听" : $"调试端口 {port} 未打开";
        var count = CountProcesses();
        var runningText = count == 0
            ? "网易云没在运行"
            : IsPortListening(port)
                ? $"网易云正在运行（{count} 个进程，精确同步可用）"
                : $"网易云正在运行（{count} 个进程）但没带调试端口，歌词只能推算";
        return $"{runningText}；{executableText}；{portText}";
    }

    /// <summary>列出正在跑的网易云进程号，方便日志排查。</summary>
    public static string DescribeProcesses()
    {
        try
        {
            var ids = new List<string>();
            foreach (var process in Process.GetProcessesByName("cloudmusic"))
            {
                try
                {
                    ids.Add(process.Id.ToString());
                }
                finally
                {
                    process.Dispose();
                }
            }

            return ids.Count == 0 ? "无" : string.Join(',', ids);
        }
        catch (Exception)
        {
            return "未知";
        }
    }

    /// <summary>
    /// 调试参数。已有参数（例如用户自己的 <c>--disable-gpu</c>）会保留在前面。
    /// </summary>
    internal static string BuildDebugArguments(string existingArguments, int port)
    {
        var flags = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port}";
        return string.IsNullOrWhiteSpace(existingArguments)
            ? flags
            : $"{existingArguments.Trim()} {flags}";
    }

    private static bool TryLaunch(string executable, int port, out string error)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable, BuildDebugArguments(string.Empty, port))
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
            };
            Process.Start(startInfo);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"启动网易云失败：{ex.Message}";
            return false;
        }
    }

    private static bool IsValidPort(int port)
    {
        return port is > 0 and <= 65535;
    }

    private static bool IsExecutable(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               File.Exists(path) &&
               string.Equals(Path.GetFileName(path), ExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从桌面 / 开始菜单的快捷方式里**只读**地找安装位置（不改任何快捷方式）。
    /// 这样可以兼容装在任意盘符 / 自定义目录的情况，又不需要写死路径。
    /// </summary>
    private static string? FindExecutableFromShortcuts()
    {
        // 读快捷方式要 WScript.Shell，只有 Windows 有（CA1416）。
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var link in EnumerateShortcutFiles())
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                {
                    return null;
                }

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(link);
                var target = shortcut.TargetPath as string;
                if (IsExecutable(target))
                {
                    return target;
                }
            }
            catch (Exception)
            {
                // 单个快捷方式读不出来不影响其它。
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateShortcutFiles()
    {
        var roots = new List<string>();
        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    roots.Add(path);
                }
            }
            catch (Exception)
            {
                // 忽略。
            }
        }

        Add(Environment.SpecialFolder.DesktopDirectory);
        Add(Environment.SpecialFolder.CommonDesktopDirectory);
        Add(Environment.SpecialFolder.Programs);
        Add(Environment.SpecialFolder.CommonPrograms);

        foreach (var root in roots)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>默认安装目录（都是通用位置，不含任何本机专属路径）。</summary>
    private static IEnumerable<string> EnumerateDefaultInstallPaths()
    {
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.LocalApplicationData
                 })
        {
            string root;
            try
            {
                root = Environment.GetFolderPath(folder);
            }
            catch (Exception)
            {
                continue;
            }

            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            yield return Path.Combine(root, "Netease", "CloudMusic", ExecutableName);
            yield return Path.Combine(root, "NetEase", "CloudMusic", ExecutableName);
        }
    }
}
