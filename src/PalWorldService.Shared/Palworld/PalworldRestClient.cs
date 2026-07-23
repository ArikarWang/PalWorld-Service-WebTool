using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PalWorldService.Shared.Config;

namespace PalWorldService.Shared.Palworld;

public interface IPalworldRestClient
{
    Task<PalServerInfo?> GetInfoAsync(ServerConfig server, CancellationToken ct = default);
    Task<PalServerMetrics?> GetMetricsAsync(ServerConfig server, CancellationToken ct = default);
    Task<IReadOnlyList<PalPlayer>> GetPlayersAsync(ServerConfig server, CancellationToken ct = default);
    Task<string?> GetSettingsAsync(ServerConfig server, CancellationToken ct = default);
    Task KickAsync(ServerConfig server, string userId, string? message, CancellationToken ct = default);
    Task BanAsync(ServerConfig server, string userId, string? message, CancellationToken ct = default);
    Task UnbanAsync(ServerConfig server, string userId, CancellationToken ct = default);
    Task AnnounceAsync(ServerConfig server, string message, CancellationToken ct = default);
    Task SaveAsync(ServerConfig server, CancellationToken ct = default);
    Task ShutdownAsync(ServerConfig server, int waitTime, string? message, CancellationToken ct = default);
    Task StopAsync(ServerConfig server, CancellationToken ct = default);
    Task<ServerStatusSnapshot> GetStatusSnapshotAsync(ServerConfig server, CancellationToken ct = default);
}

public class PalworldRestClient : IPalworldRestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PalworldRestClient> _logger;

    public PalworldRestClient(IHttpClientFactory httpClientFactory, ILogger<PalworldRestClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<PalServerInfo?> GetInfoAsync(ServerConfig server, CancellationToken ct = default)
        => GetAsync<PalServerInfo>(server, "info", ct);

    public Task<PalServerMetrics?> GetMetricsAsync(ServerConfig server, CancellationToken ct = default)
        => GetAsync<PalServerMetrics>(server, "metrics", ct);

    public async Task<IReadOnlyList<PalPlayer>> GetPlayersAsync(ServerConfig server, CancellationToken ct = default)
    {
        var response = await SendAsync(server, HttpMethod.Get, "players", null, ct);
        if (response is null) return [];

        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return JsonSerializer.Deserialize<List<PalPlayer>>(response, JsonOptions) ?? [];
            if (doc.RootElement.TryGetProperty("players", out var players))
                return JsonSerializer.Deserialize<List<PalPlayer>>(players.GetRawText(), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse players from {Host}", server.Host);
        }
        return [];
    }

    public Task<string?> GetSettingsAsync(ServerConfig server, CancellationToken ct = default)
        => SendAsync(server, HttpMethod.Get, "settings", null, ct);

    public Task KickAsync(ServerConfig server, string userId, string? message, CancellationToken ct = default)
        => PostAsync(server, "kick", new { userid = userId, message }, ct);

    public Task BanAsync(ServerConfig server, string userId, string? message, CancellationToken ct = default)
        => PostAsync(server, "ban", new { userid = userId, message }, ct);

    public Task UnbanAsync(ServerConfig server, string userId, CancellationToken ct = default)
        => PostAsync(server, "unban", new { userid = userId }, ct);

    public Task AnnounceAsync(ServerConfig server, string message, CancellationToken ct = default)
        => PostAsync(server, "announce", new { message }, ct);

    public Task SaveAsync(ServerConfig server, CancellationToken ct = default)
        => PostAsync(server, "save", null, ct);

    public Task ShutdownAsync(ServerConfig server, int waitTime, string? message, CancellationToken ct = default)
        => PostAsync(server, "shutdown", new { waittime = waitTime, message }, ct);

    public Task StopAsync(ServerConfig server, CancellationToken ct = default)
        => PostAsync(server, "stop", null, ct);

    public async Task<ServerStatusSnapshot> GetStatusSnapshotAsync(ServerConfig server, CancellationToken ct = default)
    {
        try
        {
            PalServerInfo? info = null;
            PalServerMetrics? metrics = null;
            IReadOnlyList<PalPlayer> players = [];

            try { info = await GetInfoAsync(server, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "info failed for {Name}", server.Name); }

            try { metrics = await GetMetricsAsync(server, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "metrics failed for {Name}", server.Name); }

            try { players = await GetPlayersAsync(server, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "players failed for {Name}", server.Name); }

            // Reachable if at least one endpoint succeeded
            var online = info is not null || metrics is not null;
            if (!online)
            {
                // last attempt to confirm connectivity
                info = await GetInfoAsync(server, ct);
                online = true;
            }

            return new ServerStatusSnapshot
            {
                ServerId = server.Id,
                ServerName = server.Name,
                IsOnline = online,
                Info = info,
                Metrics = metrics,
                Players = players,
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Server {Name} unreachable", server.Name);
            return new ServerStatusSnapshot
            {
                ServerId = server.Id,
                ServerName = server.Name,
                IsOnline = false,
                Error = ex.Message,
                CheckedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<T?> GetAsync<T>(ServerConfig server, string path, CancellationToken ct)
    {
        var response = await SendAsync(server, HttpMethod.Get, path, null, ct);
        return string.IsNullOrWhiteSpace(response) ? default : JsonSerializer.Deserialize<T>(response, JsonOptions);
    }

    private async Task PostAsync(ServerConfig server, string path, object? body, CancellationToken ct)
    {
        await SendAsync(server, HttpMethod.Post, path, body, ct);
    }

    private async Task<string?> SendAsync(ServerConfig server, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Palworld");
        var request = new HttpRequestMessage(method, $"{server.RestApiBaseUrl}/{path.TrimStart('/')}");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:{server.AdminPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("API {Method} /{Path} => {Status}: {Content}", method, path, (int)response.StatusCode, content);
            response.EnsureSuccessStatusCode();
        }
        return content;
    }
}
