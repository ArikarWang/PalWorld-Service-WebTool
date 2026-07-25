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
    /// Used only when updateSource involves GitHub.
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
        var source = GetUpdateSource();
        var errors = new List<string>();

        if (source is "gitee" or "auto")
        {
            try
            {
                var gitee = await CheckViaGiteeApiAsync(current, ct);
                if (gitee.Checked)
                    return gitee;
                if (!string.IsNullOrWhiteSpace(gitee.Message))
                    errors.Add($"Gitee: {gitee.Message}");
            }
            catch (Exception ex)
            {
                var msg = FlattenExceptionMessage(ex);
                _logger.LogWarning(ex, "Gitee release check failed");
                errors.Add($"Gitee: {msg}");
            }

            if (source == "gitee")
            {
                return Fail(current,
                    "无法从 Gitee 获取最新版本。" +
                    "请确认 Gitee Release 已发布安装包，或将 updateSource 设为 auto/github。" +
                    " 详情：" + string.Join(" | ", errors.Take(3)));
            }
        }

        if (source is "github" or "auto")
        {
            var gh = await CheckViaGithubAsync(current, errors, ct);
            if (gh.Checked)
                return gh;
        }

        return Fail(current,
            "无法获取最新版本。" +
            "默认使用 Gitee（https://gitee.com/arikar/pal-world-service-web-tool）；" +
            "也可在 config/servers.yaml 设置 updateSource: auto 或 github。" +
            " 详情：" + string.Join(" | ", errors.Take(3)));
    }

    private async Task<ToolUpdateCheckResult> CheckViaGithubAsync(
        string current,
        List<string> errors,
        CancellationToken ct)
    {
        var owner = string.IsNullOrWhiteSpace(_config.Current.GithubOwner)
            ? "ArikarWang"
            : _config.Current.GithubOwner!;
        var repo = string.IsNullOrWhiteSpace(_config.Current.GithubRepo)
            ? "PalWorld-Service-WebTool"
            : _config.Current.GithubRepo!;

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

        return Fail(current, "GitHub 检查失败");
    }

    private async Task<ToolUpdateCheckResult> CheckViaGiteeApiAsync(string current, CancellationToken ct)
    {
        var owner = string.IsNullOrWhiteSpace(_config.Current.GiteeOwner)
            ? "arikar"
            : _config.Current.GiteeOwner!;
        var repo = string.IsNullOrWhiteSpace(_config.Current.GiteeRepo)
            ? "pal-world-service-web-tool"
            : _config.Current.GiteeRepo!;

        var apiUrl = $"https://gitee.com/api/v5/repos/{owner}/{repo}/releases/latest";
        var client = _httpClientFactory.CreateClient("GitHub");
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PalWorldService", current));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, ct);
        var bodyText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return Fail(current,
                $"HTTP {(int)response.StatusCode}" +
                (bodyText.Length is > 0 and < 240 ? $": {bodyText.Trim()}" : ""));
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

        // Gitee may put files under "assets" and/or require attach_files listing.
        CollectNamedAssets(root, "assets", downloadCandidates, ref assetName, ref assetSize);
        CollectNamedAssets(root, "attach_files", downloadCandidates, ref assetName, ref assetSize);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            AddUnique(downloadCandidates,
                $"https://gitee.com/{owner}/{repo}/releases/download/{tag.Trim()}/{ToolSelfUpdateService.ReleaseAssetName}");
        }

        // Always offer GitHub mirrors of the same tag as download fallbacks.
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var ghOwner = string.IsNullOrWhiteSpace(_config.Current.GithubOwner)
                ? "ArikarWang"
                : _config.Current.GithubOwner!;
            var ghRepo = string.IsNullOrWhiteSpace(_config.Current.GithubRepo)
                ? "PalWorld-Service-WebTool"
                : _config.Current.GithubRepo!;
            var ghAsset =
                $"https://github.com/{ghOwner}/{ghRepo}/releases/download/{tag.Trim()}/{ToolSelfUpdateService.ReleaseAssetName}";
            AddUnique(downloadCandidates, ghAsset);
            foreach (var proxy in BuildProxyCandidates())
            {
                if (proxy is null) continue;
                AddUnique(downloadCandidates, ApplyProxy(proxy, ghAsset));
            }
        }

        var latest = NormalizeVersion(tag ?? "");
        if (string.IsNullOrWhiteSpace(latest))
            return Fail(current, "最新 Release 缺少有效版本号");

        assetName ??= ToolSelfUpdateService.ReleaseAssetName;
        var primary = downloadCandidates.FirstOrDefault();
        var updateAvailable = CompareSemVer(latest, current) > 0;

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

            // Some Gitee payloads expose download URL under "browser_url" / "url".
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
        var assetRaw =
            $"https://github.com/{owner}/{repo}/releases/download/{tagRaw.Trim()}/{ToolSelfUpdateService.ReleaseAssetName}";
        var downloadCandidates = new List<string>();
        AddUnique(downloadCandidates, ApplyProxy(proxy, assetRaw));

        // Prefer Gitee copy of the same version when available as alternate download.
        var giteeOwner = string.IsNullOrWhiteSpace(_config.Current.GiteeOwner)
            ? "arikar"
            : _config.Current.GiteeOwner!;
        var giteeRepo = string.IsNullOrWhiteSpace(_config.Current.GiteeRepo)
            ? "pal-world-service-web-tool"
            : _config.Current.GiteeRepo!;
        AddUnique(downloadCandidates,
            $"https://gitee.com/{giteeOwner}/{giteeRepo}/releases/download/{tagRaw.Trim()}/{ToolSelfUpdateService.ReleaseAssetName}");

        foreach (var p in BuildProxyCandidates())
        {
            if (p is null || string.Equals(p, proxy, StringComparison.OrdinalIgnoreCase))
                continue;
            AddUnique(downloadCandidates, ApplyProxy(p, assetRaw));
        }

        var updateAvailable = CompareSemVer(latest, current) > 0;
        var via = LabelProxy(proxy);
        string message;
        if (!updateAvailable)
            message = $"管理工具已是最新版本（{current}）。来源：GitHub/{via}";
        else
            message = $"管理工具有新版本：{latest}（当前 {current}），可在线更新。来源：GitHub/{via}";

        return new ToolUpdateCheckResult(
            Checked: true,
            UpdateAvailable: updateAvailable,
            CurrentVersion: current,
            LatestVersion: latest,
            ReleaseName: $"PalWorld Service {tagRaw.Trim()}",
            ReleaseUrl: releaseUrl,
            DownloadUrl: downloadCandidates.FirstOrDefault(),
            DownloadUrls: downloadCandidates,
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
            return Fail(current, $"HTTP {(int)response.StatusCode}");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;

        var downloadCandidates = new List<string>();
        string? assetName = null;
        long? assetSize = null;
        CollectNamedAssets(root, "assets", downloadCandidates, ref assetName, ref assetSize);

        // Re-apply proxy to github asset URLs from API.
        if (downloadCandidates.Count > 0 && !string.IsNullOrWhiteSpace(proxy))
        {
            var proxied = downloadCandidates
                .Select(u => ApplyProxy(proxy, u))
                .ToList();
            downloadCandidates.Clear();
            foreach (var u in proxied) AddUnique(downloadCandidates, u);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            AddUnique(downloadCandidates, ApplyProxy(proxy,
                $"https://github.com/{owner}/{repo}/releases/download/{tag}/{ToolSelfUpdateService.ReleaseAssetName}"));

            var giteeOwner = string.IsNullOrWhiteSpace(_config.Current.GiteeOwner)
                ? "arikar"
                : _config.Current.GiteeOwner!;
            var giteeRepo = string.IsNullOrWhiteSpace(_config.Current.GiteeRepo)
                ? "pal-world-service-web-tool"
                : _config.Current.GiteeRepo!;
            AddUnique(downloadCandidates,
                $"https://gitee.com/{giteeOwner}/{giteeRepo}/releases/download/{tag}/{ToolSelfUpdateService.ReleaseAssetName}");
        }

        var latest = NormalizeVersion(tag ?? "");
        if (string.IsNullOrWhiteSpace(latest))
            return Fail(current, "最新 Release 缺少有效版本号");

        assetName ??= ToolSelfUpdateService.ReleaseAssetName;
        var updateAvailable = CompareSemVer(latest, current) > 0;
        return new ToolUpdateCheckResult(
            Checked: true,
            UpdateAvailable: updateAvailable,
            CurrentVersion: current,
            LatestVersion: latest,
            ReleaseName: name,
            ReleaseUrl: htmlUrl,
            DownloadUrl: downloadCandidates.FirstOrDefault(),
            DownloadUrls: downloadCandidates,
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

    public string GetUpdateSource()
    {
        var raw = (_config.Current.UpdateSource ?? "gitee").Trim().ToLowerInvariant();
        return raw switch
        {
            "github" => "github",
            "auto" => "auto",
            _ => "gitee",
        };
    }

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
        if (IsConnectivityFailure(ex, text))
        {
            return "无法连接更新源。" +
                   "默认从 Gitee 下载；也可在 config/servers.yaml 设置 updateSource: auto。" +
                   $" 原始错误：{text}";
        }

        return $"检查管理工具更新失败：{text}";
    }

    private static bool IsConnectivityFailure(Exception ex, string text)
    {
        if (ex is HttpRequestException or TaskCanceledException or SocketException or IOException)
        {
            if (text.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("gitee.com", StringComparison.OrdinalIgnoreCase) ||
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

        return (text.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("gitee.com", StringComparison.OrdinalIgnoreCase)) &&
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
