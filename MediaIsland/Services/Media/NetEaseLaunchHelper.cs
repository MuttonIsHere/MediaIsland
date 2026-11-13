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
        return $"{executableText}；{portText}";
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
    private static string BuildDebugArguments(string existingArguments, int port)
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
