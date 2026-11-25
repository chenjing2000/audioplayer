param([string]$ExePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test.Common.ps1')
$workspace = Split-Path -Parent $PSScriptRoot
$exeName = 'AudioPlayer.exe'
$settingsName = 'Renamed App.settings.json'
if (-not $ExePath) { $ExePath = Join-Path $workspace "dist\$exeName" }
$ExePath = (Resolve-Path -LiteralPath $ExePath).Path
$temporary = New-TestDirectory
$script:publishedProcess = $null
Push-Location $workspace
try {
    Invoke-TestCheck 'English-only PowerShell source and syntax' { Test-PowerShellSource $workspace }
    $appDirectory = Join-Path $temporary 'app with spaces'
    $workingDirectory = Join-Path $temporary 'other working directory'
    New-Item -ItemType Directory -Path $appDirectory, $workingDirectory | Out-Null
    $exe = Join-Path $appDirectory 'Renamed App.exe'
    Copy-Item -LiteralPath $ExePath -Destination $exe
    $setup = Join-Path (Split-Path -Parent $ExePath) 'FFmpeg-Setup.txt'
    Invoke-TestCheck 'FFmpeg setup TXT shipped beside the EXE' {
        if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Missing FFmpeg-Setup.txt.' }
        Copy-Item -LiteralPath $setup -Destination (Join-Path $appDirectory 'FFmpeg-Setup.txt')
    }
    Invoke-TestCheck 'Isolated renamed EXE and configuration and playlist location' {
        $media = Join-Path $temporary 'media'
        New-Item -ItemType Directory -Path $media | Out-Null
        $playlist = Join-Path $appDirectory 'Renamed App.playlist.json'
        $missingAudio = Join-Path $media 'missing.mp3'
        $existingAudio = Join-Path $media 'existing.wav'
        [IO.File]::WriteAllText($existingAudio, 'metadata error fixture')
        @{ rootFolder = $media; files = @($missingAudio, $existingAudio) } |
            ConvertTo-Json | Set-Content -LiteralPath $playlist -Encoding UTF8
        $script:publishedProcess = Start-Process -FilePath $exe -WorkingDirectory $workingDirectory -WindowStyle Hidden -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $script:publishedProcess.Refresh()
        } while (-not $script:publishedProcess.HasExited -and $script:publishedProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
        if ($script:publishedProcess.HasExited -or $script:publishedProcess.MainWindowHandle -eq 0) { throw 'The application did not create its main window.' }
        $settings = Join-Path $appDirectory $settingsName
        while (-not (Test-Path -LiteralPath $settings) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json | Out-Null
        # Startup restores the playlist asynchronously and replaces the JSON.
        # A concurrent read can briefly fail while the file is being replaced.
        # Wait for the complete expected state, not just the first readable file.
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        $restored = $false
        $lastReadError = ''
        do {
            $script:publishedProcess.Refresh()
            if ($script:publishedProcess.HasExited) { throw 'The application exited during playlist restoration.' }
            try {
                $json = [IO.File]::ReadAllText($playlist)
                $lastReadError = ''
            }
            catch [IO.IOException] {
                $lastReadError = $_.Exception.Message
                Start-Sleep -Milliseconds 100
                continue
            }
            $saved = ConvertFrom-Json -InputObject $json
            if ($saved.rootFolder -eq $media -and $saved.files.Count -eq 1 -and $saved.files[0] -eq $existingAudio) {
                $restored = $true
                break
            }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        if (-not $restored) {
            throw "Renamed EXE did not restore and prune its own playlist within 20 seconds. Last read error: $lastReadError"
        }
        if (@(Get-ChildItem -LiteralPath $workingDirectory -File).Count) { throw 'Application files followed the working directory.' }
        $script:publishedProcess.CloseMainWindow() | Out-Null
        if (-not $script:publishedProcess.WaitForExit(5000)) { throw 'The main window did not close cleanly.' }
        $script:publishedProcess.Dispose()
        $script:publishedProcess = $null
    }
    # Use a valid hostfxr with no frameworks, leaving installed runtimes untouched.
    Invoke-TestCheck 'Native installation instructions when .NET is missing' {
        $runtime = Join-Path $temporary 'empty-runtime'
        New-Item -ItemType Directory -Path (Join-Path $runtime 'host') | Out-Null
        $dotnetRoot = Split-Path -Parent (Get-Command dotnet.exe).Source
        Copy-Item -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -Destination (Join-Path $runtime 'host\fxr') -Recurse
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardError = $true
        $start.RedirectStandardOutput = $true
        $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $runtime
        $start.EnvironmentVariables['DOTNET_ROOT'] = $runtime
        $start.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS'] = '1'
        $missing = [Diagnostics.Process]::Start($start)
        try {
            if (-not $missing.WaitForExit(10000)) { $missing.Kill(); throw 'The runtime check timed out.' }
            $message = $missing.StandardError.ReadToEnd()
            if ($missing.ExitCode -eq 0 -or $message -notmatch 'You must install or update .NET') {
                throw 'Expected native .NET installation instructions.'
            }
        }
        finally { $missing.Dispose() }
    }
    Complete-TestRun
}
finally {
    if ($script:publishedProcess) {
        if (-not $script:publishedProcess.HasExited) { $script:publishedProcess.Kill(); $script:publishedProcess.WaitForExit() }
        $script:publishedProcess.Dispose()
    }
    Remove-TestDirectory $temporary
    Pop-Location
}
