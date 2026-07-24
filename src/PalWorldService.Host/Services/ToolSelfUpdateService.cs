using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PalWorldService.Host.Services;

/// <summary>
/// Downloads the latest GitHub Release zip, stages it, then launches a helper that
/// replaces files after this process exits and restarts start.bat.
/// </summary>
public class ToolSelfUpdateService
{
    public const string ReleaseAssetName = "PalWorldService-win-x64.zip";

    private readonly ToolUpdateCheckService _check;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ToolSelfUpdateService> _logger;

    public ToolSelfUpdateService(
        ToolUpdateCheckService check,
        IHttpClientFactory httpClientFactory,
        ILogger<ToolSelfUpdateService> logger)
    {
        _check = check;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ToolApplyResult> ApplyAsync(IHostApplicationLifetime lifetime, CancellationToken ct = default)
    {
        var check = await _check.CheckAsync(ct);
        if (!check.Checked)
            throw new InvalidOperationException(check.Message ?? "无法检查更新。");
        if (!check.UpdateAvailable)
            throw new InvalidOperationException("当前已是最新版本，无需更新。");
        if (string.IsNullOrWhiteSpace(check.DownloadUrl))
            throw new InvalidOperationException(
                $"最新 Release 未找到资源 {ReleaseAssetName}，请手动从 GitHub 下载。");

        var installDir = Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var workRoot = Path.Combine(Path.GetTempPath(), "PalWorldService-update");
        var stagingDir = Path.Combine(workRoot, "staging");
        var zipPath = Path.Combine(workRoot, ReleaseAssetName);
        var logPath = Path.Combine(workRoot, "apply.log");
        var helperPath = Path.Combine(workRoot, "apply-update.bat");

        Directory.CreateDirectory(workRoot);
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, true);
        Directory.CreateDirectory(stagingDir);

        _logger.LogInformation(
            "Downloading tool update {Version} from {Url}",
            check.LatestVersion,
            check.DownloadUrl);

        await DownloadAsync(check.DownloadUrl!, zipPath, ct);
        ExtractZip(zipPath, stagingDir);

        if (!File.Exists(Path.Combine(stagingDir, "PalWorldService.exe")))
            throw new InvalidOperationException("更新包缺少 PalWorldService.exe，已中止。");

        var pid = Environment.ProcessId;
        await File.WriteAllTextAsync(helperPath, BuildHelperScript(), ct);

        LaunchHelper(helperPath, installDir, stagingDir, pid, logPath);
        _logger.LogWarning(
            "Tool update helper launched. Will replace install at {Install} then restart. Log: {Log}",
            installDir,
            logPath);

        _ = Task.Run(async () =>
        {
            await Task.Delay(800);
            lifetime.StopApplication();
        }, CancellationToken.None);

        return new ToolApplyResult(
            Ok: true,
            TargetVersion: check.LatestVersion,
            Message: $"正在更新到 {check.LatestVersion}，管理服务即将退出并自动重启。请稍候在离线页点击重新检测。",
            LogPath: logPath);
    }

    private async Task DownloadAsync(string url, string zipPath, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("GitHubDownload");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
            "PalWorldService",
            ToolUpdateCheckService.GetCurrentVersion()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(zipPath);
        await input.CopyToAsync(output, ct);
    }

    private static void ExtractZip(string zipPath, string stagingDir)
    {
        ZipFile.ExtractToDirectory(zipPath, stagingDir, overwriteFiles: true);

        // If zip unexpectedly wraps a single root folder, unwrap it.
        var entries = Directory.GetFileSystemEntries(stagingDir);
        if (entries.Length == 1 &&
            Directory.Exists(entries[0]) &&
            !File.Exists(Path.Combine(stagingDir, "PalWorldService.exe")))
        {
            var inner = entries[0];
            foreach (var child in Directory.GetFileSystemEntries(inner))
            {
                var name = Path.GetFileName(child);
                var dest = Path.Combine(stagingDir, name);
                if (Directory.Exists(child))
                    Directory.Move(child, dest);
                else
                    File.Move(child, dest, overwrite: true);
            }
            Directory.Delete(inner, true);
        }
    }

    private static void LaunchHelper(
        string helperPath,
        string installDir,
        string stagingDir,
        int pid,
        string logPath)
    {
        var args =
            $"/c start \"PalWorldServiceUpdate\" /min \"{helperPath}\" " +
            $"\"{installDir}\" \"{stagingDir}\" {pid} \"{logPath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? Path.GetTempPath()
        });
    }

    /// <summary>
    /// Windows helper: wait for PID, copy staging over install (preserve config/data/backups/logs), restart.
    /// %1 install  %2 staging  %3 pid  %4 log
    /// </summary>
    internal static string BuildHelperScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal EnableExtensions");
        sb.AppendLine("set \"INSTALL=%~1\"");
        sb.AppendLine("set \"STAGING=%~2\"");
        sb.AppendLine("set \"PID=%~3\"");
        sb.AppendLine("set \"LOG=%~4\"");
        sb.AppendLine("if \"%LOG%\"==\"\" set \"LOG=%TEMP%\\PalWorldService-update\\apply.log\"");
        sb.AppendLine("echo [%date% %time%] apply start >> \"%LOG%\"");
        sb.AppendLine("echo install=%INSTALL% >> \"%LOG%\"");
        sb.AppendLine("echo staging=%STAGING% >> \"%LOG%\"");
        sb.AppendLine("echo pid=%PID% >> \"%LOG%\"");
        sb.AppendLine();
        sb.AppendLine("set /a WAITED=0");
        sb.AppendLine(":waitloop");
        sb.AppendLine("tasklist /FI \"PID eq %PID%\" 2>NUL | find \"%PID%\" >NUL");
        sb.AppendLine("if errorlevel 1 goto copyfiles");
        sb.AppendLine("timeout /t 1 /nobreak >NUL");
        sb.AppendLine("set /a WAITED+=1");
        sb.AppendLine("if %WAITED% GEQ 120 (");
        sb.AppendLine("  echo [%date% %time%] timeout waiting for pid %PID% >> \"%LOG%\"");
        sb.AppendLine("  goto copyfiles");
        sb.AppendLine(")");
        sb.AppendLine("goto waitloop");
        sb.AppendLine();
        sb.AppendLine(":copyfiles");
        sb.AppendLine("echo [%date% %time%] copying files >> \"%LOG%\"");
        sb.AppendLine("if exist \"%INSTALL%\\config\\servers.yaml\" (");
        sb.AppendLine("  copy /Y \"%INSTALL%\\config\\servers.yaml\" \"%TEMP%\\pal-servers-yaml.bak\" >NUL");
        sb.AppendLine("  echo preserved servers.yaml >> \"%LOG%\"");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("robocopy \"%STAGING%\" \"%INSTALL%\" /E /XD data backups logs /R:8 /W:2 /NFL /NDL /NJH /NJS /NP >> \"%LOG%\" 2>&1");
        sb.AppendLine("set \"RC=%ERRORLEVEL%\"");
        sb.AppendLine("echo robocopy exit=%RC% >> \"%LOG%\"");
        sb.AppendLine("if %RC% GEQ 8 (");
        sb.AppendLine("  echo [%date% %time%] robocopy failed >> \"%LOG%\"");
        sb.AppendLine("  exit /b 1");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("if exist \"%TEMP%\\pal-servers-yaml.bak\" (");
        sb.AppendLine("  if not exist \"%INSTALL%\\config\" mkdir \"%INSTALL%\\config\"");
        sb.AppendLine("  copy /Y \"%TEMP%\\pal-servers-yaml.bak\" \"%INSTALL%\\config\\servers.yaml\" >NUL");
        sb.AppendLine("  echo restored servers.yaml >> \"%LOG%\"");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("echo [%date% %time%] starting service >> \"%LOG%\"");
        sb.AppendLine("cd /d \"%INSTALL%\"");
        sb.AppendLine("if exist \"%INSTALL%\\start.bat\" (");
        sb.AppendLine("  start \"PalWorld Service\" \"%INSTALL%\\start.bat\"");
        sb.AppendLine(") else (");
        sb.AppendLine("  start \"PalWorld Service\" \"%INSTALL%\\PalWorldService.exe\"");
        sb.AppendLine(")");
        sb.AppendLine("echo [%date% %time%] apply done >> \"%LOG%\"");
        sb.AppendLine("exit /b 0");
        return sb.ToString();
    }
}

public record ToolApplyResult(
    bool Ok,
    string? TargetVersion,
    string? Message,
    string? LogPath);
