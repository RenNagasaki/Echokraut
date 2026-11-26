using System.Text;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// <c>restore.ps1</c>, written into the backup folder by phase B before the wipe (plan › Report and
/// restore). The user runs it by hand with the game closed. Windows PowerShell 5.1, ASCII only, CRLF.
/// It finds every path from its own location (<c>pluginConfigs\Echokraut-e2e\backup-…\</c>), so no
/// user-profile path (possibly non-ASCII) is baked in.
/// </summary>
public static class E2eRestoreScript
{
    public static string Build(string runId)
    {
        var lines = new[]
        {
            $"# Echokraut E2E restore for run {Ascii(runId)} - written by the plugin before the reset.",
            "# Run it by hand with the game and the launcher closed:",
            "#   powershell -NoProfile -ExecutionPolicy Bypass -File restore.ps1",
            "# Puts pluginConfigs\\Echokraut.json and pluginConfigs\\Echokraut\\ back from this folder",
            "# (selftest\\ is kept) and deletes pluginConfigs\\Echokraut-e2e\\state.json. The backup stays.",
            "$ErrorActionPreference = 'Stop'",
            "$backup = $PSScriptRoot",
            "$e2eDir = Split-Path -Parent $backup",
            "$configs = Split-Path -Parent $e2eDir",
            "foreach ($name in @('ffxiv_dx11', 'XIVLauncher', 'Dalamud.Injector')) {",
            "    if (Get-Process -Name $name -ErrorAction SilentlyContinue) {",
            "        Write-Host \"$name is running - close the game and the launcher first.\"",
            "        exit 1",
            "    }",
            "}",
            "$manifest = Get-Content -LiteralPath (Join-Path $backup 'manifest.json') -Raw | ConvertFrom-Json",
            "$files = @($manifest.files)",
            "foreach ($f in $files) {",
            "    $src = Join-Path $backup $f.path",
            "    if (-not (Test-Path -LiteralPath $src)) { Write-Host \"Backup file missing: $($f.path)\"; exit 2 }",
            "    if ((Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash -ne $f.sha256) { Write-Host \"Backup file changed: $($f.path)\"; exit 2 }",
            "}",
            "$configFile = Join-Path $configs 'Echokraut.json'",
            "$configDir = Join-Path $configs 'Echokraut'",
            "if (Test-Path -LiteralPath $configFile) { Remove-Item -LiteralPath $configFile -Force }",
            "if (Test-Path -LiteralPath $configDir) {",
            "    Get-ChildItem -LiteralPath $configDir -Force | Where-Object { $_.Name -ne 'selftest' } | Remove-Item -Recurse -Force",
            "} else {",
            "    New-Item -ItemType Directory -Path $configDir | Out-Null",
            "}",
            "foreach ($f in $files) {",
            "    $dst = Join-Path $configs $f.path",
            "    $dir = Split-Path -Parent $dst",
            "    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }",
            "    Copy-Item -LiteralPath (Join-Path $backup $f.path) -Destination $dst -Force",
            "    if ((Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash -ne $f.sha256) { Write-Host \"Restored file differs: $($f.path)\"; exit 3 }",
            "}",
            "$state = Join-Path $e2eDir 'state.json'",
            "if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state -Force }",
            "Write-Host \"Restored $($files.Count) files from $backup - every SHA-256 matches.\"",
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    // The run id is already restricted to [A-Za-z0-9_-]; this only guards the comment line.
    private static string Ascii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c is >= ' ' and <= '~' ? c : '?');
        return sb.ToString();
    }
}
