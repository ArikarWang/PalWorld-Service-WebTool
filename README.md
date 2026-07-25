# PalWorld Service

幻兽帕鲁 1.0 专用服务器管理端。控制台窗口运行，浏览器打开使用。配置文件驱动服务器列表，按服网页密码登录。

**代码与发版仓库（主仓库）：** https://gitee.com/arikar/pal-world-service-web-tool

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

也可直接从 Gitee Release 下载 `PalWorldService-win-x64.zip`：  
https://gitee.com/arikar/pal-world-service-web-tool/releases

## 登录说明

- 主界面显示配置中的服务器列表
- 点击服务器 → 输入该服的 **webPassword**（与帕鲁 AdminPassword 无关）
- 可勾选「记住密码」（保存在浏览器 localStorage）

## 配置文件

[`config/servers.yaml`](config/servers.yaml)：

```yaml
listen: "0.0.0.0:5080"
# giteeOwner: arikar
# giteeRepo: pal-world-service-web-tool
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
| 工具更新 | 从 Gitee Release 检查/一键更新 |

## 发版（Gitee）

管理工具更新**只认 Gitee Release** 附件 `PalWorldService-win-x64.zip`。

### 推荐：本机一键发版（国内网络）

```powershell
$env:GITEE_TOKEN = "<Gitee私人令牌>"
.\scripts\release-gitee.ps1 -Version v1.0.14
```

脚本会：构建前端 → 发布 win-x64 → 打 zip → 推送到 Gitee → 创建 Release 并上传附件。

### 可选：Gitee 流水线自动发版

新版流水线读取仓库内 [`.workflow/release.yml`](.workflow/release.yml)：

1. Gitee 仓库打开「流水线」，确认卡片「发布 win-x64」
2. 推送 tag（如 `v1.0.16`）后自动：装 .NET → 构建 → 上传 `PalWorldService-win-x64.zip`
3. 若失败：打开「构建历史」→ 失败步骤日志；常见原因是缺外网拉 SDK / 缺 `release@gitee` 插件

云端搞不定时，用上面的本机 `release-gitee.ps1`（国内上传更稳）。

### 客户端更新

网页服务器列表 →「检查工具更新」→「下载并更新」。或手动下载 Release zip，解压覆盖（保留本机 `config\servers.yaml`），再运行 `start.bat`。

## 将本地仓库切到 Gitee

```powershell
git remote remove origin
git remote add origin https://gitee.com/arikar/pal-world-service-web-tool.git
git push -u origin main --tags
```

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
scripts/publish.ps1          # 本地打包
scripts/release-gitee.ps1    # 打包并发布到 Gitee Release
scripts/start.bat            # 启动（随 publish 拷贝）
.workflow/release.yml        # Gitee 流水线（推送 v* tag 自动发版）
```


扩展新功能见 [docs/EXTENDING.md](docs/EXTENDING.md)。
