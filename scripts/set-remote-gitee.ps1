# Point this clone at Gitee as the only origin (no GitHub bridge).
# Usage:
#   .\scripts\set-remote-gitee.ps1
#   .\scripts\set-remote-gitee.ps1 -Token $env:GITEE_TOKEN   # optional, for authenticated push URL
param(
    [string]$Owner = "arikar",
    [string]$Repo = "pal-world-service-web-tool",
    [string]$Token = ""
)

$ErrorActionPreference = "Stop"

if (-not [string]::IsNullOrWhiteSpace($Token)) {
    $url = "https://oauth2:${Token}@gitee.com/${Owner}/${Repo}.git"
} else {
    $url = "https://gitee.com/${Owner}/${Repo}.git"
}

git remote remove origin 2>$null | Out-Null
git remote add origin $url
git remote remove github 2>$null | Out-Null

Write-Host "origin => $url" -ForegroundColor Green
git remote -v
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  git fetch origin"
Write-Host "  git branch -u origin/main main"
Write-Host "  git push -u origin main"
