#!/usr/bin/env bash
# Starts an in-game E2E run (plan docs/plans/ingame-e2e.md): PC-side preconditions, then the request.
# Usage: tools/e2e/start.sh [--id ID] [--stages a,b] [--engines x,y] [--root 'E:\EkE2E'] [--no-remote] [--restore] [--test-wrapper] [--wrapper-version TAG]
# --restore appends the opt-in stage "restore": the run ends on the real config again (needs --stages).
# --test-wrapper: first-time installs the zip pushed by push-test-wrapper.sh instead of the release.
# --wrapper-version TAG: first-time installs that published wrapper release (mixed state, stage wrapper-mixed).
# Prints the run id on the last line; follow it with tools/e2e/watch.sh <id>.
source "$(dirname "$0")/lib.sh"

id="$(date +%Y%m%d-%H%M%S)"; stages=""; engines=""; root='E:\EkE2E'; use_remote=1; restore=0; test_wrapper=0; wrapper_version=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --id) id="$2"; shift 2 ;;
    --stages) stages="$2"; shift 2 ;;
    --engines) engines="$2"; shift 2 ;;
    --root) root="$2"; shift 2 ;;
    --no-remote) use_remote=0; shift ;;
    --restore) restore=1; shift ;;
    --test-wrapper) test_wrapper=1; shift ;;
    --wrapper-version) wrapper_version="$2"; shift 2 ;;
    *) echo "unknown option $1" >&2; exit 64 ;;
  esac
done

if [[ $restore == 1 ]]; then
  [[ -n "$stages" ]] || { echo "--restore needs --stages (a run without a stage list leaves the opt-in restore out)" >&2; exit 64; }
  stages="$stages,restore"
fi

remote=""
if [[ $use_remote == 1 ]]; then
  [[ -f "$EKE2E_CONFIG" ]] || { echo "missing $EKE2E_CONFIG ({\"remoteUrl\": \"https://...\"}); or pass --no-remote" >&2; exit 65; }
  remote="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("remoteUrl",""))' "$EKE2E_CONFIG")"
fi

eke2e_upload_tools
if [[ -n "$remote" ]]; then
  eke2e_ps preconditions.ps1 -RemoteUrl "'$remote'" || { echo "preconditions failed - no request sent" >&2; exit 1; }
else
  eke2e_ps preconditions.ps1 || { echo "preconditions failed - no request sent" >&2; exit 1; }
fi

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
python3 - "$tmp" "$id" "$root" "$remote" "$stages" "$engines" "$test_wrapper" "$wrapper_version" <<'PY'
import json, sys
path, id_, root, remote, stages, engines, test_wrapper, wrapper_version = sys.argv[1:]
req = {"id": id_, "installRoot": root}
if remote: req["remoteUrl"] = remote
if stages: req["stages"] = [s for s in stages.split(",") if s]
if engines: req["engines"] = [e for e in engines.split(",") if e]
if test_wrapper == "1": req["testWrapper"] = True
if wrapper_version: req["wrapperVersion"] = wrapper_version
json.dump(req, open(path, "w"), indent=2)
PY
scp -q "$tmp" "$EKE2E_HOST:$EKE2E_REMOTE_TOOLS/request-$id.json"
eke2e_ps submit-request.ps1 -Uploaded "$EKE2E_REMOTE_TOOLS\\request-$id.json"
echo "$id"
