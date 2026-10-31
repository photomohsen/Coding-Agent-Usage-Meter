// PROTOTYPE: throwaway. Codex Usage from ChatGPT's usage endpoint, with the Codex token read-only; the Codex session
// logs when that fails (ADR 0002).
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UsageWidget.Prototype;

static class CodexUsageClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly string CodexHome =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    static readonly string AuthPath = Path.Combine(CodexHome, "auth.json");
    static readonly string SessionsPath = Path.Combine(CodexHome, "sessions");

    /// No Codex on this machine: the widget shows Claude only.
    public static bool Installed => Directory.Exists(CodexHome);

    /// AsOf is when the Usage was true: now for the endpoint, the time of the log entry otherwise.
    /// An Error with a Usage means the Usage came from the logs and is Stale.
    public static async Task<(Usage? Usage, DateTime? AsOf, string? Error)> FetchAsync()
    {
        var (usage, err) = await FromEndpoint();
        if (usage != null) return (usage, DateTime.Now, null);
        var (logged, at) = FromLogs();
        return (logged, at, err);
    }

    static async Task<(Usage?, string?)> FromEndpoint()
    {
        if (!File.Exists(AuthPath)) return (null, "Sign in to Codex first");

        string token, account;
        try
        {
            // Share everything so Codex can keep writing the file while we read it. Never write it.
            using var stream = new FileStream(AuthPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = await JsonDocument.ParseAsync(stream);
            var tokens = doc.RootElement.GetProperty("tokens");
            token = tokens.GetProperty("access_token").GetString()!;
            account = tokens.GetProperty("account_id").GetString()!;
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, "Sign in to Codex with ChatGPT");
        }
        if (Expiry(token) is { } exp && exp < DateTimeOffset.UtcNow) return (null, "Token expired: open Codex once to refresh it");

        var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("ChatGPT-Account-Id", account);
        req.Headers.UserAgent.ParseAdd("claude-usage-widget-prototype");

        try
        {
            using var res = await Http.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.Unauthorized) return (null, "Open Codex to sign in again");
            if (res.StatusCode == HttpStatusCode.TooManyRequests) return (null, "Rate limited");
            if (!res.IsSuccessStatusCode) return (null, $"Offline (HTTP {(int)res.StatusCode})");

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var limits = doc.RootElement.GetProperty("rate_limit");
            return (Read(limits, "primary_window", "secondary_window"), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (null, "Offline");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, "Unexpected answer");
        }
    }

    /// The newest rate_limits entry Codex logged. It only moves while Codex is used.
    static (Usage?, DateTime?) FromLogs()
    {
        if (!Directory.Exists(SessionsPath)) return (null, null);
        var newest = Directory.EnumerateFiles(SessionsPath, "rollout-*.jsonl", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).Take(5);
        foreach (var file in newest)
        {
            string? line = null;
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } l)
                    if (l.Contains("\"rate_limits\":{")) line = l;
                if (line == null) continue;

                using var doc = JsonDocument.Parse(line);
                var at = doc.RootElement.GetProperty("timestamp").GetDateTimeOffset().LocalDateTime;
                var limits = doc.RootElement.GetProperty("payload").GetProperty("rate_limits");
                return (Read(limits, "primary", "secondary"), at);
            }
            catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        return (null, null);
    }

    /// Both shapes carry used_percent and reset_at (Unix seconds). A Window whose Reset has passed is back at zero.
    static Usage Read(JsonElement limits, string session, string weekly)
    {
        var (sPct, sReset) = Window(limits.GetProperty(session));
        var (wPct, wReset) = Window(limits.GetProperty(weekly));
        return new Usage(sPct, sReset, wPct, wReset);
    }

    static (double, DateTimeOffset?) Window(JsonElement w)
    {
        if (w.ValueKind != JsonValueKind.Object) return (0, null);
        var pct = w.GetProperty("used_percent").GetDouble();
        DateTimeOffset? reset = w.TryGetProperty("reset_at", out var r) && r.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()) : null;
        return reset < DateTimeOffset.UtcNow ? (0, null) : (pct, reset);
    }

    /// The exp claim of the JWT access token; null if it can't be read (the server then decides).
    static DateTimeOffset? Expiry(string jwt)
    {
        try
        {
            var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(part));
            return DateTimeOffset.FromUnixTimeSeconds(doc.RootElement.GetProperty("exp").GetInt64());
        }
        catch { return null; }
    }
}
