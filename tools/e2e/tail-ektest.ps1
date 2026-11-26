# Follows dalamud.log and prints every "EKTEST <id> ..." line; ends after "EKTEST <id> done".
# Re-opens the file when it shrinks (Dalamud rotates it to dalamud.old.log). Windows PowerShell 5.1.
param(
    [Parameter(Mandatory = $true)][string]$Id,
    [int]$TimeoutMinutes = 600
)
$ErrorActionPreference = "Stop"
$log = Join-Path $env:APPDATA "XIVLauncher\dalamud.log"
$needle = "EKTEST $Id "
$doneNeedle = "EKTEST $Id done "
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$pos = (Get-Item $log).Length   # only lines written from now on
$rest = ""
while ((Get-Date) -lt $deadline) {
    $len = (Get-Item $log).Length
    if ($len -lt $pos) { $pos = 0; $rest = "" }   # rotated
    if ($len -gt $pos) {
        $fs = [System.IO.File]::Open($log, "Open", "Read", "ReadWrite")
        try {
            $fs.Seek($pos, "Begin") | Out-Null
            $buf = New-Object byte[] ($len - $pos)
            $n = $fs.Read($buf, 0, $buf.Length)
            $pos += $n
            $text = $rest + [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
        } finally { $fs.Dispose() }
        $lines = $text -split "`n"
        $rest = $lines[-1]
        foreach ($line in $lines[0..($lines.Length - 2)]) {
            $i = $line.IndexOf($needle)
            if ($i -ge 0) {
                Write-Output $line.Substring($i).TrimEnd()
                if ($line.IndexOf($doneNeedle) -ge 0) { exit 0 }
            }
        }
    }
    Start-Sleep -Milliseconds 500
}
Write-Output "EKTEST $Id watch-timeout after $TimeoutMinutes min"
exit 2
