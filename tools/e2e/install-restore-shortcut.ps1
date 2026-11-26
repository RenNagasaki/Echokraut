# Puts "Echokraut E2E Restore" into the user's Start menu. It runs restore-latest.ps1 from this folder.
# Run once over ssh (restore.sh --install-shortcut does it). Windows PowerShell 5.1, ASCII only.
$script = Join-Path $PSScriptRoot 'restore-latest.ps1'
if (-not (Test-Path -LiteralPath $script)) { Write-Host "missing $script"; exit 1 }
$programs = [Environment]::GetFolderPath('Programs')
$link = Join-Path $programs 'Echokraut E2E Restore.lnk'
$shell = New-Object -ComObject WScript.Shell
$s = $shell.CreateShortcut($link)
$s.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$s.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
$s.WorkingDirectory = $PSScriptRoot
$s.Description = 'Restore the real Echokraut config after an E2E run (close the game first)'
$s.Save()
Write-Host "OK   shortcut $link"
