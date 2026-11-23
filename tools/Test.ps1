param(
    [ValidateSet('Core', 'UI', 'Full')][string]$Suite = 'Core',
    [string]$AudioDirectory,
    [string]$AudioFile
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test.Common.ps1')
$workspace = Split-Path -Parent $PSScriptRoot
$mediaCopy = $null
$previousAudio = $env:AUDIOPLAYER_TEST_AUDIO_DIRECTORY
$previousTrimFile = $env:AUDIOPLAYER_TEST_TRIM_FILE
Push-Location $workspace
try {
    Invoke-TestCheck 'English-only PowerShell source and syntax' { Test-PowerShellSource (Split-Path -Parent $PSScriptRoot) }
    if ($AudioDirectory) {
        if (-not (Test-Path -LiteralPath $AudioDirectory -PathType Container)) {
            throw '-AudioDirectory expects a folder containing MP3 files. For a single WAV, MP3 or M4A, use -AudioFile.'
        }
        $sample = Get-ChildItem -LiteralPath $AudioDirectory -Recurse -File -Filter '*.mp3' |
            Sort-Object FullName | Select-Object -First 1
        if (-not $sample) { throw 'The supplied audio directory contains no MP3 files.' }
        $mediaCopy = New-TestDirectory
        Copy-Item -LiteralPath $sample.FullName -Destination (Join-Path $mediaCopy 'sample.mp3')
        $env:AUDIOPLAYER_TEST_AUDIO_DIRECTORY = $mediaCopy
    }
    if ($AudioFile) {
        if (-not (Test-Path -LiteralPath $AudioFile -PathType Leaf)) {
            throw 'The supplied -AudioFile path is not an existing file.'
        }
        $extension = [IO.Path]::GetExtension($AudioFile).ToLowerInvariant()
        if ($extension -notin @('.wav', '.mp3', '.m4a')) {
            throw '-AudioFile requires a WAV, MP3 or M4A file.'
        }
        if (-not $mediaCopy) { $mediaCopy = New-TestDirectory }
        $trimInput = Join-Path $mediaCopy ('trim-input' + $extension)
        Copy-Item -LiteralPath $AudioFile -Destination $trimInput
        $env:AUDIOPLAYER_TEST_TRIM_FILE = $trimInput
    }
    Invoke-TestCheck 'Publishing safeguards' {
        Invoke-TestCommand 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/Test-Publish.ps1')
    }
    $arguments = @('run', '--project', 'tests/AudioPausePlayer.Tests', '-c', 'Release')
    if ($Suite -ne 'Core') { $arguments += @('--', '--ui') }
    if ($Suite -eq 'Full') {
        if ($AudioDirectory) { $arguments += '--playback' }
        else { Skip-TestCheck 'Real MP3 playback: supply -AudioDirectory to enable it.' }
    }
    Invoke-TestCheck "Application suite: $Suite" { Invoke-TestCommand 'dotnet' $arguments }
    Complete-TestRun
}
finally {
    $env:AUDIOPLAYER_TEST_AUDIO_DIRECTORY = $previousAudio
    $env:AUDIOPLAYER_TEST_TRIM_FILE = $previousTrimFile
    Remove-TestDirectory $mediaCopy
    Pop-Location
}
