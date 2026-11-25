# Puts the real Echokraut config back after an E2E run (plan docs/plans/ingame-e2e.md > Report and restore).
# Finds the backup through state.json (only while the plugin runs on an E2E config) and runs that
# backup's own restore.ps1, which refuses while the game, the launcher or Dalamud run.
# Started by the Start-menu shortcut (waits for Enter at the end) or over ssh with -NoPause.
# Windows PowerShell 5.1, ASCII only.
param([switch]$NoPause)

function Finish([int]$code) {
    if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    exit $code
}

$e2eDir = Join-Path $env:APPDATA 'XIVLauncher\pluginConfigs\Echokraut-e2e'
$statePath = Join-Path $e2eDir 'state.json'
if (-not (Test-Path -LiteralPath $statePath)) {
    Write-Host 'Nothing to restore: no state.json - the plugin already runs on the real config.'
    Finish 0
}
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if (-not $state.ConfigIsE2e) {
    Write-Host "Nothing to restore: run $($state.Id) did not reset the config."
    Finish 0
}
$backup = $state.BackupDir
if (-not $backup -or -not (Test-Path -LiteralPath (Join-Path $backup 'restore.ps1'))) {
    Write-Host "Run $($state.Id) reset the config, but its backup is missing: $backup"
    Finish 2
}

Write-Host "Restoring the config from run $($state.Id): $backup"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $backup 'restore.ps1')
$code = $LASTEXITCODE
if ($code -eq 0) { Write-Host 'Done. Start the game: the plugin loads the real config again.' }
else { Write-Host "restore.ps1 stopped with exit code $code - nothing was changed after that point." }
Finish $code
