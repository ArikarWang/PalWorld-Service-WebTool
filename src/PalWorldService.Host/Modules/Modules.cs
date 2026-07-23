using PalWorldService.Host.Auth;
using PalWorldService.Host.Services;
using PalWorldService.Shared.Palworld;

namespace PalWorldService.Host.Modules;

public static class ModuleRegistration
{
    public static void MapAllModules(this WebApplication app)
    {
        app.MapServersModule();
        app.MapMonitorModule();
        app.MapServerOpsModule();
        app.MapScheduleModule();
        app.MapSystemModule();
    }
}

public static class ServersModule
{
    public static void MapServersModule(this WebApplication app)
    {
        var g = app.MapGroup("/api/servers");

        g.MapGet("/", (AppConfigProvider config, MonitorService monitor) =>
        {
            var list = config.Current.Servers.Select(s =>
            {
                var snap = monitor.Get(s.Id);
                return new
                {
                    id = s.Id,
                    name = s.Name,
                    host = s.Host,
                    restApiPort = s.RestApiPort,
                    gamePort = s.GamePort,
                    isOnline = snap?.IsOnline ?? false,
                    playerCount = snap?.Metrics?.CurrentPlayerNum,
                    maxPlayers = snap?.Metrics?.MaxPlayerNum,
                    checkedAt = snap?.CheckedAt
                };
            });
            return Results.Ok(list);
        });

        g.MapGet("/{serverId}", (string serverId, AppConfigProvider config, MonitorService monitor) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            var snap = monitor.Get(s.Id);
            return Results.Ok(new
            {
                id = s.Id,
                name = s.Name,
                host = s.Host,
                restApiPort = s.RestApiPort,
                gamePort = s.GamePort,
                hasLocalPaths = !string.IsNullOrWhiteSpace(s.ExecutablePath)
                    || !string.IsNullOrWhiteSpace(s.ConfigPath)
                    || !string.IsNullOrWhiteSpace(s.LogDirectory)
                    || !string.IsNullOrWhiteSpace(s.SaveDirectory),
                isOnline = snap?.IsOnline ?? false
            });
        });

        g.MapPost("/{serverId}/login", (string serverId, LoginRequest body, AppConfigProvider config, SessionService sessions, HttpContext http) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound(new { error = "Server not found" });
            if (string.IsNullOrEmpty(body.Password) || body.Password != s.WebPassword)
                return Results.Json(new { error = "Invalid password" }, statusCode: StatusCodes.Status401Unauthorized);

            var token = sessions.Create(serverId);
            http.Response.Cookies.Append(SessionService.CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddHours(12)
            });
            return Results.Ok(new { ok = true, token, serverId });
        });

        g.MapPost("/{serverId}/logout", (string serverId, SessionService sessions, HttpContext http) =>
        {
            var token = http.Request.Cookies[SessionService.CookieName];
            sessions.Revoke(token);
            http.Response.Cookies.Delete(SessionService.CookieName);
            return Results.Ok(new { ok = true });
        });

        g.MapGet("/{serverId}/session", (string serverId, SessionService sessions, HttpContext http) =>
        {
            var token = http.Request.Cookies[SessionService.CookieName]
                ?? http.Request.Headers["X-Pal-Session"].FirstOrDefault();
            var ok = sessions.TryValidate(token, serverId, out _);
            return Results.Ok(new { authenticated = ok });
        });
    }

    public record LoginRequest(string Password);
}

public static class MonitorModule
{
    public static void MapMonitorModule(this WebApplication app)
    {
        app.MapGet("/api/monitor", (MonitorService monitor) => Results.Ok(monitor.GetAll()));

        app.MapPost("/api/monitor/refresh", async (MonitorService monitor, CancellationToken ct) =>
        {
            await monitor.RefreshAllAsync(ct);
            return Results.Ok(monitor.GetAll());
        });

        var g = app.MapGroup("/api/servers/{serverId}/monitor").RequireServerSession();
        g.MapGet("/", (string serverId, MonitorService monitor) =>
        {
            var snap = monitor.Get(serverId);
            return snap is null ? Results.NotFound() : Results.Ok(snap);
        });
        g.MapPost("/refresh", async (string serverId, MonitorService monitor, CancellationToken ct) =>
        {
            await monitor.RefreshOneAsync(serverId, ct);
            return Results.Ok(monitor.Get(serverId));
        });
    }
}

public static class ServerOpsModule
{
    public static void MapServerOpsModule(this WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{serverId}").RequireServerSession();

        g.MapGet("/players", async (string serverId, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            return Results.Ok(await client.GetPlayersAsync(s, ct));
        });

        g.MapPost("/announce", async (string serverId, MessageBody body, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await client.AnnounceAsync(s, body.Message ?? "", ct);
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/save", async (string serverId, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await client.SaveAsync(s, ct);
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/shutdown", async (string serverId, ShutdownBody body, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await client.ShutdownAsync(s, body.WaitTime <= 0 ? 60 : body.WaitTime, body.Message, ct);
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/stop", async (string serverId, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await client.StopAsync(s, ct);
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/kick", async (string serverId, UserActionBody body, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await client.KickAsync(s, body.UserId, body.Message, ct);
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/ban", async (string serverId, UserActionBody body, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await client.BanAsync(s, body.UserId, body.Message, ct);
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/unban", async (string serverId, UserActionBody body, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await client.UnbanAsync(s, body.UserId, ct);
            return Results.Ok(new { ok = true });
        });

        g.MapGet("/settings", async (string serverId, AppConfigProvider config, IPalworldRestClient client, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            return Results.Ok(new { raw = await client.GetSettingsAsync(s, ct) });
        });

        g.MapGet("/config", async (string serverId, AppConfigProvider config, LocalOpsService local, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            return Results.Ok(new { content = await local.ReadConfigAsync(s, ct) });
        });

        g.MapPut("/config", async (string serverId, ConfigBody body, AppConfigProvider config, LocalOpsService local, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            await local.WriteConfigAsync(s, body.Content ?? "", ct);
            return Results.Ok(new { ok = true });
        });

        g.MapGet("/logs", async (string serverId, int? lines, AppConfigProvider config, LocalOpsService local, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            return Results.Ok(await local.ReadLogsAsync(s, lines ?? 200, ct));
        });

        g.MapGet("/process", (string serverId, AppConfigProvider config, LocalOpsService local) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            return Results.Ok(new { running = local.IsProcessRunning(s) });
        });

        g.MapPost("/process/start", (string serverId, AppConfigProvider config, LocalOpsService local) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            local.StartProcess(s);
            return Results.Ok(new { ok = true });
        });

        g.MapPost("/process/stop", (string serverId, AppConfigProvider config, LocalOpsService local) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            local.StopProcess(s);
            return Results.Ok(new { ok = true });
        });

        g.MapGet("/backups", (string serverId, AppConfigProvider config, LocalOpsService local) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            return Results.Ok(local.ListBackups(s));
        });

        g.MapPost("/backups", async (string serverId, AppConfigProvider config, LocalOpsService local, CancellationToken ct) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            return Results.Ok(await local.CreateBackupAsync(s, ct));
        });

        g.MapPost("/backups/{fileName}/restore", (string serverId, string fileName, AppConfigProvider config, LocalOpsService local) =>
        {
            var s = config.GetServer(serverId);
            if (s is null) return Results.NotFound();
            local.RestoreBackup(s, fileName);
            return Results.Ok(new { ok = true });
        });
    }

    public record MessageBody(string? Message);
    public record ShutdownBody(int WaitTime, string? Message);
    public record UserActionBody(string UserId, string? Message);
    public record ConfigBody(string? Content);
}

public static class ScheduleModule
{
    public static void MapScheduleModule(this WebApplication app)
    {
        app.MapGet("/api/schedules", (ScheduleStore store) => Results.Ok(store.GetAll()));

        var g = app.MapGroup("/api/servers/{serverId}/schedules").RequireServerSession();

        g.MapGet("/", (string serverId, ScheduleStore store) => Results.Ok(store.GetByServer(serverId)));

        g.MapPost("/", (string serverId, ScheduledTaskDefinition body, ScheduleStore store) =>
        {
            body.ServerId = serverId;
            return Results.Ok(store.Add(body));
        });

        g.MapDelete("/{taskId}", (string serverId, string taskId, ScheduleStore store) =>
            store.Delete(taskId) ? Results.Ok(new { ok = true }) : Results.NotFound());
    }
}

public static class SystemModule
{
    public static void MapSystemModule(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

        app.MapPost("/api/system/shutdown", (IHostApplicationLifetime lifetime, ILoggerFactory logs) =>
        {
            logs.CreateLogger("System").LogWarning("Shutdown requested from web UI");
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                lifetime.StopApplication();
            });
            return Results.Ok(new { ok = true, message = "Shutting down..." });
        });
    }
}
