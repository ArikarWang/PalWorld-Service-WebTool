# Build win-x64 zip and publish a Gitee Release (primary release path).
# Usage (在国内网络本机执行，上传更快更稳):
#   $env:GITEE_TOKEN = "<your token>"
#   .\scripts\release-gitee.ps1 -Version v1.0.14
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Owner = "arikar",
    [string]$Repo = "pal-world-service-web-tool",
    [string]$TargetCommitish = "main",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$token = $env:GITEE_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "请先设置环境变量 GITEE_TOKEN（Gitee 私人令牌，需仓库 Releases 写权限）"
}

if ($Version -notmatch '^v?\d+\.\d+') {
    throw "Version 格式应为 v1.0.14 或 1.0.14"
}
if (-not $Version.StartsWith("v")) { $Version = "v$Version" }
$verNum = $Version.TrimStart("v")

Write-Host ">>> Build frontend + publish ($Version)" -ForegroundColor Cyan
& "$PSScriptRoot\publish.ps1" -OutputDir $OutputDir -Configuration Release -Version $verNum

$artifacts = Join-Path $Root "artifacts"
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$zipPath = Join-Path $artifacts "PalWorldService-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $OutputDir "*") -DestinationPath $zipPath -Force
Write-Host "Zip: $zipPath" -ForegroundColor Green

if (-not (git rev-parse -q --verify "refs/tags/$Version")) {
    git tag -a $Version -m "PalWorld Service $Version"
}

$remoteUrl = "https://oauth2:${token}@gitee.com/${Owner}/${Repo}.git"
git remote remove gitee 2>$null | Out-Null
git remote add gitee $remoteUrl
Write-Host ">>> Push branch/tag to Gitee" -ForegroundColor Cyan
git push gitee "HEAD:refs/heads/$TargetCommitish"
git push gitee "refs/tags/${Version}:refs/tags/${Version}" --force

$apiBase = "https://gitee.com/api/v5"
function Invoke-GiteeJson([string]$Method, [string]$Url, [hashtable]$Body = $null) {
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Url -Headers @{ "User-Agent" = "PalWorldService-Release" }
    }
    $json = $Body | ConvertTo-Json -Depth 6 -Compress
    return Invoke-RestMethod -Method $Method -Uri $Url -Headers @{ "User-Agent" = "PalWorldService-Release" } `
        -ContentType "application/json" -Body $json
}

Write-Host ">>> Create/update Gitee Release $Version" -ForegroundColor Cyan
$releases = @()
try {
    $releases = @(Invoke-GiteeJson GET "$apiBase/repos/$Owner/$Repo/releases?access_token=$token&per_page=100")
} catch {}
$existing = @($releases | Where-Object { $_.tag_name -eq $Version }) | Select-Object -First 1
$payload = @{
    access_token     = $token
    tag_name         = $Version
    name             = "PalWorld Service $Version"
    body             = "PalWorld Service $Version"
    target_commitish = $TargetCommitish
    prerelease       = $false
}
if ($null -eq $existing) {
    $release = Invoke-GiteeJson POST "$apiBase/repos/$Owner/$Repo/releases" $payload
} else {
    $release = Invoke-GiteeJson PATCH "$apiBase/repos/$Owner/$Repo/releases/$($existing.id)" $payload
}
$releaseId = $release.id

try {
    $files = @(Invoke-GiteeJson GET "$apiBase/repos/$Owner/$Repo/releases/$releaseId/attach_files?access_token=$token")
    foreach ($f in $files) {
        if ($f.name -eq "PalWorldService-win-x64.zip") {
            Invoke-GiteeJson DELETE "$apiBase/repos/$Owner/$Repo/releases/$releaseId/attach_files/$($f.id)?access_token=$token" | Out-Null
        }
    }
} catch {}

Write-Host ">>> Upload PalWorldService-win-x64.zip" -ForegroundColor Cyan
& curl.exe -sS -f --http1.1 --connect-timeout 30 --max-time 600 `
    -X POST `
    -F "access_token=$token" `
    -F "release_id=$releaseId" `
    -F "file=@${zipPath};filename=PalWorldService-win-x64.zip;type=application/zip" `
    "$apiBase/repos/$Owner/$Repo/releases/$releaseId/attach_files" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "附件上传失败" }

Write-Host ""
Write-Host "Done: https://gitee.com/$Owner/$Repo/releases/$Version" -ForegroundColor Green
Write-Host "客户端「检查工具更新」将从此 Release 拉取安装包。"
