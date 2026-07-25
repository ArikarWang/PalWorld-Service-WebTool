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

# Gitee rejects pushes from shallow clones ("shallow update not allowed").
try {
    git fetch --unshallow 2>$null | Out-Null
} catch {}
try {
    git fetch --tags --force origin 2>$null | Out-Null
} catch {}

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

$pushMain = git push gitee "HEAD:refs/heads/$target" --force 2>&1
Write-Host $pushMain
if ($LASTEXITCODE -ne 0) {
    throw "Failed to push branch to Gitee (exit $LASTEXITCODE). If this mentions shallow update, ensure checkout fetch-depth: 0."
}

$pushTag = git push gitee "refs/tags/${Version}:refs/tags/${Version}" --force 2>&1
Write-Host $pushTag
if ($LASTEXITCODE -ne 0) {
    throw "Failed to push tag $Version to Gitee (exit $LASTEXITCODE)."
}

Write-Host "Ensuring Gitee release $Version ..."
$releases = @()
try {
    $releases = @(Invoke-GiteeJson -Method GET -Url "$apiBase/repos/$owner/$repo/releases?access_token=$token&per_page=100")
} catch {
    Write-Host "List releases returned empty/error: $($_.Exception.Message)"
}
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

try {
    $files = @(Invoke-GiteeJson -Method GET -Url "$apiBase/repos/$owner/$repo/releases/$releaseId/attach_files?access_token=$token")
    foreach ($f in $files) {
        if ($f.name -eq $assetName) {
            Write-Host "Deleting existing attachment $($f.name) ($($f.id))"
            Invoke-GiteeJson -Method DELETE -Url "$apiBase/repos/$owner/$repo/releases/$releaseId/attach_files/$($f.id)?access_token=$token" | Out-Null
        }
    }
} catch {
    Write-Host "List/delete attach_files skipped: $($_.Exception.Message)"
}

Write-Host "Uploading $assetName (timeout 10m) ..."
$uploadUrl = "$apiBase/repos/$owner/$repo/releases/$releaseId/attach_files"
$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if ($null -eq $curl) {
    throw "curl.exe is required to upload multipart attach_files"
}

# Gitee attach API expects access_token + release_id + file in multipart form.
& curl.exe -sS -f --http1.1 `
    --connect-timeout 30 `
    --max-time 600 `
    --retry 2 `
    --retry-delay 5 `
    -X POST `
    -F "access_token=$token" `
    -F "release_id=$releaseId" `
    -F "file=@${ZipPath};filename=$assetName;type=application/zip" `
    $uploadUrl | Out-Host

if ($LASTEXITCODE -ne 0) {
    throw "Gitee attach_files upload failed (exit $LASTEXITCODE). You can manually upload $assetName to https://gitee.com/$owner/$repo/releases/$Version"
}

Write-Host "Gitee release synced: https://gitee.com/$owner/$repo/releases/$Version"
