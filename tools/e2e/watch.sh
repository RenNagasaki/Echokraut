#!/usr/bin/env bash
# Follows one E2E run: prints every "EKTEST <id> ..." line from dalamud.log, ends after "... done".
# Usage: tools/e2e/watch.sh <id> [timeoutMinutes]   (one line per event - suits the Monitor tool)
source "$(dirname "$0")/lib.sh"
id="${1:?usage: watch.sh <id> [timeoutMinutes]}"
eke2e_upload_tools
eke2e_ps tail-ektest.ps1 -Id "$id" -TimeoutMinutes "${2:-600}"
