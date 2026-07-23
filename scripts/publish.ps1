# Build Vue frontend + publish self-contained win-x64 console host
param(
    [string]$OutputDir = "publish",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

Push-Location $Root
try {
    Write-Host ">>> Building Vue frontend..." -ForegroundColor Cyan
    Push-Location "web"
    if (-not (Test-Path "node_modules")) { npm install }
    npm run build
    Pop-Location

    Write-Host ">>> Publishing Host (win-x64 self-contained)..." -ForegroundColor Cyan
    if (Test-Path $OutputDir) {
        # Keep user config if present
        $keepConfig = Join-Path $OutputDir "config\servers.yaml"
        $tmpConfig = $null
        if (Test-Path $keepConfig) {
            $tmpConfig = Join-Path $env:TEMP "pal-servers-yaml-backup.yaml"
            Copy-Item $keepConfig $tmpConfig -Force
        }
        Remove-Item $OutputDir -Recurse -Force
        New-Item -ItemType Directory -Path $OutputDir | Out-Null
    }

    dotnet publish "src\PalWorldService.Host\PalWorldService.Host.csproj" `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -o $OutputDir

    New-Item -ItemType Directory -Path (Join-Path $OutputDir "config") -Force | Out-Null
    if ($tmpConfig -and (Test-Path $tmpConfig)) {
        Copy-Item $tmpConfig (Join-Path $OutputDir "config\servers.yaml") -Force
        Remove-Item $tmpConfig -Force
        Write-Host "Restored existing config\servers.yaml" -ForegroundColor Yellow
    } else {
        Copy-Item "config\servers.yaml" (Join-Path $OutputDir "config\servers.yaml") -Force
    }

    Copy-Item "scripts\start.bat" (Join-Path $OutputDir "start.bat") -Force

    Write-Host ""
    Write-Host "Publish done: $Root\$OutputDir" -ForegroundColor Green
    Write-Host "1. Edit config\servers.yaml (webPassword / adminPassword / paths)"
    Write-Host "2. Double-click start.bat"
    Write-Host "3. Open http://127.0.0.1:5080"
}
finally {
    Pop-Location
}
