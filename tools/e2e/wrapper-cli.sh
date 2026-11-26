#!/usr/bin/env bash
# B10 (plan docs/plans/ingame-e2e.md): wrapper bootstrap cases on the gaming PC that windows-test.sh cannot
# express (it has no --language and no free bootstrap args), judged Mac-side from the NDJSON. No game needed.
# Same install path as ../echokrautts/tools/windows-test.sh: working tree copied over, bootstrap\install_win.ps1.
#
#   tools/e2e/wrapper-cli.sh [--fresh] [case ...]      default: qwen-cpu pocket-ja pocket6-fr
#     qwen-cpu    --tts-backend qwen-0.6b --gpu-backend cpu   (R4: stops before any download)
#     pocket-ja   --tts-backend pocket --language ja          (R2: clear message, no model download)
#     pocket6-fr  --tts-backend pocket --pocket-layers 6 --language fr  (R1: pocket.layers_fallback, /tts fr)
#     --fresh     delete the test folder first (else the install is reused between runs)
#
# Env: EKE2E_HOST (lib.sh), EKE2E_WRAPPER_ROOT (test folder on the PC, default E:\EkE2E-wrapper-cli;
# never the E2E install root), EKE2E_WRAPPER_REPO (default ../echokrautts). Output: .claude/e2e-reports/wrapper-cli-<ts>/.
source "$(dirname "$0")/lib.sh"

root="${EKE2E_WRAPPER_ROOT:-E:\\EkE2E-wrapper-cli}"
repo="$(cd "${EKE2E_WRAPPER_REPO:-$EKE2E_DIR/../../../echokrautts}" && pwd)"
fresh=0; cases=()
for a in "$@"; do
  case "$a" in
    --fresh) fresh=1 ;;
    qwen-cpu|pocket-ja|pocket6-fr) cases+=("$a") ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "unknown argument $a" >&2; exit 64 ;;
  esac
done
[[ ${#cases[@]} -gt 0 ]] || cases=(qwen-cpu pocket-ja pocket6-fr)

boot_args() {
  case "$1" in
    qwen-cpu)   echo "--tts-backend qwen-0.6b --gpu-backend cpu --language de" ;;
    pocket-ja)  echo "--tts-backend pocket --language ja" ;;
    pocket6-fr) echo "--tts-backend pocket --pocket-layers 6 --language fr" ;;
  esac
}

ssh -o ConnectTimeout=10 -o BatchMode=yes "$EKE2E_HOST" exit || { echo "$EKE2E_HOST not reachable" >&2; exit 1; }
eke2e_upload_tools

echo "==> test folder $root (fresh=$fresh)"
ssh -o BatchMode=yes "$EKE2E_HOST" "\$d = '$root'
Get-CimInstance Win32_Process | Where-Object { \$_.ExecutablePath -and \$_.ExecutablePath -like (\$d + '*') } |
    ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }
if ($fresh -eq 1 -and (Test-Path \$d)) { cmd /c rd /s /q \$d }
New-Item -ItemType Directory -Force \$d | Out-Null
\$o = Join-Path \$d 'out'
if (Test-Path \$o) { Remove-Item \$o -Recurse -Force }
exit 0"

echo "==> copying the working tree of $(basename "$repo") ($(git -C "$repo" rev-parse --short HEAD))"
(cd "$repo" && git ls-files -co --exclude-standard -z | COPYFILE_DISABLE=1 xargs -0 tar -cf -) |
  ssh -o BatchMode=yes "$EKE2E_HOST" "tar -xf - -C '$root'"

for c in "${cases[@]}"; do
  extra=""
  [[ $c == pocket6-fr ]] && extra="-TtsLanguage fr -TtsText 'Bonjour, ceci est un court test de la synthese vocale.'"
  eke2e_ps wrapper-cli.ps1 -Root "'$root'" -Case "$c" -BootArgs "'$(boot_args "$c")'" $extra || echo "==> $c: PC script exited $?"
done

out="$EKE2E_DIR/../../.claude/e2e-reports/wrapper-cli-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$(dirname "$out")"
scp -q -r -o BatchMode=yes "$EKE2E_HOST:${root//\\//}/out" "$out" || echo "warning: output not fetched" >&2
echo; echo "==> judging ($out)"
python3 -I "$EKE2E_DIR/wrapper_cli_judge.py" "$out" "$repo" "${cases[@]}"
