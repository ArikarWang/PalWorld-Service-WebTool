using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PalWorldService.Shared.Config;

namespace PalWorldService.Host.Services;

/// <summary>Checks management-tool updates from Gitee Releases only.</summary>
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
        var owner = string.IsNullOrWhiteSpace(_config.Current.GiteeOwner)
            ? "arikar"
            : _config.Current.GiteeOwner!;
        var repo = string.IsNullOrWhiteSpace(_config.Current.GiteeRepo)
            ? "pal-world-service-web-tool"
            : _config.Current.GiteeRepo!;

        try
        {
            return await CheckViaGiteeApiAsync(current, owner, repo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gitee release check failed");
            return Fail(current, FormatNetworkError(ex));
        }
    }

    private async Task<ToolUpdateCheckResult> CheckViaGiteeApiAsync(
        string current,
        string owner,
        string repo,
        CancellationToken ct)
    {
        var apiUrl = $"https://gitee.com/api/v5/repos/{owner}/{repo}/releases/latest";
        var client = _httpClientFactory.CreateClient("Gitee");
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PalWorldService", current));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, ct);
        var bodyText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return Fail(current,
                $"无法访问 Gitee Release（HTTP {(int)response.StatusCode}）。" +
                $"仓库：https://gitee.com/{owner}/{repo}" +
                (bodyText.Length is > 0 and < 240 ? $" 详情：{bodyText.Trim()}" : ""));
        }

        using var doc = JsonDocument.Parse(bodyText);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(htmlUrl) && !string.IsNullOrWhiteSpace(tag))
            htmlUrl = $"https://gitee.com/{owner}/{repo}/releases/{tag}";

        var downloadCandidates = new List<string>();
        string? assetName = null;
        long? assetSize = null;

        CollectNamedAssets(root, "assets", downloadCandidates, ref assetName, ref assetSize);
        CollectNamedAssets(root, "attach_files", downloadCandidates, ref assetName, ref assetSize);

        if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var releaseId))
        {
            try
            {
                var (foundName, foundSize) = await AppendGiteeAttachFilesAsync(
                    client, owner, repo, releaseId, downloadCandidates, ct);
                if (!string.IsNullOrWhiteSpace(foundName))
                    assetName = foundName;
                if (foundSize is not null)
                    assetSize = foundSize;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Gitee attach_files lookup failed for release {ReleaseId}", releaseId);
            }
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            AddUnique(downloadCandidates,
                $"https://gitee.com/{owner}/{repo}/releases/download/{tag.Trim()}/{ToolSelfUpdateService.ReleaseAssetName}");
        }

        // Drop source archives that Gitee auto-adds; keep only the win-x64 package when possible.
        downloadCandidates = downloadCandidates
            .Where(u => !u.Contains("/archive/refs/tags/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var latest = NormalizeVersion(tag ?? "");
        if (string.IsNullOrWhiteSpace(latest))
            return Fail(current, "最新 Release 缺少有效版本号");

        assetName ??= ToolSelfUpdateService.ReleaseAssetName;
        var primary = downloadCandidates.FirstOrDefault();
        var updateAvailable = CompareSemVer(latest, current) > 0;

        if (updateAvailable && string.IsNullOrWhiteSpace(primary))
        {
            return Fail(current,
                $"Gitee 上已有 {latest}，但未找到安装包 {ToolSelfUpdateService.ReleaseAssetName}。" +
                $"请到 https://gitee.com/{owner}/{repo}/releases 上传附件后重试。");
        }

        return new ToolUpdateCheckResult(
            Checked: true,
            UpdateAvailable: updateAvailable,
            CurrentVersion: current,
            LatestVersion: latest,
            ReleaseName: name ?? $"PalWorld Service {tag}",
            ReleaseUrl: htmlUrl,
            DownloadUrl: primary,
            DownloadUrls: downloadCandidates,
            AssetName: assetName,
            AssetSizeBytes: assetSize,
            PublishedAtUtc: null,
            Message: updateAvailable
                ? $"管理工具有新版本：{latest}（当前 {current}），可在线更新。来源：Gitee"
                : $"管理工具已是最新版本（{current}）。来源：Gitee",
            CheckedAtUtc: DateTime.UtcNow);
    }

    private static async Task<(string? Name, long? Size)> AppendGiteeAttachFilesAsync(
        HttpClient client,
        string owner,
        string repo,
        long releaseId,
        List<string> downloadCandidates,
        CancellationToken ct)
    {
        var url = $"https://gitee.com/api/v5/repos/{owner}/{repo}/releases/{releaseId}/attach_files";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return (null, null);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, null);

        string? foundName = null;
        long? foundSize = null;
        foreach (var asset in doc.RootElement.EnumerateArray())
        {
            var an = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (!string.Equals(an, ToolSelfUpdateService.ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            foundName = an;
            if (asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size))
                foundSize = size;

            var browser = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (!string.IsNullOrWhiteSpace(browser))
                AddUnique(downloadCandidates, browser);

            if (asset.TryGetProperty("id", out var aid) && aid.TryGetInt64(out var attachId))
            {
                AddUnique(downloadCandidates,
                    $"https://gitee.com/api/v5/repos/{owner}/{repo}/releases/{releaseId}/attach_files/{attachId}/download");
            }
        }

        return (foundName, foundSize);
    }

    private static void CollectNamedAssets(
        JsonElement root,
        string propertyName,
        List<string> downloadCandidates,
        ref string? assetName,
        ref long? assetSize)
    {
        if (!root.TryGetProperty(propertyName, out var assets) || assets.ValueKind != JsonValueKind.Array)
            return;

        foreach (var asset in assets.EnumerateArray())
        {
            var an = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (!string.Equals(an, ToolSelfUpdateService.ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            assetName = an;
            if (asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size))
                assetSize = size;

            var browser = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (!string.IsNullOrWhiteSpace(browser))
                AddUnique(downloadCandidates, browser);

            var browserUrl = asset.TryGetProperty("browser_url", out var bu) ? bu.GetString() : null;
            if (!string.IsNullOrWhiteSpace(browserUrl) &&
                browserUrl.Contains("http", StringComparison.OrdinalIgnoreCase))
                AddUnique(downloadCandidates, browserUrl);

            var url = asset.TryGetProperty("url", out var uu) ? uu.GetString() : null;
            if (!string.IsNullOrWhiteSpace(url) &&
                url.EndsWith("/download", StringComparison.OrdinalIgnoreCase))
                AddUnique(downloadCandidates, url);
        }
    }

    internal static string FormatNetworkError(Exception ex)
    {
        var text = FlattenExceptionMessage(ex);
        if (IsConnectivityFailure(ex, text))
        {
            return "无法连接 Gitee。" +
                   "请确认本机可访问 https://gitee.com ，以及仓库 Release 已发布安装包。" +
                   $" 原始错误：{text}";
        }

        return $"检查管理工具更新失败：{text}";
    }

    private static bool IsConnectivityFailure(Exception ex, string text)
    {
        if (ex is HttpRequestException or TaskCanceledException or SocketException or IOException)
        {
            if (text.Contains("gitee.com", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("443", StringComparison.Ordinal) ||
                text.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("没有正确答复", StringComparison.Ordinal) ||
                text.Contains("连接尝试失败", StringComparison.Ordinal) ||
                text.Contains("积极拒绝", StringComparison.Ordinal) ||
                text.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("No such host", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return text.Contains("gitee.com", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("连接", StringComparison.Ordinal) ||
                text.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("443", StringComparison.Ordinal));
    }

    private static string FlattenExceptionMessage(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(e.Message) &&
                (parts.Count == 0 || !parts[^1].Contains(e.Message, StringComparison.Ordinal)))
                parts.Add(e.Message.Trim());
        }
        return string.Join(" → ", parts);
    }

    private static void AddUnique(List<string> list, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (list.Any(x => string.Equals(x, url, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add(url);
    }

    private static ToolUpdateCheckResult Fail(string current, string message) => new(
        Checked: false,
        UpdateAvailable: false,
        CurrentVersion: current,
        LatestVersion: null,
        ReleaseName: null,
        ReleaseUrl: null,
        DownloadUrl: null,
        DownloadUrls: null,
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
    IReadOnlyList<string>? DownloadUrls,
    string? AssetName,
    long? AssetSizeBytes,
    DateTime? PublishedAtUtc,
    string? Message,
    DateTime CheckedAtUtc);
