# Sync a local zip to a Gitee Release (create/update release + upload attach file).
# Requires env: GITEE_TOKEN
# Optional env: GITEE_OWNER, GITEE_REPO, GITEE_TARGET_COMMITISH
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$ZipPath
)

$ErrorActionPreference = "Stop"

$token = $env:GITEE_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Warning "GITEE_TOKEN is not set; skip Gitee sync."
    exit 0
}

if (-not (Test-Path -LiteralPath $ZipPath)) {
    throw "Zip not found: $ZipPath"
}

$owner = if ([string]::IsNullOrWhiteSpace($env:GITEE_OWNER)) { "arikar" } else { $env:GITEE_OWNER }
$repo = if ([string]::IsNullOrWhiteSpace($env:GITEE_REPO)) { "pal-world-service-web-tool" } else { $env:GITEE_REPO }
$target = if ([string]::IsNullOrWhiteSpace($env:GITEE_TARGET_COMMITISH)) { "main" } else { $env:GITEE_TARGET_COMMITISH }
$assetName = [IO.Path]::GetFileName($ZipPath)
$apiBase = "https://gitee.com/api/v5"

function Invoke-GiteeJson {
    param(
        [string]$Method,
        [string]$Url,
        [hashtable]$Body = $null
    )
    $headers = @{ "User-Agent" = "PalWorldService-ReleaseSync" }
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Url -Headers $headers
    }
    $json = $Body | ConvertTo-Json -Depth 6 -Compress
    return Invoke-RestMethod -Method $Method -Uri $Url -Headers $headers -ContentType "application/json" -Body $json
}

if (-not (git rev-parse -q --verify "refs/tags/$Version")) {
    Write-Host "Creating local tag $Version"
    git config user.email "github-actions[bot]@users.noreply.github.com"
    git config user.name "github-actions[bot]"
    git tag -a $Version -m "PalWorld Service $Version"
}

Write-Host "Pushing commit/tag to Gitee $owner/$repo ..."
$remoteUrl = "https://oauth2:${token}@gitee.com/${owner}/${repo}.git"
git remote remove gitee 2>$null | Out-Null
git remote add gitee $remoteUrl
git push gitee "HEAD:refs/heads/$target" --force
git push gitee "refs/tags/${Version}:refs/tags/${Version}" --force

Write-Host "Ensuring Gitee release $Version ..."
$releases = Invoke-GiteeJson -Method GET -Url "$apiBase/repos/$owner/$repo/releases?access_token=$token&per_page=100"
$existing = @($releases | Where-Object { $_.tag_name -eq $Version }) | Select-Object -First 1

$payload = @{
    access_token     = $token
    tag_name         = $Version
    name             = "PalWorld Service $Version"
    body             = "Synced from GitHub Release $Version"
    target_commitish = $target
    prerelease       = $false
}

if ($null -eq $existing) {
    $release = Invoke-GiteeJson -Method POST -Url "$apiBase/repos/$owner/$repo/releases" -Body $payload
} else {
    $release = Invoke-GiteeJson -Method PATCH -Url "$apiBase/repos/$owner/$repo/releases/$($existing.id)" -Body $payload
}

$releaseId = $release.id
Write-Host "Gitee release id=$releaseId"

# Remove existing same-named attachments
try {
    $files = Invoke-GiteeJson -Method GET -Url "$apiBase/repos/$owner/$repo/releases/$releaseId/attach_files?access_token=$token"
    foreach ($f in @($files)) {
        if ($f.name -eq $assetName) {
            Write-Host "Deleting existing attachment $($f.name) ($($f.id))"
            Invoke-GiteeJson -Method DELETE -Url "$apiBase/repos/$owner/$repo/releases/$releaseId/attach_files/$($f.id)?access_token=$token" | Out-Null
        }
    }
} catch {
    Write-Host "List/delete attach_files skipped: $($_.Exception.Message)"
}

Write-Host "Uploading $assetName ..."
$uploadUrl = "$apiBase/repos/$owner/$repo/releases/$releaseId/attach_files"
$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if ($null -eq $curl) {
    throw "curl.exe is required to upload multipart attach_files"
}

& curl.exe -sS -f -X POST `
    -F "access_token=$token" `
    -F "file=@${ZipPath};filename=$assetName" `
    $uploadUrl | Out-Host

if ($LASTEXITCODE -ne 0) {
    throw "Gitee attach_files upload failed (exit $LASTEXITCODE)"
}

Write-Host "Gitee release synced: https://gitee.com/$owner/$repo/releases/$Version"
