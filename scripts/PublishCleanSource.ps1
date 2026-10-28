# Saved with UTF-8 BOM for Windows PowerShell 5.1.
param(
    [string]$Repository = "https://github.com/xzlosats111/ASR-UI.git",
    [string]$Proxy = "http://127.0.0.1:10809"
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git for Windows was not found.' }
Set-Location -LiteralPath $Root

if (-not (Test-Path -LiteralPath (Join-Path $Root '.git'))) {
    Write-Host 'Initializing local Git repository...'
    Invoke-Git init
}

Invoke-Git config http.proxy $Proxy
Invoke-Git config http.version HTTP/1.1
Invoke-Git config http.postBuffer 524288000
Invoke-Git config http.lowSpeedLimit 0
Invoke-Git config http.lowSpeedTime 999999
& git remote remove origin 2>$null
Invoke-Git remote add origin $Repository

if (-not (& git config user.name)) { throw 'Git user.name is not configured.' }
if (-not (& git config user.email)) { throw 'Git user.email is not configured.' }

Write-Host 'Fetching remote main...'
Invoke-Git fetch origin main
Invoke-Git rev-parse --verify origin/main

# This intentionally resets only HEAD and index to remote main. The full current
# source tree remains on disk, then git add honours .gitignore and stages only source.
Write-Host 'Rebuilding clean source change set from origin/main...'
Invoke-Git reset --mixed origin/main
Invoke-Git add -A

$excluded = New-Object System.Collections.Generic.List[string]
$staged = @(& git diff --cached --name-only)
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate staged files.' }
foreach ($relative in $staged) {
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }
    $full = Join-Path $Root $relative
    if ((Test-Path -LiteralPath $full -PathType Leaf) -and (Get-Item -LiteralPath $full).Length -gt 5MB) {
        $sizeMiB = [Math]::Round((Get-Item -LiteralPath $full).Length / 1MB, 1)
        & git restore --staged -- $relative
        if ($LASTEXITCODE -ne 0) { throw "Unable to exclude large file: $relative" }
        $excluded.Add("$relative ($sizeMiB MiB)")
    }
}

if ($excluded.Count -gt 0) {
    Write-Host ''
    Write-Host 'These local generated/downloaded files were excluded from Git:' -ForegroundColor Yellow
    $excluded | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

$remaining = @(& git diff --cached --name-only)
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate final staged files.' }
if ($remaining.Count -eq 0) {
    Write-Host 'No source changes remain to push.'
    exit 0
}

Write-Host ''
Write-Host 'Committing clean source update:'
$remaining | ForEach-Object { Write-Host "  $_" }
Invoke-Git commit -m 'Fix ASR-UI v0.1.6 timeline repair build error'
Invoke-Git branch -M main
Write-Host ''
Write-Host 'Pushing main...'
Invoke-Git push -u origin main
Write-Host ''
Write-Host 'SUCCESS. GitHub Actions can now rebuild ASR-UI from clean source.' -ForegroundColor Green
