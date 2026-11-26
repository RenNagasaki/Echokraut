# E2E run, stage 0, gaming-PC side (plan docs/plans/ingame-e2e.md). Uploaded by start.sh via scp,
# never inlined. Windows PowerShell 5.1: no ?? / ternary, ASCII only.
# Prints one "OK ..." / "FAIL ..." line per check; exit 1 when any check fails.
param(
    [string]$RemoteUrl = "",
    [int]$MinFreeEGb = 60
)
$ErrorActionPreference = "Stop"
$failed = $false
function Report([bool]$ok, [string]$text) {
    if ($ok) { Write-Output "OK   $text" } else { Write-Output "FAIL $text"; $script:failed = $true }
}

$game = Get-Process ffxiv_dx11 -ErrorAction SilentlyContinue
Report ($null -ne $game) "game running (ffxiv_dx11)"

$e = Get-PSDrive E -ErrorAction SilentlyContinue
if ($null -eq $e) { Report $false "drive E: exists" }
else {
    $freeE = [math]::Round($e.Free / 1GB, 1)
    Report ($freeE -ge $MinFreeEGb) "E: free $freeE GB (need $MinFreeEGb)"
}

$cfg = Join-Path $env:APPDATA "XIVLauncher\pluginConfigs\Echokraut"
$dbBytes = 0
foreach ($f in @("echokraut.db", "echokraut.db-wal")) {
    $p = Join-Path $cfg $f
    if (Test-Path $p) { $dbBytes += (Get-Item $p).Length }
}
$freeC = (Get-PSDrive C).Free
Report ($freeC -ge 2 * $dbBytes) ("C: free {0} GB >= 2 x DB {1} MB" -f [math]::Round($freeC / 1GB, 1), [math]::Round($dbBytes / 1MB, 1))

$request = Join-Path $env:APPDATA "XIVLauncher\pluginConfigs\Echokraut-e2e\request.json"
Report (-not (Test-Path $request)) "no request.json pending"

if ($RemoteUrl -ne "") {
    # The host name stays out of the output; only the outcome is printed.
    $ok = $false
    try {
        $r = Invoke-WebRequest -UseBasicParsing -TimeoutSec 15 -Uri ($RemoteUrl.TrimEnd("/") + "/health")
        $ok = ($r.StatusCode -eq 200)
    } catch { $ok = $false }
    Report $ok "remote EchokrauTTS answers /health from this PC"
}

if ($failed) { exit 1 } else { exit 0 }
