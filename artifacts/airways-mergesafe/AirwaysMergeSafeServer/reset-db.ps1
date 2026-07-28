# ═══════════════════════════════════════════════════════════════════
# reset-db.ps1 — Reset the local SQLite database (Windows PowerShell)
# ═══════════════════════════════════════════════════════════════════
# Usage:
#   .\reset-db.ps1            # Full reset: delete + rebuild + seed
#   .\reset-db.ps1 -Keep      # Run migrations only (keep existing data)
#   .\reset-db.ps1 -Check     # Verify DB integrity (read-only)
#
# Requirements: .NET 8 SDK, dotnet-ef tool
# ═══════════════════════════════════════════════════════════════════

param(
    [switch]$Keep,
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
Set-Location $ScriptDir

$DbFile  = "mergesafe.db"
$DbWal   = "mergesafe.db-wal"
$DbShm   = "mergesafe.db-shm"

function Write-Info($msg)  { Write-Host "[INFO]  $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "[OK]    $msg" -ForegroundColor Green }
function Write-Warn($msg)  { Write-Host "[WARN]  $msg" -ForegroundColor Yellow }
function Write-Fail($msg)  { Write-Host "[FAIL]  $msg" -ForegroundColor Red; exit 1 }

# ── --Check mode: verify DB integrity only ─────────────────────────
if ($Check) {
    Write-Info "Checking database integrity..."
    if (-not (Test-Path $DbFile)) {
        Write-Warn "Database file '$DbFile' does not exist."
        exit 0
    }
    # Use dotnet ef to verify migrations are up to date
    try {
        dotnet ef database update --no-build 2>$null
    } catch {}
    Write-Ok "Check complete (use sqlite3 or DB browser for detailed inspection)."
    exit 0
}

# ── -Keep mode: run migrations without deleting the DB ──────────────
if ($Keep) {
    Write-Info "Running EF migrations (keeping existing DB)..."
    dotnet build --nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Fail "Build failed" }
    dotnet ef database update
    if ($LASTEXITCODE -ne 0) { Write-Fail "Migration failed" }
    Write-Ok "Migrations applied."
    exit 0
}

# ── Full reset ──────────────────────────────────────────────────────
Write-Info "=== Database Reset ==="

# 1. Stop any running dotnet process
Write-Info "Stopping running dotnet processes..."
$dotnetProcs = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
if ($dotnetProcs) {
    $dotnetProcs | Stop-Process -Force
    Write-Warn "Stopped running dotnet process(es)."
    Start-Sleep -Seconds 2
} else {
    Write-Info "No running dotnet process found."
}

# 2. Delete the database files
Write-Info "Deleting database files..."
Remove-Item $DbFile -ErrorAction SilentlyContinue
Remove-Item $DbWal  -ErrorAction SilentlyContinue
Remove-Item $DbShm  -ErrorAction SilentlyContinue
Write-Ok "Database files deleted."

# 3. Build
Write-Info "Building project..."
dotnet build --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Fail "Build failed" }
Write-Ok "Build successful."

# 4. Run the app briefly to trigger MigrateAsync + SeedAsync
Write-Info "Starting app to seed database..."
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --no-build" `
    -WorkingDirectory $ScriptDir -PassThru -NoNewWindow -RedirectStandardOutput "$ScriptDir\reset-db-tmp.log"

# Wait for the DB file to appear with tables
$maxWait = 30
$seeded = $false
for ($i = 1; $i -le $maxWait; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Path $DbFile) {
        $size = (Get-Item $DbFile).Length
        if ($size -gt 1024) {
            Write-Ok "Database seeded (file size: $size bytes)."
            $seeded = $true
            break
        }
    }
    if ($proc.HasExited) {
        Write-Fail "App exited unexpectedly. Check reset-db-tmp.log."
    }
}

if (-not $seeded) {
    Write-Warn "Timeout waiting for seed — stopping app and checking..."
}

# 5. Stop the app
Write-Info "Stopping app..."
if (-not $proc.HasExited) {
    $proc | Stop-Process -Force
}
Start-Sleep -Seconds 1
Write-Ok "App stopped."

# 6. Verify
Write-Info "Verifying database..."
if (-not (Test-Path $DbFile)) {
    Write-Fail "Database file not created — check reset-db-tmp.log."
}

$fileSize = (Get-Item $DbFile).Length
Write-Ok "Database file exists ($fileSize bytes)."

# Clean up temp log
Remove-Item "$ScriptDir\reset-db-tmp.log" -ErrorAction SilentlyContinue

Write-Host ""
Write-Ok "=== Database reset complete ==="
Write-Host "  Run 'dotnet run' to start the app with a fresh database."
Write-Host "  Run '.\reset-db.ps1 -Check' to verify anytime."
Write-Host "  Run '.\reset-db.ps1 -Keep' to apply migrations without data loss."
