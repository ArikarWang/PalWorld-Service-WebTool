using System.Text.Json;
using Cronos;
using PalWorldService.Shared.Config;
using PalWorldService.Shared.Palworld;

namespace PalWorldService.Host.Services;

public class ScheduledTaskDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ServerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "save"; // announce|save|shutdown|backup
    public string Cron { get; set; } = "0 4 * * *";
    public string? Message { get; set; }
    public int WaitTime { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    public DateTime? LastRunAtUtc { get; set; }
    public string? LastResult { get; set; }
    public DateTime? NextRunAtUtc { get; set; }
}

public class ScheduleStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private List<ScheduledTaskDefinition> _tasks = [];

    public ScheduleStore(AppConfigProvider config)
    {
        Directory.CreateDirectory(config.Current.DataDirectory);
        _path = Path.Combine(config.Current.DataDirectory, "schedules.json");
        Load();
    }

    public IReadOnlyList<ScheduledTaskDefinition> GetAll()
    {
        lock (_lock) return _tasks.ToList();
    }

    public IReadOnlyList<ScheduledTaskDefinition> GetByServer(string serverId)
    {
        lock (_lock)
            return _tasks.Where(t => string.Equals(t.ServerId, serverId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public ScheduledTaskDefinition Add(ScheduledTaskDefinition task)
    {
        lock (_lock)
        {
            task.Id = Guid.NewGuid().ToString("N");
            task.NextRunAtUtc = Next(task.Cron, DateTime.UtcNow);
            _tasks.Add(task);
            Save();
            return task;
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var removed = _tasks.RemoveAll(t => t.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public void Update(ScheduledTaskDefinition task)
    {
        lock (_lock)
        {
            var idx = _tasks.FindIndex(t => t.Id == task.Id);
            if (idx < 0) return;
            _tasks[idx] = task;
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        var json = File.ReadAllText(_path);
        _tasks = JsonSerializer.Deserialize<List<ScheduledTaskDefinition>>(json) ?? [];
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_tasks, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    public static DateTime? Next(string cron, DateTime fromUtc)
    {
        var expr = CronExpression.Parse(cron, CronFormat.Standard);
        return expr.GetNextOccurrence(fromUtc, TimeZoneInfo.Local);
    }
}

public class ScheduleExecutor
{
    private readonly AppConfigProvider _config;
    private readonly IPalworldRestClient _client;
    private readonly LocalOpsService _local;
    private readonly ILogger<ScheduleExecutor> _logger;

    public ScheduleExecutor(
        AppConfigProvider config,
        IPalworldRestClient client,
        LocalOpsService local,
        ILogger<ScheduleExecutor> logger)
    {
        _config = config;
        _client = client;
        _local = local;
        _logger = logger;
    }

    public async Task ExecuteAsync(ScheduledTaskDefinition task, CancellationToken ct)
    {
        var server = _config.GetServer(task.ServerId)
            ?? throw new InvalidOperationException($"Server {task.ServerId} not found");

        _logger.LogInformation("Running schedule {Name} ({Type}) for {Server}", task.Name, task.Type, server.Name);

        switch (task.Type.ToLowerInvariant())
        {
            case "announce":
                await _client.AnnounceAsync(server, task.Message ?? "", ct);
                break;
            case "save":
                await _client.SaveAsync(server, ct);
                break;
            case "shutdown":
                await _client.ShutdownAsync(server, task.WaitTime, task.Message, ct);
                break;
            case "backup":
                await _local.CreateBackupAsync(server, ct);
                break;
            default:
                throw new NotSupportedException($"Unknown schedule type: {task.Type}");
        }
    }
}
