using System.Text.Json;

namespace CindrixVPN.Services;

public sealed record ServerStats(int Load, long Blocked, long Queries);

/// <summary>Reads the server's small stats endpoint, which is only reachable through the tunnel.</summary>
public sealed class StatsClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task<ServerStats?> GetAsync(string gateway)
    {
        try
        {
            await using var stream = await _http.GetStreamAsync($"http://{gateway}:8088/stats");
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            return new ServerStats(
                (int)Math.Round(root.GetProperty("load").GetDouble()),
                root.TryGetProperty("blocked", out var b) ? b.GetInt64() : 0,
                root.TryGetProperty("queries", out var q) ? q.GetInt64() : 0);
        }
        catch
        {
            return null;
        }
    }
}
