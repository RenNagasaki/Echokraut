# Puts an uploaded test wrapper zip where a dev load of the plugin looks for it and prints its hash.
# Same rule as Echokraut/Helper/Functional/TestWrapperInstall.cs (ZipPathFor) - keep both in sync:
# the Echokraut.dll entry of DevPluginLoadLocations, five folders up, then Echokrautts\wrapper\EchokrauTTS.zip.
# Copy to a temp name, then one rename: the plugin probes the path every 5 s and must never see half a zip.
# Windows PowerShell 5.1, ASCII only.
param([Parameter(Mandatory = $true)][string]$Uploaded)
$ErrorActionPreference = "Stop"
$cfg = Get-Content -Raw (Join-Path $env:APPDATA "XIVLauncher\dalamudConfig.json") | ConvertFrom-Json
$locations = $cfg.DevPluginLoadLocations
if ($locations.PSObject.Properties.Name -contains '$values') { $locations = $locations.'$values' }
$dll = $locations | Where-Object { $_.IsEnabled -and $_.Path -like "*\Echokraut.dll" } | Select-Object -First 1 -ExpandProperty Path
if (-not $dll) { throw "no enabled Echokraut.dll in DevPluginLoadLocations" }
$dir = Split-Path -Parent $dll
for ($i = 0; $i -lt 5; $i++) { $dir = Split-Path -Parent $dir }
$wrapper = Join-Path $dir "Echokrautts\wrapper"
if (-not (Test-Path $wrapper)) { throw "wrapper folder $wrapper missing - is the Echokrautts checkout next to the Echokraut one?" }
$target = Join-Path $wrapper "EchokrauTTS.zip"
$staged = "$target.upload"
Copy-Item -Force $Uploaded $staged
Move-Item -Force $staged $target
Remove-Item -Force $Uploaded
$hash = (Get-FileHash -Algorithm SHA256 $target).Hash.ToLowerInvariant()
Write-Output "placed $target sha256 $hash"
