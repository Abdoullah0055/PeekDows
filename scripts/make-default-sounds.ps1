<#
.SYNOPSIS
    Generates the 3 bundled minimal/classy arrange sounds (16-bit mono WAV).

.DESCRIPTION
    Deterministic synthesis: sine tones with exponential-decay envelopes at
    44.1 kHz, peak-normalized to avoid clipping. Outputs are COMMITTED under
    src/PeekDows.App/Assets/Sounds so builds never need to run this script;
    re-run it only to retune the sounds.
      chime-soft.wav   — single soft 660 Hz blip, 0.35 s
      chime-double.wav — gentle 660 -> 880 Hz two-tone, 0.55 s
      pop-minimal.wav  — short 180 Hz thump, 0.15 s
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repoRoot (Join-Path 'src' (Join-Path 'PeekDows.App' (Join-Path 'Assets' 'Sounds')))
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

$sampleRate = 44100

function Write-Wav([string]$path, [System.Collections.Generic.List[double]]$samples) {
    # Peak-normalize to 0.89 to leave headroom (classy = never harsh).
    $peak = 0.0
    foreach ($s in $samples) {
        $a = [Math]::Abs($s)
        if ($a -gt $peak) { $peak = $a }
    }
    $gain = if ($peak -gt 0) { 0.89 / $peak } else { 1.0 }

    $fs = [System.IO.File]::Create($path)
    try {
        $bw = New-Object System.IO.BinaryWriter($fs)
        $dataLen = $samples.Count * 2
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $bw.Write([int](36 + $dataLen))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
        $bw.Write([int]16)
        $bw.Write([int16]1)   # PCM
        $bw.Write([int16]1)   # mono
        $bw.Write([int]$sampleRate)
        $bw.Write([int]($sampleRate * 2))
        $bw.Write([int16]2)
        $bw.Write([int16]16)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $bw.Write([int]$dataLen)
        foreach ($s in $samples) {
            $v = [int][Math]::Round($s * $gain * 32767.0)
            if ($v -gt 32767) { $v = 32767 }
            if ($v -lt -32768) { $v = -32768 }
            $bw.Write([int16]$v)
        }
        $bw.Flush()
    }
    finally {
        $fs.Close()
    }
    Write-Host "wrote $path ($($samples.Count) samples)"
}

function Add-Tone([System.Collections.Generic.List[double]]$acc, [double]$freq, [double]$seconds, [double]$decaySeconds) {
    $n = [int]($seconds * $sampleRate)
    for ($i = 0; $i -lt $n; $i++) {
        $t = $i / $sampleRate
        $env = [Math]::Exp(-$t / $decaySeconds)
        # 5 ms fade-in avoids clicks.
        $fade = [Math]::Min(1.0, $t / 0.005)
        $acc.Add([Math]::Sin(2.0 * [Math]::PI * $freq * $t) * $env * $fade)
    }
}

$acc = New-Object System.Collections.Generic.List[double]
Add-Tone $acc 660.0 0.35 0.12
Write-Wav (Join-Path $outDir 'chime-soft.wav') $acc

$acc = New-Object System.Collections.Generic.List[double]
Add-Tone $acc 660.0 0.28 0.10
Add-Tone $acc 880.0 0.30 0.12
Write-Wav (Join-Path $outDir 'chime-double.wav') $acc

$acc = New-Object System.Collections.Generic.List[double]
Add-Tone $acc 180.0 0.15 0.035
Write-Wav (Join-Path $outDir 'pop-minimal.wav') $acc

Write-Host 'Done.'
