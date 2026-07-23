namespace PalWorldService.Shared;

public static class PalServerPathHelper
{
    public const string ExecutableName = "PalServer.exe";
    public const string ConfigRelative = @"Pal\Saved\Config\WindowsServer\PalWorldSettings.ini";
    public const string SaveRelative = @"Pal\Saved\SaveGames";
    public const string LogRelative = @"Pal\Saved\Logs";

    public static void ApplyInstallRoot(Config.ServerConfig server, string installRoot)
    {
        server.ExecutablePath ??= Path.Combine(installRoot, ExecutableName);
        server.ConfigPath ??= Path.Combine(installRoot, ConfigRelative);
        server.SaveDirectory ??= Path.Combine(installRoot, SaveRelative);
        server.LogDirectory ??= Path.Combine(installRoot, LogRelative);
    }
}
