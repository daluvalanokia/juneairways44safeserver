#!/bin/bash
# ═══════════════════════════════════════════════════════════════════
# untrack-non-dotnet.sh — Remove non-.NET files from git tracking
# ═══════════════════════════════════════════════════════════════════
# Run this ONCE from the repo root after pulling the updated .gitignore.
# It removes non-.NET files from git's index (without deleting them from
# disk) so they stop appearing in Visual Studio's Git Changes panel.
#
# After running, commit the changes:
#   git commit -m "chore: untrack non-.NET files from git"
# ═══════════════════════════════════════════════════════════════════

set -e
cd "$(git rev-parse --show-toplevel)"

echo "=== Untracking non-.NET files from git ==="

# ── Replit platform files ──
git rm --cached .replit 2>/dev/null || true
git rm --cached .replitignore 2>/dev/null || true
git rm --cached .npmrc 2>/dev/null || true
git rm --cached replit.md 2>/dev/null || true
git rm --cached replit.nix 2>/dev/null || true
git rm -r --cached .replit-artifact 2>/dev/null || true
git rm -r --cached artifacts/airways-mergesafe/.replit-artifact 2>/dev/null || true
git rm -r --cached artifacts/api-server/.replit-artifact 2>/dev/null || true
git rm -r --cached artifacts/mockup-sandbox/.replit-artifact 2>/dev/null || true

# ── Non-.NET subprojects ──
git rm -r --cached artifacts/api-server 2>/dev/null || true
git rm -r --cached artifacts/mockup-sandbox 2>/dev/null || true
git rm -r --cached lib 2>/dev/null || true
git rm -r --cached scripts 2>/dev/null || true

# ── Node/TS root config ──
git rm --cached package.json 2>/dev/null || true
git rm --cached tsconfig.json 2>/dev/null || true
git rm --cached tsconfig.base.json 2>/dev/null || true
git rm --cached pnpm-lock.yaml 2>/dev/null || true
git rm --cached pnpm-workspace.yaml 2>/dev/null || true

# ── Attached assets ──
git rm -r --cached attached_assets 2>/dev/null || true

# ── Generated PDF ──
git rm --cached "artifacts/airways-mergesafe/AirwaysMergeSafeServer/AirwaysMergeSafeServer_HLD.pdf" 2>/dev/null || true

# ── SQLite DB files (if any still tracked) ──
find . -name "*.db" -o -name "*.db-shm" -o -name "*.db-wal" | while read f; do
    git rm --cached "$f" 2>/dev/null || true
done

echo ""
echo "=== Untracked. Now commit: ==="
echo '  git commit -m "chore: untrack non-.NET files from git"'
echo ""
echo "The .gitignore will prevent these from reappearing in Git Changes."
