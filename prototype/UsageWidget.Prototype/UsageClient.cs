// PROTOTYPE: throwaway. Reads the Claude Code token read-only and asks for Usage (ADR 0001).
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UsageWidget.Prototype;

// A Reset is null while that window hasn't started, e.g. no Session since the last one ended.
record Usage(double SessionPct, DateTimeOffset? SessionReset, double WeeklyPct, DateTimeOffset? WeeklyReset);

static class UsageClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly string CredentialsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    /// Set by the last FetchAsync when the server asked to wait before the next Update.
    public static TimeSpan? RetryAfter { get; private set; }

    public static async Task<(Usage? Usage, string? Error)> FetchAsync()
    {
        RetryAfter = null;
        if (!File.Exists(CredentialsPath)) return (null, "Sign in to Claude Code first");

        string token;
        long expiresAt;
        // Share everything so Claude Code can keep writing the file while we read it. Never write it.
        using (var stream = new FileStream(CredentialsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var doc = await JsonDocument.ParseAsync(stream))
        {
            var oauth = doc.RootElement.GetProperty("claudeAiOauth");
            token = oauth.GetProperty("accessToken").GetString()!;
            expiresAt = oauth.GetProperty("expiresAt").GetInt64();
        }
        if (DateTimeOffset.FromUnixTimeMilliseconds(expiresAt) < DateTimeOffset.UtcNow)
            return (null, "Open Claude Code to sign in again");

        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        req.Headers.UserAgent.ParseAdd("claude-usage-widget-prototype");

        try
        {
            using var res = await Http.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.Unauthorized) return (null, "Open Claude Code to sign in again");
            // Too many Updates close together (e.g. several restarts): wait as long as the server asks.
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                RetryAfter = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(2);
                return (null, "Rate limited");
            }
            if (!res.IsSuccessStatusCode) return (null, $"Offline (HTTP {(int)res.StatusCode})");

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var five = doc.RootElement.GetProperty("five_hour");
            var seven = doc.RootElement.GetProperty("seven_day");
            return (new Usage(
                five.GetProperty("utilization").GetDouble(),
                Reset(five),
                seven.GetProperty("utilization").GetDouble(),
                Reset(seven)), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (null, "Offline");
        }
    }

    static DateTimeOffset? Reset(JsonElement window) =>
        window.TryGetProperty("resets_at", out var at) && at.GetString() is { } s ? DateTimeOffset.Parse(s) : null;
}
