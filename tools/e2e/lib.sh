# Shared by start.sh / watch.sh / fetch-report.sh. Sourced, not run.
# EKE2E_HOST: ssh alias of the gaming PC (default gaming-pc). The PC's ssh shell is Windows
# PowerShell 5.1: no && / & chaining; every PowerShell step lives in a .ps1 next to this file.
set -euo pipefail
EKE2E_HOST="${EKE2E_HOST:-gaming-pc}"
EKE2E_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EKE2E_REMOTE_TOOLS="eke2e-tools"      # relative to the PC user's home
EKE2E_CONFIG="${EKE2E_CONFIG:-$HOME/.config/echokraut-e2e.json}"   # { "remoteUrl": "https://..." }, in no repo

eke2e_upload_tools() {
  ssh -o ConnectTimeout=10 "$EKE2E_HOST" "New-Item -ItemType Directory -Force -Path $EKE2E_REMOTE_TOOLS | Out-Null"
  scp -q "$EKE2E_DIR"/*.ps1 "$EKE2E_HOST:$EKE2E_REMOTE_TOOLS/"
}

# Runs one uploaded script: eke2e_ps <script.ps1> [args...]
eke2e_ps() {
  local script="$1"; shift
  ssh -o ConnectTimeout=10 "$EKE2E_HOST" "powershell -NoProfile -ExecutionPolicy Bypass -File $EKE2E_REMOTE_TOOLS\\$script $*"
}
