# AudioPlayer — development handoff

**Updated:** 2026-11-23 (post-PR #10 code/documentation cleanup branch)  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Last verified merged main:** `2be037f` (PR #10, squash merged). [Post-merge Windows CI #45182410792](https://github.com/chenjing2000/audioplayer/actions/runs/45182410792) passed. The former `feature/toolbar-refresh-and-output-naming` remote branch was removed.

## Current application behavior

- C# / WPF / .NET 10, NAudio 2.2.1, TagLibSharp 2.3.0. Project `AudioPlayer/AudioPlayer.csproj`, tests `tests/AudioPlayer.Tests/AudioPlayer.Tests.csproj`, portable executable `dist/AudioPlayer.exe`. `PlaybackEngine` owns audio output.
- Nonrecursive WAV/MP3/M4A scan, natural filenames, playback/seek/stop, silence midpoint navigation and selection-based trim. Refresh scans first, closes old playback only on success, does not resume. Current toolbar has 36×36 hit areas, 28×28 icons, 5-DIP right-side gaps and a 36-DIP slider with `TranslateTransform Y=10`.
- Source silence data: `results/<full audio filename>.silent_segments.json`. No SHA or legacy-result migration; invalid caches are re-detected. WAV/MP3 trim uses PCM and 10-ms fade. AAC/M4A trim uses separately installed FFmpeg `-c:a copy` without re-encoding or fade; only M4A trim requires FFmpeg.
- Output naming: `<stem>.new.<ext>`, then `<stem>.new (N).<ext>` with no overwrite. The source remains untouched and the output gets independently analyzed. The optional installation instructions are in `docs/FFmpeg-Setup.txt`. User settings reside beside the EXE, named after its actual stem; no automatic migration.
- User-reported Windows acceptance after PR #10: settings save, M4A playback and trim, refresh, taskbar icon and the final 10-DIP progress position passed. Real long-silence MP3/WAV listening, FFmpeg incomplete-DLL/cancellation/source-mutation edge cases and exact BtbN package checksum remain unverified.

## In-progress maintenance branch

`feature/code-and-doc-cleanup` was created from verified `main@2be037f`. It fixes exposure of in-progress trim temporary audio in folder scans, adds a regression fixture, attaches `FFmpeg-Setup.txt` to future GitHub Release assets, ignores local WAV/M4A files in Git, corrects the old cache-name Toast, and avoids buffering unused FFmpeg stderr. Documentation now consolidates the current rules and removes redundant completed implementation plans; historical specifications and dated test evidence remain available.

**This cleanup branch passed user-confirmed Windows acceptance on 2026-11-23, but PR #11 is not merged.** Local Windows PowerShell results: 48 Core assertions passed (0 failed); 127 WPF UI assertions passed (0 failed); 5 publishing safeguards passed; portable EXE published; 4 published-EXE checks passed. The user manually confirmed that reopening the audio folder during a trim does not list temporary output, a completed output appears after refresh and plays normally, and existing M4A playback, M4A trimming and settings persistence all behave as expected. Revision `e392114` passed [Windows PR CI #45187026289](https://github.com/chenjing2000/audioplayer/actions/runs/45187026289). Full evidence and skips are recorded in [Testing](Testing.md). Real long-pause MP3/WAV, optional MP3-dependent automated checks, selected FFmpeg failure cases and actual next Release assets have not been verified. [Draft PR #11](https://github.com/chenjing2000/audioplayer/pull/11) awaits separate user authorization for merging.

## Next steps and safety

1. Start with [program overview](program-overview.md), [project contract](project-contract.md), [Testing](Testing.md), and [Publishing](Publishing.md); see [documentation index](README.md).
2. The Windows Core/UI/published-EXE and manual trim/refresh/M4A/settings checks passed user acceptance on 2026-11-23. Inspect the two downloadable assets when a future tagged GitHub Release is actually created. Real MP3/WAV and selected FFmpeg fault tests remain deferred.
3. Do not alter source audio, existing outputs, config or installed FFmpeg during cleanup. Avoid unnecessary MVVM/DI/task-framework abstractions and broad refactoring.
4. Discuss new requirements before implementation. Do not merge to `main` without user authorization; use English commit messages, squash merge after acceptance, and clean branches afterward.

### Windows verification commands

```powershell
cd C:\Projects\AudioPlayer
git fetch origin
git switch feature/code-and-doc-cleanup
git pull --ff-only origin feature/code-and-doc-cleanup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```
