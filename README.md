# PalWorld Service

幻兽帕鲁 1.0 专用服务器管理端。控制台窗口运行，浏览器打开使用。配置文件驱动服务器列表，按服网页密码登录。

## 快速使用（mini 主机）

1. 在开发机执行：

```powershell
cd d:\Job\PalWorldService
.\scripts\publish.ps1
```

2. 将 `publish\` 整个文件夹拷到 mini 主机（如 `C:\PalWorldService`）
3. 编辑 `config\servers.yaml`（`webPassword` / `adminPassword` / 路径）
4. 双击 `start.bat`（弹出控制台，持续输出日志）
5. 浏览器打开：`http://127.0.0.1:5080` 或 `http://<局域网IP>:5080`
6. 关闭方式：网页右上角「关闭管理服务」，或直接关控制台窗口

## 登录说明

- 主界面显示配置中的服务器列表
- 点击服务器 → 输入该服的 **webPassword**（与帕鲁 AdminPassword 无关）
- 可勾选「记住密码」（保存在浏览器 localStorage）

## 配置文件

[`config/servers.yaml`](config/servers.yaml)：

```yaml
listen: "0.0.0.0:5080"
servers:
  - id: main
    name: 本机帕鲁服务器
    host: 127.0.0.1
    restApiPort: 8212
    adminPassword: "帕鲁REST密码"
    webPassword: "网页登录密码"
    executablePath: "C:/gameserver/steamcmd/steamapps/common/PalServer/PalServer.exe"
    configPath: "C:/gameserver/.../PalWorldSettings.ini"
    logDirectory: "C:/gameserver/.../Logs"
    saveDirectory: "C:/gameserver/.../SaveGames"
```

帕鲁侧需开启：

```ini
RESTAPIEnabled=True
RESTAPIPort=8212
AdminPassword="与 adminPassword 一致"
```

## 功能模块

| 模块 | 说明 |
|------|------|
| 仪表盘 | 在线状态、人数、FPS |
| 玩家 | 列表、踢人、封禁 |
| 控制 | 公告、保存、关闭、本机进程启停 |
| 配置 | 编辑 PalWorldSettings.ini |
| 日志 | 读取服务器日志 |
| 备份 | 存档 zip 备份/恢复 |
| 定时任务 | Cron 公告/保存/关闭/备份 |

## GitHub 自动 Release

仓库已配置 Actions：打 tag 后自动构建 `PalWorldService-win-x64.zip` 并挂到 Release。

```powershell
git add .
git commit -m "..."
git push
git tag v1.0.0
git push origin v1.0.0
```

或在 GitHub → Actions → **Release** → **Run workflow** 手动触发。

mini 主机更新：打开仓库 Releases，下载 zip，解压覆盖（保留本机 `config\servers.yaml`），再运行 `start.bat`。

详见 `.github/workflows/release.yml`。

## 开发

```powershell
# 前端
cd web
npm install
npm run build   # 输出到 src/PalWorldService.Host/wwwroot

# 后端
dotnet run --project src\PalWorldService.Host
```

前端开发热更新：`npm run dev`（代理到 5080）。

## 项目结构

```
config/servers.yaml          # 服务器配置
src/PalWorldService.Shared/  # 配置加载、帕鲁 REST 客户端
src/PalWorldService.Host/    # 控制台 Host、API 模块、静态前端
web/                         # Vue3 源码
scripts/publish.ps1          # 打包
scripts/start.bat            # 启动（随 publish 拷贝）
```

扩展新功能见 [docs/EXTENDING.md](docs/EXTENDING.md)。
