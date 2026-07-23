using PalWorldService.Host.Services;

namespace PalWorldService.Host.Background;

public class MonitorHostedService : BackgroundService
{
    private readonly MonitorService _monitor;
    private readonly AppConfigProvider _config;
    private readonly ILogger<MonitorHostedService> _logger;

    public MonitorHostedService(MonitorService monitor, AppConfigProvider config, ILogger<MonitorHostedService> logger)
    {
        _monitor = monitor;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Monitor started, interval={Seconds}s", _config.Current.MonitorIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _monitor.RefreshAllAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Monitor refresh failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _config.Current.MonitorIntervalSeconds)), stoppingToken);
        }
    }
}

public class ScheduleHostedService : BackgroundService
{
    private readonly ScheduleStore _store;
    private readonly ScheduleExecutor _executor;
    private readonly ILogger<ScheduleHostedService> _logger;

    public ScheduleHostedService(ScheduleStore store, ScheduleExecutor executor, ILogger<ScheduleHostedService> logger)
    {
        _store = store;
        _executor = executor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduler started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var task in _store.GetAll().Where(t => t.Enabled))
                {
                    if (task.NextRunAtUtc is null)
                    {
                        task.NextRunAtUtc = ScheduleStore.Next(task.Cron, now);
                        _store.Update(task);
                    }
                    if (task.NextRunAtUtc is null || now < task.NextRunAtUtc) continue;

                    try
                    {
                        await _executor.ExecuteAsync(task, stoppingToken);
                        task.LastResult = "OK";
                    }
                    catch (Exception ex)
                    {
                        task.LastResult = ex.Message;
                        _logger.LogError(ex, "Schedule {Name} failed", task.Name);
                    }
                    task.LastRunAtUtc = now;
                    task.NextRunAtUtc = ScheduleStore.Next(task.Cron, now);
                    _store.Update(task);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Scheduler loop error");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
