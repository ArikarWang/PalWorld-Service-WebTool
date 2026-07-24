using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PalWorldService.Shared.Config;

namespace PalWorldService.Host.Services;

/// <summary>
/// Compares local Steam appmanifest buildid with the remote public branch buildid.
/// </summary>
public class UpdateCheckService
{
    public const int DefaultPalDedicatedAppId = 2394010;

    private static readonly Regex BuildIdRegex = new(
        "\"buildid\"\\s+\"(?<id>\\d+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(IHttpClientFactory httpClientFactory, ILogger<UpdateCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckAsync(ServerConfig server, CancellationToken ct = default)
    {
        var appId = server.SteamAppId is > 0 ? server.SteamAppId.Value : DefaultPalDedicatedAppId;
        var layout = SteamLayout.Resolve(server, appId);

        if (layout is null)
        {
            return new UpdateCheckResult(
                Checked: false,
                UpdateAvailable: false,
                LocalBuildId: null,
                RemoteBuildId: null,
                AppId: appId,
                RemoteSource: null,
                Message: "无法定位 Steam 安装目录。请确认 executablePath 指向 steamapps/common/PalServer，或配置 steamCmdPath。",
                CheckedAtUtc: DateTime.UtcNow);
        }

        var localBuildId = TryReadLocalBuildId(layout.AppManifestPath);
        if (string.IsNullOrWhiteSpace(localBuildId))
        {
            return new UpdateCheckResult(
                Checked: false,
                UpdateAvailable: false,
                LocalBuildId: null,
                RemoteBuildId: null,
                AppId: appId,
                RemoteSource: null,
                Message: $"未找到本地安装清单：{layout.AppManifestPath}",
                CheckedAtUtc: DateTime.UtcNow);
        }

        string? remoteBuildId = null;
        string? remoteSource = null;
        string? remoteError = null;

        if (!string.IsNullOrWhiteSpace(layout.SteamCmdPath) && File.Exists(layout.SteamCmdPath))
        {
            try
            {
                remoteBuildId = await QueryRemoteBuildIdViaSteamCmdAsync(layout.SteamCmdPath, appId, ct);
                if (!string.IsNullOrWhiteSpace(remoteBuildId))
                    remoteSource = "steamcmd";
            }
            catch (Exception ex)
            {
                remoteError = ex.Message;
                _logger.LogWarning(ex, "SteamCMD update check failed for app {AppId}", appId);
            }
        }

        if (string.IsNullOrWhiteSpace(remoteBuildId))
        {
            try
            {
                remoteBuildId = await QueryRemoteBuildIdViaHttpAsync(appId, ct);
                if (!string.IsNullOrWhiteSpace(remoteBuildId))
                    remoteSource = "http";
            }
            catch (Exception ex)
            {
                remoteError ??= ex.Message;
                _logger.LogWarning(ex, "HTTP update check failed for app {AppId}", appId);
            }
        }

        if (string.IsNullOrWhiteSpace(remoteBuildId))
        {
            return new UpdateCheckResult(
                Checked: false,
                UpdateAvailable: false,
                LocalBuildId: localBuildId,
                RemoteBuildId: null,
                AppId: appId,
                RemoteSource: null,
                Message: "已读取本地版本，但无法获取 Steam 远端版本。" +
                         (string.IsNullOrWhiteSpace(remoteError) ? "" : $" ({remoteError})"),
                CheckedAtUtc: DateTime.UtcNow);
        }

        var updateAvailable = !string.Equals(localBuildId, remoteBuildId, StringComparison.Ordinal);
        return new UpdateCheckResult(
            Checked: true,
            UpdateAvailable: updateAvailable,
            LocalBuildId: localBuildId,
            RemoteBuildId: remoteBuildId,
            AppId: appId,
            RemoteSource: remoteSource,
            Message: updateAvailable
                ? "检测到帕鲁专用服务器有可用更新。"
                : "当前已是最新版本。",
            CheckedAtUtc: DateTime.UtcNow);
    }

    internal static string? TryReadLocalBuildId(string appManifestPath)
    {
        if (!File.Exists(appManifestPath)) return null;
        var text = File.ReadAllText(appManifestPath);
        // Prefer AppState root buildid (first match in typical manifests).
        var match = BuildIdRegex.Match(text);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private async Task<string?> QueryRemoteBuildIdViaSteamCmdAsync(string steamCmdPath, int appId, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = steamCmdPath,
            Arguments = $"+login anonymous +app_info_update 1 +app_info_print {appId} +quit",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(steamCmdPath) ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException("无法启动 SteamCMD。");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("SteamCMD 查询超时。");
        }

        var output = await stdoutTask + Environment.NewLine + await stderrTask;
        return ParsePublicBranchBuildId(output, appId);
    }

    private async Task<string?> QueryRemoteBuildIdViaHttpAsync(int appId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("SteamInfo");
        using var response = await client.GetAsync($"https://api.steamcmd.net/v1/info/{appId}", ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("data", out var data))
            return null;
        if (!data.TryGetProperty(appId.ToString(), out var app))
            return null;
        if (!app.TryGetProperty("depots", out var depots))
            return null;
        if (!depots.TryGetProperty("branches", out var branches))
            return null;
        if (!branches.TryGetProperty("public", out var pub))
            return null;
        if (!pub.TryGetProperty("buildid", out var buildIdEl))
            return null;

        return buildIdEl.ValueKind switch
        {
            JsonValueKind.String => buildIdEl.GetString(),
            JsonValueKind.Number => buildIdEl.GetRawText(),
            _ => null
        };
    }

    internal static string? ParsePublicBranchBuildId(string steamCmdOutput, int appId)
    {
        if (string.IsNullOrWhiteSpace(steamCmdOutput)) return null;

        // Prefer public branch buildid under "branches" { "public" { "buildid" "..." } }
        var publicBlock = Regex.Match(
            steamCmdOutput,
            "\"public\"\\s*\\{[^{}]*\"buildid\"\\s+\"(?<id>\\d+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (publicBlock.Success)
            return publicBlock.Groups["id"].Value;

        // Fallback: any buildid near the app id section
        var appIdx = steamCmdOutput.IndexOf($"\"{appId}\"", StringComparison.Ordinal);
        var search = appIdx >= 0 ? steamCmdOutput[appIdx..] : steamCmdOutput;
        var match = BuildIdRegex.Match(search);
        return match.Success ? match.Groups["id"].Value : null;
    }
}

public record UpdateCheckResult(
    bool Checked,
    bool UpdateAvailable,
    string? LocalBuildId,
    string? RemoteBuildId,
    int AppId,
    string? RemoteSource,
    string? Message,
    DateTime CheckedAtUtc);

internal sealed class SteamLayout
{
    public required string SteamCmdPath { get; init; }
    public required string SteamAppsDirectory { get; init; }
    public required string AppManifestPath { get; init; }

    public static SteamLayout? Resolve(ServerConfig server, int appId)
    {
        string? steamCmdPath = null;
        string? steamApps = null;

        if (!string.IsNullOrWhiteSpace(server.SteamCmdPath))
        {
            steamCmdPath = Path.GetFullPath(server.SteamCmdPath);
            var root = Path.GetDirectoryName(steamCmdPath);
            if (!string.IsNullOrWhiteSpace(root))
                steamApps = Path.Combine(root, "steamapps");
        }

        if (steamApps is null && !string.IsNullOrWhiteSpace(server.ExecutablePath))
        {
            // .../steamapps/common/PalServer/PalServer.exe
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(server.ExecutablePath));
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                var common = Directory.GetParent(exeDir);          // common
                var apps = common?.Parent;                         // steamapps
                var steamRoot = apps?.Parent;                      // steamcmd root
                if (apps is not null &&
                    string.Equals(apps.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    steamApps = apps.FullName;
                    if (steamRoot is not null)
                    {
                        var candidate = Path.Combine(steamRoot.FullName, "steamcmd.exe");
                        if (File.Exists(candidate))
                            steamCmdPath = candidate;
                        else
                            steamCmdPath ??= candidate;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(steamApps))
            return null;

        return new SteamLayout
        {
            SteamCmdPath = steamCmdPath ?? "",
            SteamAppsDirectory = steamApps,
            AppManifestPath = Path.Combine(steamApps, $"appmanifest_{appId}.acf")
        };
    }
}
