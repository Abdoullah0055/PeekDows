<#
.SYNOPSIS
    Generates the 3 bundled minimal/classy arrange sounds (16-bit mono WAV).

.DESCRIPTION
    Deterministic synthesis: sine tones with exponential-decay envelopes at
    44.1 kHz, peak-normalized to avoid clipping. Outputs are COMMITTED under
    src/PeekDows.App/Assets/Sounds so builds never need to run this script;
    re-run it only to retune the sounds.
      chime-soft.wav   — single soft 660 Hz blip, 0.35 s (reference, untouched)
      whoosh-wind.wav  — airy wind swell (filtered noise, 0.7 s, quiet)
      pop-minimal.wav  — soft low 220 Hz tick, 0.10 s, very quiet
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

function Write-Wav([string]$path, [System.Collections.Generic.List[double]]$samples, [double]$level = 0.89) {
    # Peak-normalize to $level to leave headroom (classy = never harsh).
    $peak = 0.0
    foreach ($s in $samples) {
        $a = [Math]::Abs($s)
        if ($a -gt $peak) { $peak = $a }
    }
    $gain = if ($peak -gt 0) { $level / $peak } else { 1.0 }

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

# Airy descending sigh (tonal, no harshness): a soft 380 -> 190 Hz glide over
# 0.55 s with a slow attack, breathing on a whisper of dark filtered noise.
# Deterministic seed so every run produces the exact same file.
$acc = New-Object System.Collections.Generic.List[double]
$rng = New-Object System.Random(20261010)
$sighSeconds = 0.55
$sighN = [int]($sighSeconds * $sampleRate)
$phase = 0.0
$y = 0.0
for ($i = 0; $i -lt $sighN; $i++) {
    $t = $i / $sampleRate
    $k = $t / $sighSeconds
    $freq = 380.0 + (190.0 - 380.0) * $k   # gentle downward glide
    $phase += 2.0 * [Math]::PI * $freq / $sampleRate
    $tone = [Math]::Sin($phase)
    $noise = $rng.NextDouble() * 2.0 - 1.0
    $y = $y + 0.03 * ($noise - $y)          # very dark air bed (barely there)
    $attack = [Math]::Min(1.0, $t / 0.15)   # slow 150 ms fade-in
    $release = [Math]::Pow(1.0 - $k, 1.5)   # smooth landing
    $acc.Add(($tone + 0.12 * $y) * $attack * $release)
}
Write-Wav (Join-Path $outDir 'whoosh-wind.wav') $acc 0.45

$acc = New-Object System.Collections.Generic.List[double]
Add-Tone $acc 220.0 0.10 0.025
Write-Wav (Join-Path $outDir 'pop-minimal.wav') $acc 0.55

Write-Host 'Done.'
