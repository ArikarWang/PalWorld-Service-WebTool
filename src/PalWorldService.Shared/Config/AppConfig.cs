namespace PalWorldService.Shared.Config;

public class AppConfig
{
    public string Listen { get; set; } = "0.0.0.0:5080";
    public string DataDirectory { get; set; } = "data";
    public string BackupDirectory { get; set; } = "backups";
    public int MonitorIntervalSeconds { get; set; } = 30;

    /// <summary>用于检查管理工具自身更新的 GitHub 仓库。</summary>
    public string GithubOwner { get; set; } = "ArikarWang";
    public string GithubRepo { get; set; } = "PalWorld-Service-WebTool";

    public List<ServerConfig> Servers { get; set; } = [];
}

public class ServerConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int RestApiPort { get; set; } = 8212;
    public int GamePort { get; set; } = 8211;

    /// <summary>帕鲁 REST API AdminPassword，不下发到前端</summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>网页登录该服的密码，与 AdminPassword 无关</summary>
    public string WebPassword { get; set; } = string.Empty;

    public string? ExecutablePath { get; set; }
    public string? ConfigPath { get; set; }
    public string? LogDirectory { get; set; }
    public string? SaveDirectory { get; set; }

    /// <summary>可选。SteamCMD 可执行文件路径；留空则尝试从 executablePath 推导。</summary>
    public string? SteamCmdPath { get; set; }

    /// <summary>可选。Steam AppId，帕鲁专用服默认 2394010。</summary>
    public int? SteamAppId { get; set; }

    public string RestApiBaseUrl => $"http://{Host}:{RestApiPort}/v1/api";
}
