using PalWorldService.Shared.Palworld;

namespace PalWorldService.Host.Services;

public class PlayerRosterService
{
    private readonly IPalworldRestClient _client;
    private readonly LocalOpsService _local;

    public PlayerRosterService(IPalworldRestClient client, LocalOpsService local)
    {
        _client = client;
        _local = local;
    }

    public async Task<IReadOnlyList<PlayerRosterItem>> GetRosterAsync(
        Shared.Config.ServerConfig server,
        CancellationToken ct = default)
    {
        IReadOnlyList<PalPlayer> online = [];
        try { online = await _client.GetPlayersAsync(server, ct); }
        catch { /* REST may be down */ }

        var saves = _local.ListPlayersFromSaves(server);
        var map = new Dictionary<string, PlayerRosterItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var o in online)
        {
            var key = FirstNonEmpty(o.PlayerUid, o.PlayerId, o.UserId, o.Name) ?? Guid.NewGuid().ToString("N");
            map[key] = new PlayerRosterItem
            {
                Key = key,
                Name = o.Name ?? o.AccountName,
                PlayerUid = o.PlayerUid ?? o.PlayerId,
                UserId = o.UserId,
                Ip = o.Ip,
                Ping = o.Ping,
                Level = o.Level,
                IsOnline = true,
                Location = o.Location,
                Source = "rest"
            };
        }

        foreach (var s in saves)
        {
            // Match by uid / userid containing save filename
            var existing = map.Values.FirstOrDefault(p =>
                IdsMatch(p.PlayerUid, s.PlayerId) ||
                IdsMatch(p.UserId, s.PlayerId) ||
                IdsMatch(p.Key, s.PlayerId));

            if (existing is not null)
            {
                existing.HasSave = true;
                existing.SaveLastWriteUtc = s.LastWriteUtc;
                continue;
            }

            map[s.PlayerId] = new PlayerRosterItem
            {
                Key = s.PlayerId,
                Name = null,
                PlayerUid = s.PlayerId,
                UserId = s.PlayerId,
                IsOnline = false,
                HasSave = true,
                SaveLastWriteUtc = s.LastWriteUtc,
                Source = "save"
            };
        }

        return map.Values
            .OrderByDescending(p => p.IsOnline)
            .ThenBy(p => p.Name ?? p.UserId ?? p.Key)
            .ToList();
    }

    private static bool IdsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        // steam_xxx / bare id
        return a.Contains(b, StringComparison.OrdinalIgnoreCase)
               || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public class PlayerRosterItem
{
    public string Key { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? PlayerUid { get; set; }
    public string? UserId { get; set; }
    public string? Ip { get; set; }
    public int Ping { get; set; }
    public int Level { get; set; }
    public bool IsOnline { get; set; }
    public bool HasSave { get; set; }
    public DateTime? SaveLastWriteUtc { get; set; }
    public string Source { get; set; } = "rest";
    public PalPlayerLocation? Location { get; set; }
}

public class PlayerPalsResult
{
    public bool Supported { get; set; }
    public string Status { get; set; } = "unsupported";
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<PlayerPalItem> Pals { get; set; } = [];
}

public class PlayerPalItem
{
    public string? Name { get; set; }
    public string? CharacterId { get; set; }
    public int Level { get; set; }
    public string? Nickname { get; set; }
    public int? PotentialScore { get; set; }
    public string? PotentialLabel { get; set; }
    public string? Note { get; set; }
}
