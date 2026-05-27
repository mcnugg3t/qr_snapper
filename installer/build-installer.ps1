# Builds QRSnapperSetup.exe end-to-end.
#
# What it does:
#   1. Runs dotnet publish to produce a fresh self-contained Release build
#      under src\QrSnip\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\
#   2. Invokes Inno Setup's compiler (ISCC.exe) on installer\QRSnapper.iss
#   3. Output installer EXE lands in installer\Output\QRSnapperSetup.exe
#
# Run from the repo root: .\installer\build-installer.ps1
#
# Pure ASCII per the project's PowerShell encoding convention -- PowerShell
# 5.1 misparses non-ASCII bytes in .ps1 files without a UTF-8 BOM.

$ErrorActionPreference = "Stop"

# Resolve paths relative to the script's location so this works regardless
# of where the user runs it from.
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$IssFile   = Join-Path $ScriptDir "QRSnapper.iss"
$OutputDir = Join-Path $ScriptDir "Output"

# ISCC.exe location -- assumed to be Inno Setup 6's default install.
$Iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $Iscc)) {
    Write-Error "ISCC.exe not found at '$Iscc'. Install Inno Setup 6 from https://jrsoftware.org/isdl.php (or update the path in this script)."
    exit 1
}

# --- Step 0: stop any running QRSnapper.exe ---
# A running instance holds the EXE open, and `dotnet publish` will then fail
# to overwrite it with the new build. Killing here is safe -- we're about to
# replace the binary anyway. The single-instance mutex releases cleanly on
# process exit so subsequent launches will pick up the new build.
$running = Get-Process -Name QRSnapper -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running QRSnapper (PID $($running.Id))..." -ForegroundColor Cyan
    Stop-Process -Id $running.Id -Force
    Start-Sleep -Milliseconds 500
}

# --- Step 1: publish ---
Write-Host "Publishing self-contained Release build..." -ForegroundColor Cyan
Push-Location $RepoRoot
try {
    dotnet publish src\QrSnip -c Release --self-contained true -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed (exit code $LASTEXITCODE)."
        exit 1
    }
}
finally {
    Pop-Location
}

# --- Step 1.5: prune unused localization files ---
# WindowsAppSDK (transitively from Win2D) ships ~100 locale folders, each
# containing Microsoft.UI.Xaml.dll.mui files. We don't display any localized
# UI strings from WinUI XAML so these are pure bloat -- ~30 MB compressed.
#
# <SatelliteResourceLanguages>en</...> in the csproj already strips the
# .NET satellite assemblies but doesn't touch the .mui files (those are
# unmanaged Windows resources, controlled separately). We delete them here.
#
# Keep:
#   - en-us / en-US (the fallback the OS uses when the user's locale has
#     no specific match)
#   - Microsoft.UI.Xaml (NOT a locale, despite the name -- this holds
#     actual WinUI runtime files we need)
#   - Two-letter language-only folders (cs, de, es, fr, ...) -- these are
#     .NET satellite folders that are now EMPTY thanks to the csproj
#     setting. Empty folders are harmless and removing them isn't worth
#     the script complexity.
Write-Host ""
Write-Host "Pruning unused localization files..." -ForegroundColor Cyan
$PublishDir = Join-Path $RepoRoot "src\QrSnip\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$pruned = 0
Get-ChildItem $PublishDir -Directory | Where-Object {
    # Five-or-more-character names with at least one dash = locale folder
    # (e.g. "af-ZA", "zh-Hant", "az-Latn-AZ"). Two-letter folders without
    # dashes (cs, de) are .NET satellite folders and stay.
    $_.Name -match '^[A-Za-z]{2,3}(-[A-Za-z0-9]+)+$' -and
    $_.Name -notmatch '^en-(us|US|GB|gb)$' -and
    $_.Name -ne 'Microsoft.UI.Xaml'
} | ForEach-Object {
    Remove-Item -Recurse -Force $_.FullName
    $pruned++
}
Write-Host "  pruned $pruned locale folders" -ForegroundColor DarkGray

# --- Step 2: compile the installer ---
Write-Host ""
Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $Iscc $IssFile
if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC failed (exit code $LASTEXITCODE)."
    exit 1
}

# --- Step 3: report ---
$SetupExe = Join-Path $OutputDir "QRSnapperSetup.exe"
if (Test-Path $SetupExe) {
    $SizeMB = [Math]::Round((Get-Item $SetupExe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Built: $SetupExe ($SizeMB MB)" -ForegroundColor Green
} else {
    Write-Error "Build appeared to succeed but $SetupExe is missing."
    exit 1
}
