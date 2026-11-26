#!/usr/bin/env bash
# Restores the real Echokraut config on the gaming PC after an E2E run.
# Usage: tools/e2e/restore.sh [--live | --install-shortcut]
#   (none)              runs restore-latest.ps1 on the PC: the game, the launcher and Dalamud must be closed.
#   --live              game keeps running: submits a request with only the opt-in stage "restore" (the plugin
#                       checks the backup, stops the E2E engine and reloads itself onto the real config), then
#                       follows it until "done". If it fails, use the plain form (game closed).
#   --install-shortcut  only (re)creates the Start-menu entry "Echokraut E2E Restore".
source "$(dirname "$0")/lib.sh"

case "${1:-}" in
  --install-shortcut)
    eke2e_upload_tools
    eke2e_ps install-restore-shortcut.ps1
    ;;
  --live)
    id="restore-$(date +%Y%m%d-%H%M%S)"
    eke2e_upload_tools
    tmp="$(mktemp)"
    trap 'rm -f "$tmp"' EXIT
    # installRoot is required by the request format; the restore stage does not use it.
    python3 - "$tmp" "$id" <<'PY'
import json, sys
path, id_ = sys.argv[1:]
json.dump({"id": id_, "installRoot": "E:\\EkE2E", "stages": ["restore"]}, open(path, "w"), indent=2)
PY
    scp -q "$tmp" "$EKE2E_HOST:$EKE2E_REMOTE_TOOLS/request-$id.json"
    eke2e_ps submit-request.ps1 -Uploaded "$EKE2E_REMOTE_TOOLS\\request-$id.json"
    eke2e_ps tail-ektest.ps1 -Id "$id" -TimeoutMinutes 5
    ;;
  "")
    eke2e_upload_tools
    eke2e_ps restore-latest.ps1 -NoPause
    ;;
  *) echo "unknown option $1" >&2; exit 64 ;;
esac
