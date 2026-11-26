# B10, gaming-PC side (plan docs/plans/ingame-e2e.md): one wrapper bootstrap case through the real install
# path (bootstrap\install_win.ps1, as ../echokrautts/tools/windows-test.ps1 starts it), with bootstrap args
# that windows-test.sh cannot pass (--language, --gpu-backend). Uploaded by wrapper-cli.sh, never inlined.
# Windows PowerShell 5.1: no ?? / ternary, ASCII only. Judges nothing: writes <Case>.ndjson, <Case>.err.log
# and <Case>.result.json into <Root>\out; wrapper-cli.sh fetches and judges them.
param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Case,
    [Parameter(Mandatory = $true)][string]$BootArgs,
    [string]$TtsLanguage = "",
    [string]$TtsText = "",
    [int]$Port = 8799,
    [int]$ReadyMinutes = 60
)
$ErrorActionPreference = "Stop"
$wrapper = Join-Path $Root "wrapper"
$out = Join-Path $Root "out"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$base = "http://127.0.0.1:$Port"

Add-Type -Namespace EkE2e -Name Power -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
'@
[EkE2e.Power]::SetThreadExecutionState([uint32]"0x80000001") | Out-Null

# Same reason as windows-test.ps1: uv's managed Python must sit in a folder this ssh process created
# (junction trust, os error 448). The wheel cache lives here too, so the run never fills C:.
$env:UV_PYTHON_INSTALL_DIR = Join-Path $Root ".cache\python"
$env:UV_PYTHON_BIN_DIR = Join-Path $Root ".cache\bin"
$env:UV_PYTHON_PREFERENCE = "only-managed"
$env:UV_CACHE_DIR = Join-Path $Root ".cache\uv"

function Snapshot([string]$dir) {
    $files = @{}
    if (Test-Path $dir) {
        Get-ChildItem -LiteralPath $dir -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $files[$_.FullName.Substring($dir.Length).TrimStart('\')] = $_.Length
        }
    }
    return $files
}
function FolderBytes([string]$dir) {
    if (-not (Test-Path $dir)) { return -1 }
    $sum = (Get-ChildItem -LiteralPath $dir -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return [int64]$sum
}
function Venvs() {
    $r = @{}
    Get-ChildItem -LiteralPath $wrapper -Directory -Force -Filter ".venv*" -ErrorAction SilentlyContinue | ForEach-Object {
        $r[$_.Name] = FolderBytes $_.FullName
    }
    return $r
}

$busy = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($busy) { Write-Output "FAIL port $Port already in use"; exit 2 }

$modelsDir = Join-Path $wrapper "models"
$modelsBefore = Snapshot $modelsDir
$venvsBefore = Venvs
$cacheBefore = FolderBytes $env:UV_CACHE_DIR

$ndjson = Join-Path $out "$Case.ndjson"
$errLog = Join-Path $out "$Case.err.log"
$installArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "bootstrap\install_win.ps1",
                 "--start", "--host", "127.0.0.1", "--port", "$Port") + @($BootArgs -split ' ' | Where-Object { $_ })
Write-Output "==> $Case : install_win.ps1 $($installArgs[5..($installArgs.Count - 1)] -join ' ')"
$sw = [Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath "powershell.exe" -PassThru -NoNewWindow -WorkingDirectory $wrapper `
    -RedirectStandardOutput $ndjson -RedirectStandardError $errLog -ArgumentList $installArgs
$null = $proc.Handle

$result = [ordered]@{ case = $Case; bootArgs = $BootArgs; ready = $false; exitCode = $null; health = $null; tts = $null }
$deadline = (Get-Date).AddMinutes($ReadyMinutes)
$lastLine = ""
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) { break }
    try { $result.health = Invoke-RestMethod -Uri "$base/health" -TimeoutSec 5; $result.ready = $true; break } catch { }
    $line = Get-Content $ndjson -Tail 1 -ErrorAction SilentlyContinue
    if ($line -and $line -ne $lastLine) { Write-Output "   $line"; $lastLine = $line }
    Start-Sleep -Seconds 10
}
$result.secondsToEnd = [math]::Round($sw.Elapsed.TotalSeconds, 1)

if ($result.ready -and $TtsLanguage) {
    $tts = [ordered]@{ language = $TtsLanguage; ok = $false }
    try {
        $samples = (Invoke-RestMethod -Uri "$base/samples" -TimeoutSec 30).samples
        if (-not $samples -or $samples.Count -eq 0) {
            # A native install ships no voices (the plugin installs them), same step as windows-test.ps1.
            Push-Location $wrapper
            try { & (Join-Path $wrapper ".venv\Scripts\python.exe") -m src.voicepack 2>&1 | Out-Null } finally { Pop-Location }
            $samples = (Invoke-RestMethod -Uri "$base/samples" -TimeoutSec 30).samples
        }
        $tts.sample = [System.IO.Path]::GetFileNameWithoutExtension([string]$samples[0])
        $body = @{ sample = $tts.sample; text = $TtsText; language = $TtsLanguage; format = "wav" } | ConvertTo-Json
        $wav = Join-Path $out "$Case.wav"
        $t = [Diagnostics.Stopwatch]::StartNew()
        Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$base/tts" -ContentType "application/json; charset=utf-8" `
            -Body ([Text.Encoding]::UTF8.GetBytes($body)) -OutFile $wav -TimeoutSec 900
        $tts.seconds = [math]::Round($t.Elapsed.TotalSeconds, 1)
        $bytes = [IO.File]::ReadAllBytes($wav)
        if ($bytes.Length -ge 44 -and [Text.Encoding]::ASCII.GetString($bytes, 0, 4) -eq "RIFF") {
            $rate = [BitConverter]::ToInt32($bytes, 24)
            $tts.audioSeconds = [math]::Round(($bytes.Length - 44) / (2.0 * $rate), 2)
            $tts.ok = $true
        }
    } catch { $tts.error = "$_" }
    $result.tts = $tts
}

if (-not $proc.HasExited) {
    try { Invoke-RestMethod -Method Post -Uri "$base/shutdown" -TimeoutSec 10 | Out-Null } catch { }
    if (-not $proc.WaitForExit(30000)) { & taskkill /PID $proc.Id /T /F | Out-Null }
}
$proc.WaitForExit() | Out-Null
$result.exitCode = $proc.ExitCode

$modelsAfter = Snapshot $modelsDir
$result.newModelFiles = @($modelsAfter.Keys | Where-Object { -not $modelsBefore.ContainsKey($_) -or $modelsBefore[$_] -ne $modelsAfter[$_] } | Sort-Object)
$result.venvsBefore = $venvsBefore
$result.venvsAfter = Venvs
$result.uvCacheBytesBefore = $cacheBefore
$result.uvCacheBytesAfter = FolderBytes $env:UV_CACHE_DIR
$result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $out "$Case.result.json")
Write-Output "==> $Case : ready=$($result.ready) exit=$($result.exitCode) in $($result.secondsToEnd) s"
exit 0
