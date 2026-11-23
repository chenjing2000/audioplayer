# AudioPlayer — development handoff

**Updated:** 2026-11-23 (PR #11 merged and post-merge checks verified)  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Latest application-code baseline:** `c90afec` (PR #11, squash merged; subsequent documentation-only commits do not change application behavior). [Post-merge Windows CI #45190102017](https://github.com/chenjing2000/audioplayer/actions/runs/45190102017) passed. After PR #11, the user confirmed that their local `main` matched `origin/main`, the worktree was clean, and the former `feature/code-and-doc-cleanup` branch was deleted locally and remotely.

## Current application behavior

- C# / WPF / .NET 10, NAudio 2.2.1, TagLibSharp 2.3.0. Project `AudioPlayer/AudioPlayer.csproj`, tests `tests/AudioPlayer.Tests/AudioPlayer.Tests.csproj`, portable executable `dist/AudioPlayer.exe`. `PlaybackEngine` owns audio output.
- Nonrecursive WAV/MP3/M4A scan, natural filenames, playback/seek/stop, silence midpoint navigation and selection-based trim. Refresh scans first, closes old playback only on success, does not resume. Current toolbar has 36×36 hit areas, 28×28 icons, 5-DIP right-side gaps and a 36-DIP slider with `TranslateTransform Y=10`.
- Source silence data: `results/<full audio filename>.silent_segments.json`. No SHA or legacy-result migration; invalid caches are re-detected. WAV/MP3 trim uses PCM and 10-ms fade. AAC/M4A trim uses separately installed FFmpeg `-c:a copy` without re-encoding or fade; only M4A trim requires FFmpeg.
- Output naming: `<stem>.new.<ext>`, then `<stem>.new (N).<ext>` with no overwrite. The source remains untouched and the output gets independently analyzed. The optional installation instructions are in `docs/FFmpeg-Setup.txt`. User settings reside beside the EXE, named after its actual stem; no automatic migration.
- User-reported Windows acceptance after PR #10: settings save, M4A playback and trim, refresh, taskbar icon and the final 10-DIP progress position passed. Real long-silence MP3/WAV listening, FFmpeg incomplete-DLL/cancellation/source-mutation edge cases and exact BtbN package checksum remain unverified.

## Completed maintenance (PR #11)

PR #11 was developed on `feature/code-and-doc-cleanup` from `main@2be037f` and [squash merged](https://github.com/chenjing2000/audioplayer/pull/11) as `main@c90afec`. It hides in-progress trim temporary audio from folder scans, adds a regression fixture, attaches `FFmpeg-Setup.txt` to future GitHub Release assets, ignores local WAV/M4A files in Git, corrects the old cache-name Toast, and avoids buffering unused FFmpeg stderr. Documentation now consolidates current rules and removes redundant completed plans; historical specifications and dated test evidence remain available.

**PR #11 was accepted by the user on Windows on 2026-11-23 and is now merged.** Local PowerShell tests passed: 48 Core assertions, 127 WPF UI assertions, 5 publishing safeguards, portable EXE build and 4 published-EXE checks, all with zero failures. The user confirmed an in-progress trim file stays out of the playlist when reopening the folder, the completed output appears after refresh and plays, and existing M4A playback/trimming and settings persistence have no observed regression. The pre-merge PR check [#45189289382](https://github.com/chenjing2000/audioplayer/actions/runs/45189289382) passed; a separate push check initially failed an intermittent Toast UI assertion and [passed on retry](https://github.com/chenjing2000/audioplayer/actions/runs/45189280854). The merged `main` [CI #45190102017](https://github.com/chenjing2000/audioplayer/actions/runs/45190102017) passed. Full evidence and skipped cases are recorded in [Testing](Testing.md). Real long-pause MP3/WAV, optional MP3-dependent checks, selected FFmpeg faults and actual tagged Release assets remain unverified.

## Next steps and safety

1. Start with [program overview](program-overview.md), [project contract](project-contract.md), [Testing](Testing.md), and [Publishing](Publishing.md); see [documentation index](README.md).
2. The Windows Core/UI/published-EXE and manual trim/refresh/M4A/settings checks passed user acceptance on 2026-11-23. Inspect the two downloadable assets when a future tagged GitHub Release is actually created. Real MP3/WAV and selected FFmpeg fault tests remain deferred.
3. Preserve source audio, existing outputs, configuration and installed FFmpeg in future changes. Avoid unnecessary MVVM/DI/task-framework abstractions and broad refactoring.
4. Discuss new requirements before implementation. Do not merge to `main` without user authorization; use English commit messages, squash merge after acceptance, and clean branches afterward.

### Windows verification commands

```powershell
cd C:\Projects\AudioPlayer
git fetch origin
git switch main
git pull --ff-only origin main
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```
