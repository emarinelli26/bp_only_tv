using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BigPictureTV.Core;

namespace BigPictureTV.App;

/// <summary>
/// Controls the page in a TV menu browser window through the browser's
/// DevTools protocol: adjusts what the page sees before it loads (so YouTube
/// shows its TV interface) and types the controller's keys straight into it,
/// whichever window is in front. Only the menu's own browser profiles open
/// this channel, on a local port the browser picks.
/// </summary>
sealed class CdpPage : IDisposable
{
    readonly ClientWebSocket _socket = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    readonly CancellationTokenSource _stop = new();
    readonly ILog _log;
    string? _session;
    int _nextId;

    /// <summary>Raised (on a background thread) when the browser goes away.</summary>
    public event Action? Closed;

    CdpPage(ILog log) => _log = log;

    /// <summary>
    /// Waits for the browser started on <paramref name="profileDir"/> to open
    /// its DevTools port, attaches to its page and returns it. Null if it
    /// never shows up.
    /// </summary>
    public static async Task<CdpPage?> ConnectAsync(string profileDir, ILog log, TimeSpan wait)
    {
        string portFile = Path.Combine(profileDir, "DevToolsActivePort");
        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            string[] lines;
            try { lines = File.Exists(portFile) ? await File.ReadAllLinesAsync(portFile) : Array.Empty<string>(); }
            catch (IOException) { lines = Array.Empty<string>(); }
            if (lines.Length >= 2 && int.TryParse(lines[0], out int port))
            {
                var page = new CdpPage(log);
                try
                {
                    await page.StartAsync(new Uri($"ws://127.0.0.1:{port}{lines[1].Trim()}"));
                    if (await page.AttachToPageAsync()) return page;
                }
                catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
                {
                    log.Write($"DevTools connection failed: {e.Message}");
                }
                page.Dispose();
            }
            await Task.Delay(300);
        }
        return null;
    }

    async Task StartAsync(Uri browser)
    {
        await _socket.ConnectAsync(browser, _stop.Token);
        _ = Task.Run(ReceiveLoop);
    }

    async Task<bool> AttachToPageAsync()
    {
        // The page target can take a moment to appear after the browser starts.
        for (int i = 0; i < 20; i++)
        {
            var targets = await SendAsync("Target.getTargets", null, browserLevel: true);
            var pages = (targets?["targetInfos"]?.AsArray() ?? new JsonArray())
                .Where(t => (string?)t?["type"] == "page")
                .OrderByDescending(t => ((string?)t?["url"] ?? "").StartsWith("http", StringComparison.OrdinalIgnoreCase)); // the web page, not a browser page
            foreach (var t in pages)
            {
                var attached = await SendAsync("Target.attachToTarget",
                    new JsonObject { ["targetId"] = (string?)t!["targetId"], ["flatten"] = true }, browserLevel: true);
                _session = (string?)attached?["sessionId"];
                if (_session != null) return true;
            }
            await Task.Delay(250);
        }
        return false;
    }

    /// <summary>Makes the page see a TV browser: user agent, platform, and no desktop client hints.</summary>
    public async Task PretendToBeTvAsync(string userAgent)
    {
        await SendAsync("Emulation.setUserAgentOverride", new JsonObject { ["userAgent"] = userAgent, ["platform"] = "" });
        string ua = JsonSerializer.Serialize(userAgent);
        await SendAsync("Page.addScriptToEvaluateOnNewDocument", new JsonObject
        {
            ["source"] = "(() => { const ua = " + ua + "; const set = (k, v) => { try { Object.defineProperty(Navigator.prototype, k, { get: () => v, configurable: true }); } catch (e) {} };" +
                         " set('userAgent', ua); set('appVersion', ua.slice(ua.indexOf('/') + 1)); set('platform', ''); set('userAgentData', undefined); })();",
        });
    }

    /// <summary>Runs a script in the page now and in every page it loads later.</summary>
    public async Task InjectAsync(string script)
    {
        await SendAsync("Page.addScriptToEvaluateOnNewDocument", new JsonObject { ["source"] = script });
        await SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = script });
    }

    public Task NavigateAsync(string url) => SendAsync("Page.navigate", new JsonObject { ["url"] = url });

    public Task GoBackAsync() => SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = "history.back()" });

    /// <summary>Runs JavaScript in the page and returns its value as text (null if none).</summary>
    public async Task<string?> EvaluateAsync(string expression)
    {
        var result = await SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true });
        return result?["result"]?["value"]?.ToString();
    }

    public Task BringToFrontAsync() => SendAsync("Page.bringToFront", null);

    /// <summary>Presses and releases a key in the page. <paramref name="modifiers"/>: Alt 1, Ctrl 2, Meta 4, Shift 8.</summary>
    public async Task PressAsync(string key, string code, int virtualKey, int modifiers, string? text = null)
    {
        var down = new JsonObject
        {
            ["type"] = text == null ? "rawKeyDown" : "keyDown", ["key"] = key, ["code"] = code,
            ["windowsVirtualKeyCode"] = virtualKey, ["modifiers"] = modifiers,
        };
        if (text != null) down["text"] = text;
        await SendAsync("Input.dispatchKeyEvent", down);
        await SendAsync("Input.dispatchKeyEvent", new JsonObject
        {
            ["type"] = "keyUp", ["key"] = key, ["code"] = code, ["windowsVirtualKeyCode"] = virtualKey, ["modifiers"] = modifiers,
        });
    }

    readonly SemaphoreSlim _sending = new(1, 1);

    async Task<JsonNode?> SendAsync(string method, JsonObject? parameters, bool browserLevel = false)
    {
        int id = Interlocked.Increment(ref _nextId);
        var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
        if (!browserLevel && _session != null) message["sessionId"] = _session;
        var reply = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = reply;
        await _sending.WaitAsync(_stop.Token); // a WebSocket takes one message at a time
        try
        {
            await _socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, _stop.Token);
        }
        finally
        {
            _sending.Release();
        }
        var done = await Task.WhenAny(reply.Task, Task.Delay(TimeSpan.FromSeconds(10), _stop.Token));
        _pending.TryRemove(id, out _);
        if (done != reply.Task) throw new OperationCanceledException($"No answer to {method}.");
        return await reply.Task;
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[64 * 1024];
        var text = new StringBuilder();
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;
                var node = JsonNode.Parse(text.ToString());
                text.Clear();
                if (node?["id"] is JsonNode idNode && _pending.TryGetValue((int)idNode, out var waiter))
                {
                    if (node["error"] is JsonNode error) waiter.TrySetException(new InvalidOperationException((string?)error["message"] ?? "DevTools error"));
                    else waiter.TrySetResult(node["result"]);
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or JsonException or ObjectDisposedException)
        {
        }
        foreach (var waiter in _pending.Values) waiter.TrySetCanceled();
        if (!_stop.IsCancellationRequested)
        {
            _log.Write("The browser closed its DevTools connection.");
            Closed?.Invoke();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _socket.Dispose();
    }
}
