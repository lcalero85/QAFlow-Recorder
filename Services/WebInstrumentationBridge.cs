using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class WebDomObservation
{
    public string EventType { get; set; } = "";
    public string Url { get; set; } = "";
    public string PageTitle { get; set; } = "";
    public long ClientTimestamp { get; set; }
    public string TagName { get; set; } = "";
    public string Text { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string AriaLabel { get; set; } = "";
    public string DataTestId { get; set; } = "";
    public string CssSelector { get; set; } = "";
    public string XPath { get; set; } = "";
    public string InputType { get; set; } = "";
    public string BrowserName { get; set; } = "";
    public string FrameUrl { get; set; } = "";
    public bool IsTopFrame { get; set; } = true;
}

/// <summary>
/// Local-only bridge used by the QAFlow browser instrumentation extension.
/// It never exposes a listener outside 127.0.0.1 and never accepts typed values.
/// </summary>
public sealed class WebInstrumentationBridge : IDisposable
{
    public const int DefaultPort = 47831;
    private readonly TcpListener _listener = new(IPAddress.Loopback, DefaultPort);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private AppSettings _settings;
    private readonly string _sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public event Action<WebDomObservation>? ObservationReceived;
    public static DateTimeOffset LastClientSeen { get; private set; }
    public static string LastClientBrowser { get; private set; } = "";
    public static bool IsRunning { get; private set; }

    public WebInstrumentationBridge(AppSettings settings) => _settings = settings;

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    public void Start()
    {
        if (_loop is not null) return;
        try
        {
            _listener.Start();
            IsRunning = true;
            _loop = Task.Run(AcceptLoopAsync);
        }
        catch (Exception ex)
        {
            IsRunning = false;
            TechnicalLogService.Error(nameof(WebInstrumentationBridge), "Start", ex);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { client?.Dispose(); TechnicalLogService.Warn(nameof(WebInstrumentationBridge), "AcceptLoop", ex.Message, ex); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true))
        {
            try
            {
                var requestLine = await reader.ReadLineAsync(token) ?? "";
                if (string.IsNullOrWhiteSpace(requestLine)) return;
                var parts = requestLine.Split(' ');
                var method = parts.ElementAtOrDefault(0) ?? "GET";
                var path = parts.ElementAtOrDefault(1) ?? "/";
                var contentLength = 0;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token)))
                {
                    var sep = line.IndexOf(':');
                    if (sep <= 0) continue;
                    var name = line[..sep].Trim();
                    var value = line[(sep + 1)..].Trim();
                    headers[name] = value;
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out contentLength);
                }
                headers.TryGetValue("Origin", out var origin);
                var allowedOrigin = IsAllowedOrigin(origin) ? origin : null;

                if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    if (allowedOrigin is null) { await WriteResponseAsync(stream, 403, "text/plain", "", token, null); return; }
                    await WriteResponseAsync(stream, 204, "text/plain", "", token, allowedOrigin);
                    return;
                }

                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/status", StringComparison.OrdinalIgnoreCase))
                {
                    if (allowedOrigin is null) { await WriteResponseAsync(stream, 403, "application/json", "{\"enabled\":false}", token, null); return; }
                    LastClientSeen = DateTimeOffset.Now;
                    var payload = JsonSerializer.Serialize(new
                    {
                        enabled = _settings.ProductMode == ProductMode.HybridTesting,
                        baseUrl = _settings.WebAutomationBaseUrl,
                        allowedDomains = _settings.WebAutomationAllowedDomains,
                        sessionToken = _sessionToken
                    });
                    await WriteResponseAsync(stream, 200, "application/json", payload, token, allowedOrigin);
                    return;
                }

                if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/event", StringComparison.OrdinalIgnoreCase))
                {
                    headers.TryGetValue("X-QAFlow-Token", out var clientToken);
                    if (_settings.ProductMode != ProductMode.HybridTesting || allowedOrigin is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(clientToken ?? ""), Encoding.UTF8.GetBytes(_sessionToken)))
                    {
                        await WriteResponseAsync(stream, 403, "application/json", "{\"accepted\":false}", token, allowedOrigin);
                        return;
                    }

                    var body = new char[Math.Clamp(contentLength, 0, 65536)];
                    var read = 0;
                    while (read < body.Length)
                    {
                        var n = await reader.ReadAsync(body.AsMemory(read, body.Length - read), token);
                        if (n == 0) break;
                        read += n;
                    }
                    var observation = JsonSerializer.Deserialize<WebDomObservation>(new string(body, 0, read), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (observation is not null && IsAllowedUrl(observation.Url))
                    {
                        LastClientSeen = DateTimeOffset.Now;
                        if (!string.IsNullOrWhiteSpace(observation.BrowserName)) LastClientBrowser = observation.BrowserName.Trim();
                        ObservationReceived?.Invoke(observation);
                    }
                    await WriteResponseAsync(stream, 200, "application/json", "{\"accepted\":true}", token, allowedOrigin);
                    return;
                }

                await WriteResponseAsync(stream, 404, "application/json", "{\"error\":\"not-found\"}", token, allowedOrigin);
            }
            catch (Exception ex) { TechnicalLogService.Warn(nameof(WebInstrumentationBridge), "HandleClient", ex.Message, ex); }
        }
    }

    private bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return IsAllowedUrl(uri.GetLeftPart(UriPartial.Authority));
    }

    private bool IsAllowedUrl(string candidate)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url)) return false;
        if (!Uri.TryCreate(_settings.WebAutomationBaseUrl, UriKind.Absolute, out var baseUrl)) return false;
        if (url.Host.Equals(baseUrl.Host, StringComparison.OrdinalIgnoreCase)) return true;
        var allowed = (_settings.WebAutomationAllowedDomains ?? "")
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeHost);
        return allowed.Any(host => url.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeHost(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var u)) return u.Host;
        return value.Trim().TrimStart('*', '.').TrimEnd('/');
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int code, string contentType, string body, CancellationToken token, string? allowedOrigin)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var status = code switch { 200 => "OK", 204 => "No Content", 403 => "Forbidden", 404 => "Not Found", _ => "OK" };
        var cors = string.IsNullOrWhiteSpace(allowedOrigin) ? "" : $"Access-Control-Allow-Origin: {allowedOrigin}\r\nVary: Origin\r\n";
        var header = $"HTTP/1.1 {code} {status}\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\n{cors}Access-Control-Allow-Headers: Content-Type, X-QAFlow-Token\r\nAccess-Control-Allow-Methods: GET,POST,OPTIONS\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
        if (bytes.Length > 0) await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        IsRunning = false;
        _cts.Dispose();
    }
}
