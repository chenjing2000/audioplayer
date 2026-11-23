# AudioPlayer — development handoff

**Updated:** 2026-11-23 (PR #8 merged; obsolete branches cleaned)  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Latest merged feature baseline:** `main@4b4afc6` ([PR #8](https://github.com/chenjing2000/audioplayer/pull/8), optional FFmpeg M4A stream-copy trimming). Later documentation commits may advance `main`.

## Current status

- Long-silence trimming merged via [PR #1](https://github.com/chenjing2000/audioplayer/pull/1). The results/Toast/silence.json refactor merged via [PR #2](https://github.com/chenjing2000/audioplayer/pull/2). Focused logic cleanup merged via [PR #3](https://github.com/chenjing2000/audioplayer/pull/3).
- Unified teal/white AudioPlayer v3 icons merged via [PR #4](https://github.com/chenjing2000/audioplayer/pull/4). Play triangle horizontal balance and Stop square 35% height merged via [PR #5](https://github.com/chenjing2000/audioplayer/pull/5).
- [PR #7](https://github.com/chenjing2000/audioplayer/pull/7) merged the FFmpeg implementation specification; [PR #8](https://github.com/chenjing2000/audioplayer/pull/8) merged the implemented AAC/M4A stream-copy feature via squash commit `4b4afc6`. GitHub reports only the remote `main` branch. The user confirmed local/remote feature-branch cleanup; do not infer a fresh local working-tree inspection from that report.
- PR #8 feature-branch Windows CI [#45164533459](https://github.com/chenjing2000/audioplayer/actions/runs/45164533459) **passed** after final review. User-reported local Windows acceptance: **47 Core, 113 UI, 5 publishing safeguards, 4 published-EXE checks**, plus real M4A playback and listening. These are feature-branch checks, not a claim of a new post-merge `main` run.
- Windows M4A/interaction/listening acceptance was reported for the audio refactor, and the user accepted the icon appearance on Windows. **Real long-pause MP3/WAV media acceptance remains deferred.** Automated CI does not replace actual codec and listening tests.

## Application and UI baseline

- Windows desktop: **C# / WPF / .NET 10**; NAudio 2.2.1, TagLibSharp 2.3.0. Project: `EnglishAudioPlayer/AudioPausePlayer.csproj`. **FFmpeg AAC/M4A stream-copy trimming is merged in `main`**; BtbN Windows x64 GPL Shared FFmpeg remains optional and user-installed. No MVVM framework, DI container, or unnecessary new abstractions.
- The selected directory is scanned nonrecursively for WAV/MP3/M4A. Playback, pause, stop, seek, left/right silence-midpoint navigation, and silence compression must retain their existing behavior.
- Three detection settings control PCM RMS/dBFS analysis. Validated silence ranges are shared by navigation and trimming and persisted as one `results/<full audio filename>/silence.json` per audio; protect source files, reject stale results, and never overwrite existing audio.
- Five settings live in `<actual EXE stem>.settings.json` beside the EXE. Toast messages are WPF-native. Settings validation and previous confirmed behavior must be preserved.
- Toolbar: **36×36 hit areas, 28×28 images**, no visible button border/background, hover/press via opacity only. Six native `DrawingImage` resources in `EnglishAudioPlayer/ui/Icons.xaml`: OpenFolder, Play, Pause, Stop, Trim, Settings. Play and Pause are states of **one button**.
- Icon palette: teal circle `#26798A`, white glyph `#FFFFFF`, transparent outside. Most glyphs occupy ~50% of circle height; Stop's rounded white square is **35%** of the circle diameter, centered; Play's white triangle is shifted slightly right for balanced corner spacing. `EnglishAudioPlayer/ui/main.ico` uses the white-leaf-on-teal-circle application icon (Windows-compatible 16/32/48/256 px); it was **not** changed in PR #5.
- `audio.svg` was a design reference, not an extra toolbar control. Keep the existing playlist-state marks and playback logic.

## Relevant documentation

- [Documentation index](README.md), [application behavior](program-overview.md), [project contract](project-contract.md), [configuration](Configuration.md).
- [Current result/Toast specification](specs/Results-Cache-Toast-Refactor.md) and its [historical implementation plan](guides/Results-Cache-Toast-Implementation.md). The earlier [long-silence specification](specs/Long-Silence-Trimming.md) remains relevant for PCM, output format and safety.
- [Testing and acceptance](Testing.md), [Publishing](Publishing.md). Preserve historical dated audits as historical evidence; do not promote older test counts to the current release.

## Implemented: optional FFmpeg AAC/M4A stream copy (merged PR #8)

- [FFmpeg/M4A specification](specs/FFmpeg-M4A-Stream-Copy.md): NAudio stays responsible for playback, navigation and RMS detection; AAC/M4A uses optional BtbN Windows x64 GPL Shared FFmpeg with one concat-demuxer stream-copy path (`-c:a copy`), no re-encoding and no 10 ms fades. MP3/WAV trimming is unchanged.
- Only check the FFmpeg dependency on an M4A trim request. If missing/broken, use an existing error Toast referring to **`FFmpeg-Setup.txt`**, not a MessageBox; no startup check, automatic download, fallback encoder or unrelated MP3/WAV changes.
- Maintain [`docs/FFmpeg-Setup.txt`](FFmpeg-Setup.txt) as the **only** installation-instructions file copied beside the development/published EXE. Do not copy the repository README or bundle FFmpeg binaries.
- **Windows feature-branch evidence:** Core 47/47, UI 113/113, publishing safeguards 5/5 and published-EXE checks 4/4; AAC-LC stereo 48-kHz source 7142.442667 s / 102,033,724 bytes -> trimmed 5468.970021 s / 81,561,519 bytes. User confirmed playback and natural joins. See [Testing](Testing.md#2026-11-23-pr-8-windows-aacm4a-stream-copy-acceptance-evidence). Incomplete DLL, active cancellation, source-mutation scenarios and exact FFmpeg build checksum remain unverified.

## Next development session

1. Read this handoff and inspect the **current** `main` source, docs and latest PR/CI status before modifying anything.
2. Discuss any new requirement and choose the smallest viable change. Avoid unnecessary frameworks, refactors, compatibility layers and duplicate state.
3. Create a dedicated feature branch from the latest clean `main`; use descriptive **English** commit messages. Do not merge to `main` without user authorization.
4. For code changes, run appropriate Windows tests, then publish and check the EXE; report real-media or visual acceptance separately from CI. Do not claim deferred MP3/WAV listening tests passed.
5. Once the user approves the implementation, squash-merge and let the user clean up local and remote feature branches, then verify `main` is synchronized and clean.

### Windows verification commands

```powershell
cd C:\Projects\AudioPlayer
git switch main
git pull --ff-only origin main
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```

**PR #8 is merged and its feature branch is cleaned up.** Use latest `main` for the next task. Remaining edge-case checks are documented in Testing; do not label them as passed or start unrelated refactoring.
