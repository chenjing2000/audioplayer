param(
    [ValidateSet('Core', 'UI', 'Full')][string]$Suite = 'Core',
    [string]$AudioDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test.Common.ps1')
$workspace = Split-Path -Parent $PSScriptRoot
$mediaCopy = $null
$previousAudio = $env:AUDIOPLAYER_TEST_AUDIO_DIRECTORY
Push-Location $workspace
try {
    Invoke-TestCheck 'English-only PowerShell source and syntax' { Test-PowerShellSource (Split-Path -Parent $PSScriptRoot) }
    if ($AudioDirectory) {
        $sample = Get-ChildItem -LiteralPath $AudioDirectory -Recurse -File -Filter '*.mp3' | Sort-Object FullName | Select-Object -First 1
        if (-not $sample) { throw 'The supplied audio directory contains no MP3 files.' }
        $mediaCopy = New-TestDirectory
        Copy-Item -LiteralPath $sample.FullName -Destination (Join-Path $mediaCopy 'sample.mp3')
        $env:AUDIOPLAYER_TEST_AUDIO_DIRECTORY = $mediaCopy
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
    Remove-TestDirectory $mediaCopy
    Pop-Location
}
