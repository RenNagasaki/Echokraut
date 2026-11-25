# Moves an uploaded request into the E2E folder in one rename, so the plugin (which polls every 2 s)
# never reads a half-written file. Windows PowerShell 5.1, ASCII only.
param([Parameter(Mandatory = $true)][string]$Uploaded)
$ErrorActionPreference = "Stop"
$dir = Join-Path $env:APPDATA "XIVLauncher\pluginConfigs\Echokraut-e2e"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$staged = Join-Path $dir "request.json.upload"
Copy-Item -Force $Uploaded $staged
Move-Item -Force $staged (Join-Path $dir "request.json")
Remove-Item -Force $Uploaded
Write-Output "submitted $(Join-Path $dir 'request.json')"
