#!/usr/bin/env bash
# One-shot bootstrap for the racecor-prodrive-windows private repo.
# Run from this directory: `bash setup-repo.sh`.
# Requires: `gh` CLI authenticated as alternatekev.

set -e
cd "$(dirname "$0")"

REPO_NAME="racecor-prodrive-windows"
REPO_OWNER="alternatekev"
DESCRIPTION="RaceCor Pro Drive — Windows apps (Electron shell + WinUI 3 native)"

if ! command -v gh >/dev/null; then echo "gh CLI not found"; exit 1; fi
if ! gh auth status >/dev/null 2>&1; then echo "gh not authenticated"; exit 1; fi

[ -d .git ] || git init -b main

git add -A
if git diff --cached --quiet; then
  echo "Nothing to commit."
else
  git commit -m "Initial scaffold: Electron shell + WinUI 3 native

Two Windows apps for RaceCor Pro Drive:
- apps/shell: Electron wrapper around prodrive.racecor.io with signed
  .exe / MSIX, Start menu icon, single-instance lock for OAuth deep-links,
  custom racecor-prodrive:// URL scheme handler
- apps/native: WinUI 3 + C# .NET 8, PKCE Discord auth via system browser +
  protocol handler, NavigationView shell, tokens in PasswordVault, first-
  screen dashboard hitting /api/v1/dashboard

Bundle IDs: racing.k10motorsports.prodrive.racecor.{winshell,win}

See README.md + each apps/*/next-steps.md."
fi

if gh repo view "$REPO_OWNER/$REPO_NAME" >/dev/null 2>&1; then
  echo "GitHub repo $REPO_OWNER/$REPO_NAME already exists."
else
  gh repo create "$REPO_OWNER/$REPO_NAME" --private --description "$DESCRIPTION" --source . --remote origin
fi

git push -u origin main
echo ""
echo "Done. https://github.com/$REPO_OWNER/$REPO_NAME"
echo "You can delete this script now: rm setup-repo.sh"
