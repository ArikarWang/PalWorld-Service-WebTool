using PalWorldService.Shared.Config;

namespace PalWorldService.Host.Services;

public class AppConfigProvider
{
    private readonly object _lock = new();
    private AppConfig _config;

    public AppConfigProvider(AppConfig config) => _config = config;

    public AppConfig Current
    {
        get { lock (_lock) return _config; }
    }

    public ServerConfig? GetServer(string id)
        => Current.Servers.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Replace(AppConfig config)
    {
        lock (_lock) _config = config;
    }
}
