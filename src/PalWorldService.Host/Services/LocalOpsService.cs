using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using PalWorldService.Shared.Config;

namespace PalWorldService.Host.Services;

public class LocalOpsService
{
    private readonly AppConfigProvider _config;
    private readonly ILogger<LocalOpsService> _logger;

    public LocalOpsService(AppConfigProvider config, ILogger<LocalOpsService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> ReadConfigAsync(ServerConfig server, CancellationToken ct)
    {
        EnsurePath(server.ConfigPath, "configPath");
        return await File.ReadAllTextAsync(server.ConfigPath!, ct);
    }

    public async Task WriteConfigAsync(ServerConfig server, string content, CancellationToken ct)
    {
        EnsurePath(server.ConfigPath, "configPath");
        var dir = Path.GetDirectoryName(server.ConfigPath!);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(server.ConfigPath!, content, ct);
    }

    public async Task<IReadOnlyList<string>> ReadLogsAsync(ServerConfig server, int lines, CancellationToken ct)
    {
        EnsurePath(server.LogDirectory, "logDirectory", isDir: true);
        var logFile = Directory.GetFiles(server.LogDirectory!, "*.log", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (logFile is null) return [];
        var all = await File.ReadAllLinesAsync(logFile.FullName, ct);
        return all.TakeLast(lines).ToList();
    }

    public bool IsProcessRunning(ServerConfig server)
    {
        if (string.IsNullOrWhiteSpace(server.ExecutablePath)) return false;
        var name = Path.GetFileNameWithoutExtension(server.ExecutablePath);
        return Process.GetProcessesByName(name).Length > 0;
    }

    public void StartProcess(ServerConfig server)
    {
        EnsurePath(server.ExecutablePath, "executablePath");
        Process.Start(new ProcessStartInfo
        {
            FileName = server.ExecutablePath!,
            WorkingDirectory = Path.GetDirectoryName(server.ExecutablePath!)!,
            UseShellExecute = true
        });
    }

    public void StopProcess(ServerConfig server)
    {
        EnsurePath(server.ExecutablePath, "executablePath");
        var name = Path.GetFileNameWithoutExtension(server.ExecutablePath!);
        foreach (var p in Process.GetProcessesByName(name))
            p.Kill(entireProcessTree: true);
    }

    public async Task<BackupInfo> CreateBackupAsync(ServerConfig server, CancellationToken ct)
    {
        EnsurePath(server.SaveDirectory, "saveDirectory", isDir: true);
        var root = Path.Combine(_config.Current.BackupDirectory, server.Id);
        Directory.CreateDirectory(root);
        var fileName = $"backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
        var path = Path.Combine(root, fileName);
        await Task.Run(() => ZipFile.CreateFromDirectory(server.SaveDirectory!, path), ct);
        return new BackupInfo(fileName, path, new FileInfo(path).Length, DateTime.UtcNow);
    }

    public IReadOnlyList<BackupInfo> ListBackups(ServerConfig server)
    {
        var root = Path.Combine(_config.Current.BackupDirectory, server.Id);
        if (!Directory.Exists(root)) return [];
        return Directory.GetFiles(root, "*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo(f.Name, f.FullName, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    public void RestoreBackup(ServerConfig server, string fileName)
    {
        EnsurePath(server.SaveDirectory, "saveDirectory", isDir: true);
        var path = Path.Combine(_config.Current.BackupDirectory, server.Id, Path.GetFileName(fileName));
        if (!File.Exists(path)) throw new FileNotFoundException("Backup not found", path);

        var temp = Path.Combine(Path.GetTempPath(), $"pal-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(path, temp, true);
            if (Directory.Exists(server.SaveDirectory))
                Directory.Delete(server.SaveDirectory, true);
            Directory.CreateDirectory(server.SaveDirectory!);
            CopyDir(temp, server.SaveDirectory!);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
        _logger.LogInformation("Restored backup {File} for {Server}", fileName, server.Id);
    }

    private static void EnsurePath(string? path, string name, bool isDir = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Server {name} is not configured.");
        if (isDir)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"{name} not found: {path}");
        }
        else if (!File.Exists(path) && name is "configPath" or "executablePath")
        {
            if (name == "executablePath" && !File.Exists(path))
                throw new FileNotFoundException($"{name} not found: {path}");
        }
    }

    private static void CopyDir(string source, string dest)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}

public record BackupInfo(string FileName, string FullPath, long SizeBytes, DateTime CreatedAtUtc);
