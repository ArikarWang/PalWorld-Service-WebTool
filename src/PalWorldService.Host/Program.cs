using PalWorldService.Host.Background;
using PalWorldService.Host.Modules;
using PalWorldService.Host.Services;
using PalWorldService.Shared.Config;
using PalWorldService.Shared.Palworld;

var contentRoot = AppContext.BaseDirectory;
Directory.SetCurrentDirectory(contentRoot);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot")
});

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddFileLogger(Path.Combine(contentRoot, "logs"));

var configPath = FindConfigPath(contentRoot);

var loader = new ConfigLoader();
AppConfig appConfig;
try
{
    appConfig = loader.Load(configPath);
    Console.WriteLine($"[config] Loaded {configPath} ({appConfig.Servers.Count} server(s))");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[config] Failed to load {configPath}: {ex.Message}");
    return 1;
}

Directory.CreateDirectory(appConfig.DataDirectory);
Directory.CreateDirectory(appConfig.BackupDirectory);
Directory.CreateDirectory("logs");

var listen = appConfig.Listen;
if (!listen.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    listen = "http://" + listen;
builder.WebHost.UseUrls(listen);

builder.Services.AddSingleton(appConfig);
builder.Services.AddSingleton(new AppConfigProvider(appConfig));
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<MonitorService>();
builder.Services.AddSingleton<LocalOpsService>();
builder.Services.AddSingleton<UpdateCheckService>();
builder.Services.AddSingleton<PlayerRosterService>();
builder.Services.AddSingleton<ScheduleStore>();
builder.Services.AddSingleton<ScheduleExecutor>();
builder.Services.AddHttpClient("Palworld", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("SteamInfo", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<IPalworldRestClient, PalworldRestClient>();
builder.Services.AddHostedService<MonitorHostedService>();
builder.Services.AddHostedService<ScheduleHostedService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true)));

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapAllModules();
app.MapFallbackToFile("index.html");

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("PalWorld Service listening on {Listen}", listen);
logger.LogInformation("Open http://127.0.0.1:{Port} in browser", new Uri(listen).Port);
logger.LogInformation("Close this window or use web UI to stop the service.");

app.Run();
return 0;

static string FindConfigPath(string contentRoot)
{
    var candidates = new[]
    {
        Path.Combine(contentRoot, "config", "servers.yaml"),
        Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "..", "..", "config", "servers.yaml")),
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config", "servers.yaml")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "servers.yaml")),
    };
    foreach (var c in candidates.Distinct())
    {
        if (File.Exists(c)) return c;
    }
    return candidates[0];
}

internal static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder, string directory)
    {
        Directory.CreateDirectory(directory);
        builder.AddProvider(new FileLoggerProvider(directory));
        return builder;
    }
}

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _lock = new();

    public FileLoggerProvider(string directory) => _directory = directory;

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _directory, _lock);

    public void Dispose() { }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly string _directory;
    private readonly object _lock;

    public FileLogger(string category, string directory, object gate)
    {
        _category = category;
        _directory = directory;
        _lock = gate;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_category}: {formatter(state, exception)}";
        if (exception is not null) line += Environment.NewLine + exception;
        var file = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
        lock (_lock) File.AppendAllText(file, line + Environment.NewLine);
    }
}
