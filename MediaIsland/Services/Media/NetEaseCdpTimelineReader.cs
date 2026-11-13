using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MediaIsland.Services.Media;

/// <summary>
/// 直接问网易云自己的渲染进程要播放进度（走 CEF 的 DevTools 协议）。
///
/// 背景：网易云的 SMTC 时间轴恒为 0（Position/EndTime/LastUpdated 全是默认值），
/// UI Automation 树里也没有进度条控件，所以之前只能靠"数像素"。像素方案能被遮挡、
/// 皮肤、悬停样式、播放器卡顿干扰，维护成本极高。
///
/// MC 圈的网易云模组（以及 cloudmusic-desktop-mcp 这类项目）走的是另一条路：
/// 给 cloudmusic.exe 加 <c>--remote-debugging-address=127.0.0.1 --remote-debugging-port=9223</c>，
/// 然后用 CDP 在页面里执行一段 JS 把播放状态读出来。这条路完全不依赖窗口可见性，
/// 最小化、被别的窗口盖住、甚至切到别的虚拟桌面都照样能读到。
///
/// 本机实测（云音乐 v3.1.43.205627，CEF 91）拿进度只需要两样东西：
/// <list type="bullet">
/// <item><c>document.querySelector('[aria-label="播放进度调节"]')</c>：进度条元素，
/// 计算样式里的 <c>--track-precent</c> 就是当前百分比（0.1% 精度）；</item>
/// <item>进度条 thumb 里的隐藏提示文字 <c>"01:31 / 01:53"</c>：当前时间 / 总时长，
/// 精确到秒，而且不用悬停就一直更新。</item>
/// </list>
/// 两者都不依赖 hash 过的类名（那串 <c>TimeWarpper_t1eityq1</c> 会随版本变），
/// 所以这里按"aria-label + 文本形状"来定位，跨版本更稳。
/// </summary>
public sealed class NetEaseCdpTimelineReader : IDisposable
{
    /// <summary>一次成功读到的进度。</summary>
    /// <param name="Position">当前播放位置。</param>
    /// <param name="Duration">总时长，读不到时为 <see cref="TimeSpan.Zero"/>。</param>
    /// <param name="Ratio">进度条百分比（0~1）。</param>
    /// <param name="HasExactTime">是否拿到了"当前时间 / 总时长"文本（否则只有百分比）。</param>
    public readonly record struct CdpTimeline(TimeSpan Position, TimeSpan Duration, double Ratio, bool HasExactTime);

    /// <summary>网易云默认的 DevTools 端口（MCP 项目用的也是这个）。</summary>
    public const int DefaultDebuggingPort = 9223;

    private const string ProgressExpression =
        "(()=>{" +
        "const s=document.querySelector('[aria-label=\"播放进度调节\"]');" +
        "if(!s)return '';" +
        "let t='';" +
        "for(const e of s.querySelectorAll('*')){" +
        "if(e.children.length)continue;" +
        "const x=(e.textContent||'').trim();" +
        "if(x.indexOf(' / ')>0&&x.length<=16){t=x;break}" +
        "}" +
        "const cs=getComputedStyle(s);" +
        "return t+'|'+cs.getPropertyValue('--track-precent').trim();" +
        "})()";

    private static readonly TimeSpan EvaluateTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly HttpClient _http;
    private readonly List<int> _candidatePorts = new() { DefaultDebuggingPort, 9222 };
    private ClientWebSocket? _socket;
    private int _connectedPort;
    private string? _socketUrl;
    private int _nextRequestId;
    private long _nextDiscoveryTicks;
    private string? _lastFailureReason;

    public NetEaseCdpTimelineReader()
    {
        _http = new HttpClient
        {
            Timeout = DiscoveryTimeout
        };
    }

    /// <summary>最近一次读取失败的原因（成功时为 null）。</summary>
    public string? LastFailureReason
    {
        get
        {
            lock (_gate)
            {
                return _lastFailureReason;
            }
        }
    }

    /// <summary>当前是否已建立可用的 CDP 连接。</summary>
    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _socket is { State: WebSocketState.Open };
            }
        }
    }

    /// <summary>当前用于调试的端口（未连接时为 0）。</summary>
    public int ConnectedPort
    {
        get
        {
            lock (_gate)
            {
                return _connectedPort;
            }
        }
    }

    /// <summary>允许追加自定义端口（例如用户在设置里改了端口）。</summary>
    public void AddCandidatePort(int port)
    {
        if (port <= 0 || port > 65535)
        {
            return;
        }

        lock (_gate)
        {
            if (!_candidatePorts.Contains(port))
            {
                _candidatePorts.Insert(0, port);
            }
        }
    }

    /// <summary>
    /// 读一次进度。连不上 DevTools 端口时返回 false 并通过 <see cref="LastFailureReason"/> 说明原因。
    /// </summary>
    public async Task<CdpTimeline?> TryReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            var raw = await EvaluateAsync(ProgressExpression, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                Fail("页面上没有播放进度条（可能停在非播放页，或播放器还没初始化）");
                return null;
            }

            if (!TryParseProgress(raw, out var timeline, out var parseFailure))
            {
                Fail(parseFailure);
                return null;
            }

            lock (_gate)
            {
                _lastFailureReason = null;
            }

            return timeline;
        }
        catch (OperationCanceledException)
        {
            Fail("读取超时");
            DropConnection();
            return null;
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            DropConnection();
            return null;
        }
    }

    /// <summary>
    /// 探测本机是否有网易云的 DevTools 端口在监听（不需要真的建立连接，用于设置页提示）。
    /// </summary>
    public async Task<bool> IsDebuggingAvailableAsync(CancellationToken cancellationToken = default)
    {
        int[] ports;
        lock (_gate)
        {
            ports = _candidatePorts.ToArray();
        }

        foreach (var port in ports)
        {
            var target = await FindPageTargetAsync(port, cancellationToken).ConfigureAwait(false);
            if (target is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把 "01:31 / 01:53|96.5%" 解析成时间。</summary>
    internal static bool TryParseProgress(string raw, out CdpTimeline timeline, out string failureReason)
    {
        timeline = default;
        failureReason = "进度文本格式不认识";

        var parts = raw.Split('|');
        var clockText = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var percentText = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        var ratio = ParsePercent(percentText);
        var hasExactTime = false;
        var position = TimeSpan.Zero;
        var duration = TimeSpan.Zero;

        var slash = clockText.IndexOf(" / ", StringComparison.Ordinal);
        if (slash > 0)
        {
            var left = clockText[..slash].Trim();
            var right = clockText[(slash + 3)..].Trim();
            if (TryParseClock(left, out var parsedPosition) && TryParseClock(right, out var parsedDuration) &&
                parsedDuration > TimeSpan.Zero)
            {
                position = parsedPosition;
                duration = parsedDuration;
                hasExactTime = true;
                if (ratio <= 0 || double.IsNaN(ratio))
                {
                    ratio = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0d, 1d);
                }
            }
        }

        // 注意 NaN：`ratio <= 0` 对 NaN 为 false，会把"什么都没有"当成成功，所以这里用 `!(ratio > 0)`。
        if (!hasExactTime && !(ratio > 0))
        {
            return false;
        }

        timeline = new CdpTimeline(position, duration, Math.Clamp(ratio, 0d, 1d), hasExactTime);
        failureReason = string.Empty;
        return true;
    }

    /// <summary>解析 "96.5%" / "0.965" 这类百分比文本。</summary>
    internal static double ParsePercent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return double.NaN;
        }

        var trimmed = text.Trim();
        if (trimmed.EndsWith('%'))
        {
            trimmed = trimmed[..^1];
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value / 100d
            : double.NaN;
    }

    /// <summary>解析 "01:31" 或 "1:02:03"。</summary>
    internal static bool TryParseClock(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var chunks = text.Split(':');
        if (chunks.Length is < 2 or > 3)
        {
            return false;
        }

        var seconds = 0d;
        var scale = 1d;
        for (var i = chunks.Length - 1; i >= 0; i--)
        {
            if (!double.TryParse(chunks[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var part) ||
                part < 0)
            {
                return false;
            }

            seconds += part * scale;
            scale *= 60d;
        }

        value = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private void Fail(string reason)
    {
        lock (_gate)
        {
            _lastFailureReason = reason;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ClientWebSocket? socket;
        string? url;
        int port;
        lock (_gate)
        {
            socket = _socket;
            url = _socketUrl;
            port = _connectedPort;
        }

        if (socket is { State: WebSocketState.Open } && url is not null)
        {
            return;
        }

        DropConnection();

        // 连不上时限速重试，别每个 tick 都去翻端口。
        var now = Stopwatch.GetTimestamp();
        long next;
        lock (_gate)
        {
            next = _nextDiscoveryTicks;
        }

        if (now < next)
        {
            throw new InvalidOperationException(_lastFailureReason ?? "DevTools 端口未连接");
        }

        lock (_gate)
        {
            _nextDiscoveryTicks = now + (long)(ReconnectInterval.TotalSeconds * Stopwatch.Frequency);
        }

        int[] ports;
        lock (_gate)
        {
            ports = _candidatePorts.ToArray();
        }

        foreach (var candidate in ports)
        {
            var target = await FindPageTargetAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                continue;
            }

            var client = new ClientWebSocket();
            client.Options.SetRequestHeader("Origin", "http://127.0.0.1");
            using var connectTimeout = new CancellationTokenSource(EvaluateTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectTimeout.Token);
            await client.ConnectAsync(new Uri(target), linked.Token).ConfigureAwait(false);

            lock (_gate)
            {
                _socket = client;
                _socketUrl = target;
                _connectedPort = candidate;
                _lastFailureReason = null;
                _nextDiscoveryTicks = 0;
            }

            return;
        }

        throw new InvalidOperationException(
            $"没有找到网易云 DevTools 端口（已试 {string.Join('/', ports)}）。需要在启动 cloudmusic.exe 时加 --remote-debugging-port={DefaultDebuggingPort}");
    }

    private async Task<string?> FindPageTargetAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http
                .GetAsync($"http://127.0.0.1:{port}/json", cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? fallback = null;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("webSocketDebuggerUrl", out var ws) ||
                    ws.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var url = ws.GetString();
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                var pageUrl = item.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                if (type == "page" && pageUrl is not null &&
                    pageUrl.StartsWith("orpheus://", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                fallback ??= url;
            }

            return fallback;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<string> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        ClientWebSocket socket;
        int requestId;
        lock (_gate)
        {
            socket = _socket ?? throw new InvalidOperationException("CDP 未连接");
            requestId = ++_nextRequestId;
        }

        var payload = JsonSerializer.Serialize(new
        {
            id = requestId,
            method = "Runtime.evaluate",
            @params = new
            {
                expression,
                returnByValue = true,
                awaitPromise = false
            }
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(EvaluateTimeout);
        var token = timeout.Token;

        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token).ConfigureAwait(false);

        var buffer = new byte[8192];
        var builder = new StringBuilder();
        while (true)
        {
            builder.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("CDP 连接被对端关闭");
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(builder.ToString());
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id) || id != requestId)
            {
                // 事件推送（Runtime.consoleAPICalled 之类）直接跳过。
                continue;
            }

            if (!root.TryGetProperty("result", out var resultElement) ||
                !resultElement.TryGetProperty("result", out var valueElement))
            {
                return string.Empty;
            }

            return valueElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }

    private void DropConnection()
    {
        ClientWebSocket? socket;
        lock (_gate)
        {
            socket = _socket;
            _socket = null;
            _socketUrl = null;
            _connectedPort = 0;
        }

        if (socket is null)
        {
            return;
        }

        try
        {
            socket.Dispose();
        }
        catch (Exception)
        {
            // 忽略关闭期的异常。
        }
    }

    public void Dispose()
    {
        DropConnection();
        _http.Dispose();
    }
}
