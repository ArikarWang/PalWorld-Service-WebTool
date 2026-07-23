using System.Collections.Concurrent;
using PalWorldService.Shared.Palworld;

namespace PalWorldService.Host.Services;

public class MonitorService
{
    private readonly AppConfigProvider _config;
    private readonly IPalworldRestClient _client;
    private readonly ConcurrentDictionary<string, ServerStatusSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public MonitorService(AppConfigProvider config, IPalworldRestClient client)
    {
        _config = config;
        _client = client;
    }

    public IReadOnlyCollection<ServerStatusSnapshot> GetAll() => _snapshots.Values.ToList();

    public ServerStatusSnapshot? Get(string serverId)
        => _snapshots.TryGetValue(serverId, out var s) ? s : null;

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        foreach (var server in _config.Current.Servers)
        {
            var snap = await _client.GetStatusSnapshotAsync(server, ct);
            _snapshots[server.Id] = snap;
        }
    }

    public async Task RefreshOneAsync(string serverId, CancellationToken ct = default)
    {
        var server = _config.GetServer(serverId);
        if (server is null) return;
        _snapshots[server.Id] = await _client.GetStatusSnapshotAsync(server, ct);
    }
}
