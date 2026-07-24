using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PalWorldService.Shared.Config;

namespace PalWorldService.Host.Services;

public class ToolUpdateCheckService
{
    private readonly AppConfigProvider _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ToolUpdateCheckService> _logger;

    public ToolUpdateCheckService(
        AppConfigProvider config,
        IHttpClientFactory httpClientFactory,
        ILogger<ToolUpdateCheckService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static string GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public async Task<ToolUpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = NormalizeVersion(GetCurrentVersion());
        var owner = string.IsNullOrWhiteSpace(_config.Current.GithubOwner)
            ? "ArikarWang"
            : _config.Current.GithubOwner!;
        var repo = string.IsNullOrWhiteSpace(_config.Current.GithubRepo)
            ? "PalWorld-Service-WebTool"
            : _config.Current.GithubRepo!;

        try
        {
            var client = _httpClientFactory.CreateClient("GitHub");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PalWorldService", current));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("GitHub release check failed: {Status} {Body}", (int)response.StatusCode, body);
                return Fail(current, $"无法获取 GitHub Release（HTTP {(int)response.StatusCode}）。");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
            DateTime? published = null;
            if (root.TryGetProperty("published_at", out var pubEl) &&
                pubEl.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(pubEl.GetString(), out var pubDt))
            {
                published = DateTime.SpecifyKind(pubDt.ToUniversalTime(), DateTimeKind.Utc);
            }

            string? downloadUrl = null;
            string? assetName = null;
            long? assetSize = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var an = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (!string.Equals(an, ToolSelfUpdateService.ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    assetName = an;
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size))
                        assetSize = size;
                    break;
                }
            }

            var latest = NormalizeVersion(tag ?? "");
            if (string.IsNullOrWhiteSpace(latest))
            {
                return new ToolUpdateCheckResult(
                    Checked: false,
                    UpdateAvailable: false,
                    CurrentVersion: current,
                    LatestVersion: null,
                    ReleaseName: name,
                    ReleaseUrl: htmlUrl,
                    DownloadUrl: downloadUrl,
                    AssetName: assetName,
                    AssetSizeBytes: assetSize,
                    PublishedAtUtc: published,
                    Message: "最新 Release 缺少有效版本号。",
                    CheckedAtUtc: DateTime.UtcNow);
            }

            var updateAvailable = CompareSemVer(latest, current) > 0;
            string message;
            if (!updateAvailable)
                message = $"管理工具已是最新版本（{current}）。";
            else if (string.IsNullOrWhiteSpace(downloadUrl))
                message = $"管理工具有新版本：{latest}（当前 {current}），但未找到 {ToolSelfUpdateService.ReleaseAssetName}，请手动下载。";
            else
                message = $"管理工具有新版本：{latest}（当前 {current}），可在线更新。";

            return new ToolUpdateCheckResult(
                Checked: true,
                UpdateAvailable: updateAvailable,
                CurrentVersion: current,
                LatestVersion: latest,
                ReleaseName: name,
                ReleaseUrl: htmlUrl,
                DownloadUrl: downloadUrl,
                AssetName: assetName,
                AssetSizeBytes: assetSize,
                PublishedAtUtc: published,
                Message: message,
                CheckedAtUtc: DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool update check failed");
            return Fail(current, $"检查管理工具更新失败：{ex.Message}");
        }
    }

    private static ToolUpdateCheckResult Fail(string current, string message) => new(
        Checked: false,
        UpdateAvailable: false,
        CurrentVersion: current,
        LatestVersion: null,
        ReleaseName: null,
        ReleaseUrl: null,
        DownloadUrl: null,
        AssetName: null,
        AssetSizeBytes: null,
        PublishedAtUtc: null,
        Message: message,
        CheckedAtUtc: DateTime.UtcNow);

    internal static string NormalizeVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var v = raw.Trim();
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            v = v[1..];
        var m = Regex.Match(v, @"^\d+(?:\.\d+){0,3}(?:-[0-9A-Za-z.-]+)?");
        return m.Success ? m.Value : v;
    }

    /// <summary>Returns &gt;0 if a newer than b.</summary>
    internal static int CompareSemVer(string a, string b)
    {
        static (int[] nums, string pre) Split(string v)
        {
            var parts = v.Split('-', 2);
            var nums = parts[0].Split('.')
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .ToArray();
            if (nums.Length < 3)
                nums = nums.Concat(Enumerable.Repeat(0, 3 - nums.Length)).ToArray();
            return (nums, parts.Length > 1 ? parts[1] : "");
        }

        var (an, ap) = Split(a);
        var (bn, bp) = Split(b);
        for (var i = 0; i < 3; i++)
        {
            var c = an[i].CompareTo(bn[i]);
            if (c != 0) return c;
        }

        if (string.IsNullOrEmpty(ap) && !string.IsNullOrEmpty(bp)) return 1;
        if (!string.IsNullOrEmpty(ap) && string.IsNullOrEmpty(bp)) return -1;
        return string.CompareOrdinal(ap, bp);
    }
}

public record ToolUpdateCheckResult(
    bool Checked,
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseName,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? AssetName,
    long? AssetSizeBytes,
    DateTime? PublishedAtUtc,
    string? Message,
    DateTime CheckedAtUtc);
