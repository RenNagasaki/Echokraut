#!/usr/bin/env bash
# Copies e2e-<id>.json from the PC to .claude/e2e-reports/ (local, gitignored) and prints a summary.
# Usage: tools/e2e/fetch-report.sh <id>
source "$(dirname "$0")/lib.sh"
id="${1:?usage: fetch-report.sh <id>}"
out_dir="$EKE2E_DIR/../../.claude/e2e-reports"
mkdir -p "$out_dir"
out="$out_dir/e2e-$id.json"
ssh -o ConnectTimeout=10 "$EKE2E_HOST" "Get-Content -Raw -Encoding UTF8 (Join-Path \$env:APPDATA 'XIVLauncher\\pluginConfigs\\Echokraut-e2e\\e2e-$id.json')" > "$out"
python3 - "$out" <<'PY'
import json, sys
r = json.load(open(sys.argv[1], encoding="utf-8-sig"))
print(f"{r['Id']}  {r['Phase']}  plugin {r['PluginVersion']}  {r['Language']}  {r['StartedUtc']} -> {r['FinishedUtc']}")
for s in r["Stages"]:
    print(f"  {s['Outcome']:<8} {s['Id']:<16} {s['DurationMs']:>8} ms  {s['Detail']}")
print(sys.argv[1])
PY
