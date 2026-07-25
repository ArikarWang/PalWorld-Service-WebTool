using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PalWorldService.Shared.Config;

namespace PalWorldService.Host.Services;

public class ToolUpdateCheckService
{
    /// <summary>
    /// Free prefix mirrors that typically work for github.com/releases (not api.github.com).
    /// Tried automatically when direct access fails.
    /// </summary>
    private static readonly string[] BuiltinProxies =
    [
        "https://ghfast.top/",
        "https://ghproxy.net/",
        "https://gh-proxy.com/",
    ];

    private static readonly Regex TagFromUrlRegex = new(
        @"/releases/tag/(?<tag>v?[\w\.-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagFromHtmlRegex = new(
        @"releases/tag/(?<tag>v?[\w\.-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var errors = new List<string>();

        // Prefer configured proxy, then built-ins, then direct.
        // Many free proxies break api.github.com but work for /releases pages + assets.
        foreach (var proxy in BuildProxyCandidates())
        {
            try
            {
                var result = await CheckViaReleasePageAsync(current, owner, repo, proxy, ct);
                if (result.Checked)
                    return result;
                if (!string.IsNullOrWhiteSpace(result.Message))
                    errors.Add($"{LabelProxy(proxy)}: {result.Message}");
            }
            catch (Exception ex)
            {
                var msg = FlattenExceptionMessage(ex);
                _logger.LogWarning(ex, "Release check failed via {Proxy}", LabelProxy(proxy));
                errors.Add($"{LabelProxy(proxy)}: {msg}");
            }
        }

        // Last resort: official API (often blocked / rate-limited in CN)
        try
        {
            var apiResult = await CheckViaGithubApiAsync(current, owner, repo, GetGithubProxy(), ct);
            if (apiResult.Checked)
                return apiResult;
            if (!string.IsNullOrWhiteSpace(apiResult.Message))
                errors.Add($"api.github.com: {apiResult.Message}");
        }
        catch (Exception ex)
        {
            errors.Add($"api.github.com: {FlattenExceptionMessage(ex)}");
            _logger.LogWarning(ex, "GitHub API check failed");
        }

        return Fail(current,
            "无法获取最新版本（直连与代理均失败）。" +
            "可在 config/servers.yaml 设置 githubProxy（如 https://ghfast.top/）后重试。" +
            " 详情：" + string.Join(" | ", errors.Take(3)));
    }

    private IEnumerable<string?> BuildProxyCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var user = GetGithubProxy();
        if (!string.IsNullOrWhiteSpace(user) && seen.Add(user))
            yield return user;

        foreach (var p in BuiltinProxies)
        {
            var n = NormalizeProxy(p);
            if (n is not null && seen.Add(n))
                yield return n;
        }

        yield return null; // direct
    }

    private async Task<ToolUpdateCheckResult> CheckViaReleasePageAsync(
        string current,
        string owner,
        string repo,
        string? proxy,
        CancellationToken ct)
    {
        var latestPath = $"https://github.com/{owner}/{repo}/releases/latest";
        var url = ApplyProxy(proxy, latestPath);
        var client = _httpClientFactory.CreateClient("GitHub");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; PalWorldService/" + current + ")");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return Fail(current,
                $"HTTP {(int)response.StatusCode}" +
                (body.Length > 0 && body.Length < 200 ? $": {body.Trim()}" : ""));
        }

        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
        var tagRaw = ExtractTag(finalUrl) ?? ExtractTagFromHtml(body);
        if (string.IsNullOrWhiteSpace(tagRaw))
            return Fail(current, "未能从 Release 页面解析版本号");

        var latest = NormalizeVersion(tagRaw);
        if (string.IsNullOrWhiteSpace(latest))
            return Fail(current, "解析到的版本号无效");

        var releaseUrl = $"https://github.com/{owner}/{repo}/releases/tag/{tagRaw.Trim()}";
        var assetRaw = $"https://github.com/{owner}/{repo}/releases/download/{tagRaw.Trim()}/{ToolSelfUpdateService.ReleaseAssetName}";
        var downloadUrl = ApplyProxy(proxy, assetRaw);

        var updateAvailable = CompareSemVer(latest, current) > 0;
        var via = LabelProxy(proxy);
        string message;
        if (!updateAvailable)
            message = $"管理工具已是最新版本（{current}）。来源：{via}";
        else
            message = $"管理工具有新版本：{latest}（当前 {current}），可在线更新。来源：{via}";

        return new ToolUpdateCheckResult(
            Checked: true,
            UpdateAvailable: updateAvailable,
            CurrentVersion: current,
            LatestVersion: latest,
            ReleaseName: $"PalWorld Service {tagRaw.Trim()}",
            ReleaseUrl: releaseUrl,
            DownloadUrl: downloadUrl,
            AssetName: ToolSelfUpdateService.ReleaseAssetName,
            AssetSizeBytes: null,
            PublishedAtUtc: null,
            Message: message,
            CheckedAtUtc: DateTime.UtcNow);
    }

    private async Task<ToolUpdateCheckResult> CheckViaGithubApiAsync(
        string current,
        string owner,
        string repo,
        string? proxy,
        CancellationToken ct)
    {
        var apiUrl = ApplyProxy(proxy, $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
        var client = _httpClientFactory.CreateClient("GitHub");
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PalWorldService", current));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return Fail(current, $"HTTP {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;

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
                var rawDownload = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                downloadUrl = string.IsNullOrWhiteSpace(rawDownload) ? null : ApplyProxy(proxy, rawDownload);
                if (asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size))
                    assetSize = size;
                break;
            }
        }

        // If API omitted assets, still construct download URL from tag
        if (string.IsNullOrWhiteSpace(downloadUrl) && !string.IsNullOrWhiteSpace(tag))
        {
            downloadUrl = ApplyProxy(proxy,
                $"https://github.com/{owner}/{repo}/releases/download/{tag}/{ToolSelfUpdateService.ReleaseAssetName}");
            assetName ??= ToolSelfUpdateService.ReleaseAssetName;
        }

        var latest = NormalizeVersion(tag ?? "");
        if (string.IsNullOrWhiteSpace(latest))
            return Fail(current, "最新 Release 缺少有效版本号");

        var updateAvailable = CompareSemVer(latest, current) > 0;
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
            PublishedAtUtc: null,
            Message: updateAvailable
                ? $"管理工具有新版本：{latest}（当前 {current}），可在线更新。来源：GitHub API"
                : $"管理工具已是最新版本（{current}）。来源：GitHub API",
            CheckedAtUtc: DateTime.UtcNow);
    }

    internal static string? ExtractTag(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var m = TagFromUrlRegex.Match(url);
        return m.Success ? m.Groups["tag"].Value : null;
    }

    internal static string? ExtractTagFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var m = TagFromHtmlRegex.Match(html);
        return m.Success ? m.Groups["tag"].Value : null;
    }

    public string? GetGithubProxy() => NormalizeProxy(_config.Current.GithubProxy);

    public static string ApplyProxy(string? proxy, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        var p = NormalizeProxy(proxy);
        if (string.IsNullOrWhiteSpace(p)) return url;
        if (url.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return url;
        return p + url;
    }

    /// <summary>Apply optional githubProxy prefix to a GitHub URL.</summary>
    public string ResolveGithubUrl(string url) => ApplyProxy(GetGithubProxy(), url);

    internal static string FormatNetworkError(Exception ex)
    {
        var text = FlattenExceptionMessage(ex);
        if (IsGithubConnectivityFailure(ex, text))
        {
            return "无法连接 GitHub。" +
                   "程序会自动尝试公共加速；也可在 config/servers.yaml 设置 githubProxy: \"https://ghfast.top/\"。" +
                   $" 原始错误：{text}";
        }

        return $"检查管理工具更新失败：{text}";
    }

    private static bool IsGithubConnectivityFailure(Exception ex, string text)
    {
        if (ex is HttpRequestException or TaskCanceledException or SocketException or IOException)
        {
            if (text.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
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

        return text.Contains("github.com", StringComparison.OrdinalIgnoreCase) &&
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

    private static string? NormalizeProxy(string? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy)) return null;
        var p = proxy.Trim();
        if (!p.EndsWith('/')) p += "/";
        return p;
    }

    private static string LabelProxy(string? proxy)
        => string.IsNullOrWhiteSpace(proxy) ? "直连" : proxy.TrimEnd('/');

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
