#!/bin/bash
# ═══════════════════════════════════════════════════════════════════
# reset-db.sh — Reset the local SQLite database to a clean seeded state
# ═══════════════════════════════════════════════════════════════════
# Usage:
#   ./reset-db.sh           # delete + rebuild + seed (stops app first)
#   ./reset-db.sh --keep    # rebuild without deleting (run migrations only)
#   ./reset-db.sh --check   # verify DB integrity without changing anything
#
# What it does:
#   1. Stops any running dotnet process (frees the DB file lock)
#   2. Deletes mergesafe.db + WAL + SHM files
#   3. Runs dotnet build to verify compilation
#   4. Starts the app briefly to trigger MigrateAsync + DbInitializer.SeedAsync
#   5. Stops the app after DB is seeded
#   5. Verifies tables exist and row counts
# ═══════════════════════════════════════════════════════════════════

set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

DB_FILE="mergesafe.db"
DB_WAL="mergesafe.db-wal"
DB_SHM="mergesafe.db-shm"

# ── Colors ───────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

info()  { echo -e "${CYAN}[INFO]${NC}  $1"; }
ok()    { echo -e "${GREEN}[OK]${NC}    $1"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $1"; }
fail()  { echo -e "${RED}[FAIL]${NC}  $1"; exit 1; }

# ── --check mode: verify DB integrity only ─────────────────────────
if [ "$1" = "--check" ]; then
    info "Checking database integrity..."
    if [ ! -f "$DB_FILE" ]; then
        warn "Database file '$DB_FILE' does not exist."
        exit 0
    fi
    dotnet ef database update --no-build 2>/dev/null || true
    sqlite3 "$DB_FILE" "PRAGMA integrity_check;" || true
    info "Table counts:"
    for t in Highways MergeZones SwitchServers SensorDevices VehicleEvents UserProfiles; do
        COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM $t;" 2>/dev/null || echo "MISSING")
        echo "  $t: $COUNT rows"
    done
    ok "Check complete."
    exit 0
fi

# ── --keep mode: run migrations without deleting the DB ─────────────
if [ "$1" = "--keep" ]; then
    info "Running EF migrations (keeping existing DB)..."
    dotnet build --nologo -v q || fail "Build failed"
    dotnet ef database update || fail "Migration failed"
    ok "Migrations applied."
    exit 0
fi

# ── Full reset ──────────────────────────────────────────────────────
info "=== Database Reset ==="

# 1. Stop any running dotnet process
info "Stopping running dotnet processes..."
pkill -f "dotnet.*AirwaysMergeSafe" 2>/dev/null && warn "Stopped running app." || info "No running app found."
sleep 1

# 2. Untrack DB from git if still tracked
if git ls-files --error-unmatch "$DB_FILE" 2>/dev/null; then
    info "Removing $DB_FILE from git tracking..."
    git rm --cached "$DB_FILE" 2>/dev/null || true
    git rm --cached "$DB_WAL" 2>/dev/null || true
    git rm --cached "$DB_SHM" 2>/dev/null || true
    warn "DB file was tracked by git — now untracked. Commit this change."
fi

# 3. Delete the database files
info "Deleting database files..."
rm -f "$DB_FILE" "$DB_WAL" "$DB_SHM"
ok "Database files deleted."

# 4. Build
info "Building project..."
dotnet build --nologo -v q || fail "Build failed"
ok "Build successful."

# 5. Run the app briefly to trigger MigrateAsync + SeedAsync
info "Starting app to seed database..."
# Run in background, wait for seeding to complete, then stop
dotnet run --no-build &
APP_PID=$!

# Wait for the app to start and seed (check for the ready log message)
MAX_WAIT=30
for i in $(seq 1 $MAX_WAIT); do
    sleep 1
    # Check if the app logged the seed-complete message
    if jobs -l 2>/dev/null | grep -q "Done"; then
        fail "App exited unexpectedly."
    fi
    # Check if the DB file exists and has tables
    if [ -f "$DB_FILE" ]; then
        TABLES=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM sqlite_master WHERE type='table';" 2>/dev/null || echo "0")
        if [ "$TABLES" -gt 5 ]; then
            ok "Database seeded with $TABLES tables."
            break
        fi
    fi
    if [ $i -eq $MAX_WAIT ]; then
        warn "Timeout waiting for seed — stopping app and checking..."
    fi
done

# 6. Stop the app
info "Stopping app..."
kill $APP_PID 2>/dev/null || true
sleep 1
kill -9 $APP_PID 2>/dev/null || true
ok "App stopped."

# 7. Verify the database
info "Verifying database..."
if [ ! -f "$DB_FILE" ]; then
    fail "Database file not created — check app logs."
fi

TABLES=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM sqlite_master WHERE type='table';" 2>/dev/null || echo "0")
info "Tables created: $TABLES"

echo ""
info "Row counts:"
for t in Highways MergeZones SwitchServers SensorDevices VehicleEvents UserProfiles SimulationStatuses AuditLogs; do
    COUNT=$(sqlite3 "$DB_FILE" "SELECT COUNT(*) FROM $t;" 2>/dev/null || echo "MISSING")
    printf "  %-25s %s\n" "$t" "$COUNT"
done

INTEGRITY=$(sqlite3 "$DB_FILE" "PRAGMA integrity_check;" 2>/dev/null || echo "error")
if [ "$INTEGRITY" = "ok" ]; then
    ok "Database integrity: OK"
else
    warn "Database integrity: $INTEGRITY"
fi

echo ""
ok "=== Database reset complete ==="
echo "  Run 'dotnet run' to start the app with a fresh database."
echo "  Run './reset-db.sh --check' to verify anytime."
echo "  Run './reset-db.sh --keep' to apply migrations without data loss."
