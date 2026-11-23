# AudioPlayer — development handoff

**Updated:** 2026-11-23 (PR #10 in review: toolbar/refresh/naming and full engineering rename)  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Latest merged baseline:** `main@de5dee4` (PR #8 AAC/M4A stream copy; PR #9 docs sync). New work is on `feature/toolbar-refresh-and-output-naming`, not yet merged.

## Current status

- Long-silence trimming merged via [PR #1](https://github.com/chenjing2000/audioplayer/pull/1). The results/Toast/silence.json refactor merged via [PR #2](https://github.com/chenjing2000/audioplayer/pull/2). Focused logic cleanup merged via [PR #3](https://github.com/chenjing2000/audioplayer/pull/3).
- Unified teal/white AudioPlayer v3 icons merged via [PR #4](https://github.com/chenjing2000/audioplayer/pull/4). Play triangle horizontal balance and Stop square 35% height merged via [PR #5](https://github.com/chenjing2000/audioplayer/pull/5).
- [PR #7](https://github.com/chenjing2000/audioplayer/pull/7) merged the FFmpeg implementation specification; [PR #8](https://github.com/chenjing2000/audioplayer/pull/8) merged the implemented AAC/M4A stream-copy feature via squash commit `4b4afc6`. After PR #9, only remote `main` remained; a separate new feature branch now exists. Do not infer local working-tree status without user verification.
- PR #8 feature-branch Windows CI [#45164533459](https://github.com/chenjing2000/audioplayer/actions/runs/45164533459) **passed** after final review. User-reported local Windows acceptance: **47 Core, 113 UI, 5 publishing safeguards, 4 published-EXE checks**, plus real M4A playback and listening. These are feature-branch checks, not a claim of a new post-merge `main` run.
- `feature/toolbar-refresh-and-output-naming` changes manual folder refresh, toolbar spacing/alignment, trim output suffix and silence result paths. Branch CI [#45171773385](https://github.com/chenjing2000/audioplayer/actions/runs/45171773385) passed: 47 Core, 124 UI, 5 publishing safeguards and 4 published-EXE checks. Windows visual/taskbar and real-audio acceptance remain **pending**. The checked-in 16/32/48-pixel ICO layers already use the teal/white icon, and the 256-pixel layer is present; any old pinned taskbar icon must be investigated on Windows before changing the ICO.
- Windows M4A/interaction/listening acceptance was reported for the audio refactor, and the user accepted the icon appearance on Windows. **Real long-pause MP3/WAV media acceptance remains deferred.** Automated CI does not replace actual codec and listening tests.

## Application and UI baseline

- Windows desktop: **C# / WPF / .NET 10**; NAudio 2.2.1, TagLibSharp 2.3.0. Project: `AudioPlayer/AudioPlayer.csproj`. **FFmpeg AAC/M4A stream-copy trimming is merged in `main`**; BtbN Windows x64 GPL Shared FFmpeg remains optional and user-installed. No MVVM framework, DI container, or unnecessary new abstractions.
- The selected directory is scanned nonrecursively for WAV/MP3/M4A. Playback, pause, stop, seek, left/right silence-midpoint navigation, and silence compression must retain their existing behavior.
- Three detection settings control PCM RMS/dBFS analysis. Validated silence ranges are shared by navigation and trimming and persisted as one `results/<full audio filename>.silent_segments.json` per audio on the feature branch (no old-path migration); protect source files, reject stale results, and never overwrite existing audio.
- Five settings live in `<actual EXE stem>.settings.json` beside the EXE. Toast messages are WPF-native. Settings validation and previous confirmed behavior must be preserved.
- Toolbar: **36×36 hit areas, 28×28 images**, no visible button border/background, hover/press via opacity only. Seven native `DrawingImage` resources in `AudioPlayer/ui/Icons.xaml`: OpenFolder, Play, Pause, Stop, Trim, Refresh, Settings. Right-side button gaps are 5 rather than 10; the Slider control height is 36 to match the buttons, with a visual `TranslateTransform Y=10` DIP offset (its layout alignment remains Center). Play and Pause are states of **one button**.
- Icon palette: teal circle `#26798A`, white glyph `#FFFFFF`, transparent outside. Most glyphs occupy ~50% of circle height; Stop's rounded white square is **35%** of the circle diameter, centered; Play's white triangle is shifted slightly right for balanced corner spacing. `AudioPlayer/ui/main.ico` uses the white-leaf-on-teal-circle application icon (Windows-compatible 16/32/48/256 px); it was **not** changed in PR #5.
- `audio.svg` was a design reference, not an extra toolbar control. Keep the existing playlist-state marks and playback logic.

## Relevant documentation

- [Documentation index](README.md), [application behavior](program-overview.md), [project contract](project-contract.md), [configuration](Configuration.md).
- [Current result/Toast specification](specs/Results-Cache-Toast-Refactor.md) and its [historical implementation plan](guides/Results-Cache-Toast-Implementation.md). The earlier [long-silence specification](specs/Long-Silence-Trimming.md) remains relevant for PCM, output format and safety.
- [Testing and acceptance](Testing.md), [Publishing](Publishing.md). Preserve historical dated audits as historical evidence; do not promote older test counts to the current release.

## Implemented: optional FFmpeg AAC/M4A stream copy (merged PR #8)

- [FFmpeg/M4A specification](specs/FFmpeg-M4A-Stream-Copy.md): NAudio stays responsible for playback, navigation and RMS detection; AAC/M4A uses optional BtbN Windows x64 GPL Shared FFmpeg with one concat-demuxer stream-copy path (`-c:a copy`), no re-encoding and no 10 ms fades. MP3/WAV trimming is unchanged.
- Only check the FFmpeg dependency on an M4A trim request. If missing/broken, use an existing error Toast referring to **`FFmpeg-Setup.txt`**, not a MessageBox; no startup check, automatic download, fallback encoder or unrelated MP3/WAV changes.
- Maintain [`docs/FFmpeg-Setup.txt`](FFmpeg-Setup.txt) as the **only** installation-instructions file copied beside the development/published EXE. Do not copy the repository README or bundle FFmpeg binaries.
- **New branch output naming:** `<stem>.new[ (N)].<ext>` without overwrite; M4A keeps FFmpeg stream copy, WAV/MP3 keep the PCM paths. The folder refresh button invokes existing `LoadFolderAsync(currentFolder)`, scans before closing playback, and does not resume playback after a successful reload. Scan failure preserves existing list/playback. Old nested silence results are ignored and left untouched.
- **Windows feature-branch evidence (PR #8, historical):** Core 47/47, UI 113/113, publishing safeguards 5/5 and published-EXE checks 4/4; AAC-LC stereo 48-kHz source 7142.442667 s / 102,033,724 bytes -> trimmed 5468.970021 s / 81,561,519 bytes. User confirmed playback and natural joins. See [Testing](Testing.md#2026-11-23-pr-8-windows-aacm4a-stream-copy-acceptance-evidence). Incomplete DLL, active cancellation, source-mutation scenarios and exact FFmpeg build checksum remain unverified.

## Engineering name consolidation (PR #10, unmerged)

- Project and test directories/files are now `AudioPlayer/AudioPlayer.csproj` and `tests/AudioPlayer.Tests/AudioPlayer.Tests.csproj`. C# namespaces, XAML `x:Class` declarations, resource pack URIs and friend-assembly references are unified under `AudioPlayer` / `AudioPlayer.Tests`; the playback implementation class is `AudioPlayer.PlaybackEngine` (renamed from the class that conflicted with the new namespace in WPF-generated markup).
- The window, assembly and portable executable are named `AudioPlayer` / `AudioPlayer.exe`. Default settings are stored in `AudioPlayer.settings.json`; renamed executables still use their own name. Old settings are **not auto-migrated**: if desired, manually copy settings after closing the old program and before first launch, without overwriting existing new settings.
- The publishing script produces `dist/AudioPlayer.exe` and leaves any old `旧版 EXE` or settings files untouched; delete the obsolete EXE manually only after acceptance. No automatic deletion or compatibility logic.
- Renamed-build Windows CI [#45178109384](https://github.com/chenjing2000/audioplayer/actions/runs/45178109384) **passed**: 47 Core, 125 UI, 5 publishing safeguards, and 4 published-EXE checks. Audio-device playback was skipped on the CI runner; real-device playback, taskbar appearance, configuration preservation and optional FFmpeg M4A acceptance remain pending. Historical test counts refer to their original branches.

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

**PR #8 and PR #9 are merged.** The new work is confined to `feature/toolbar-refresh-and-output-naming`; do not merge or claim Windows acceptance before explicit authorization. The remaining PR #8 failure-path checks remain deferred. Remaining edge-case checks are documented in Testing; do not label them as passed or start unrelated refactoring.
