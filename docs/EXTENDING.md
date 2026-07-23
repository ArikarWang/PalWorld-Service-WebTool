# 功能扩展约定

## 后端加模块

1. 在 [`src/PalWorldService.Host/Modules/`](../src/PalWorldService.Host/Modules/) 增加静态扩展方法，例如：

```csharp
public static class ModsModule
{
    public static void MapModsModule(this WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{serverId}/mods").RequireServerSession();
        g.MapGet("/", () => Results.Ok(Array.Empty<object>()));
    }
}
```

2. 在 `ModuleRegistration.MapAllModules` 中调用 `app.MapModsModule();`
3. 需要的服务在 `Program.cs` 里 `builder.Services.AddSingleton/AddScoped...`
4. 若需后台轮询，在 `Background/` 增加 `BackgroundService` 并注册

鉴权：凡操作某一服务器的接口，挂在带 `{serverId}` 的 group 上并 `.RequireServerSession()`。

帕鲁 REST 调用复用 `IPalworldRestClient`；本地文件操作复用 `LocalOpsService`。

## 前端加页面

1. 在 `web/src/views/` 新增 Vue 页面
2. 在 `web/src/router/index.ts` 的 `/servers/:id` children 中加路由
3. 在 `ServerLayout.vue` 侧栏加链接
4. 在 `web/src/api.ts` 增加对应 API 方法
5. `npm run build` 输出到 Host 的 `wwwroot`

## 配置

服务器列表只从 `config/servers.yaml` 读取。新增一台服 = 在 YAML 增加一项并重启管理服务。
