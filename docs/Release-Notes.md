# AudioPlayer portable release notes (next release)

Windows x64; requires **.NET 10 Windows Desktop Runtime x64**. Version in the project: **0.1.7**. This text is prepared for the next `portable-*` release; it is not evidence that a new release tag has been published.

- Playback, pause, stop, seek and RMS-based silence navigation for WAV/MP3/AAC-M4A.
- Recursive folder scanning and manual refresh; configurable exact-name, case-insensitive subdirectory exclusions (empty by default), reparse points and temporary trim audio. Explicitly selected roots are exempt from name exclusions.
- Configurable Ctrl+Left/Right timed seeks (default 5/4 seconds), fixed 480 × 430 DIP settings dialog, and four persistent playback modes with concise tooltips. Natural completion alone triggers automatic continuation; manual Stop/seek and errors do not.
- Persistent playlist JSON beside the EXE, readable Chinese paths, startup restoration without autoplay or rescanning, and safe handling of missing paths and access failures.
- Natural relative-path sorting, extension-free filenames and wrapped rounded parent-directory tooltips; updated 36-DIP toolbar, vector icons and progress track offset; virtualized playlist rows without unused row headers or borders.
- Safe trim output names: `<stem>.new.<ext>`, numbered on collision; output silence data stored as `results/<full audio filename>.silent_segments.json`.
- AAC/M4A stream-copy trimming uses separately installed FFmpeg; no FFmpeg binaries or .NET runtime are bundled. The Release provides `AudioPlayer.exe` and `FFmpeg-Setup.txt` as separate assets; put the TXT beside the EXE after downloading.
- Existing user audio, settings JSON, playlist JSON and old executables are not automatically removed or migrated by publishing.

PR #16 was squash merged at `af27658` with successful post-merge Windows CI and 70 cumulative user-reported manual checks across successive revisions. Core/UI/portable checks do not replace manual listening. Real long-pause MP3/WAV testing and selected FFmpeg failure-path tests remain outstanding; see [Testing](Testing.md).

## PR #17 — merged 2026-11-25 (not tagged or released)

- Optional Script companion window with ReadArticles JSON, progressive word coloring, continuous audio-time-driven visual-line scrolling, direct wheel scrolling at one quarter of the original displacement and native scrollbar navigation using the inverse of the continuous follow trajectory with 150 ms debounced seeking; no double-click seek, reader minimize/maximize disabled and reader closes on owner minimize.
- Persistent typography and color settings in a modernized two-tab dialog with normal-weight rounded inputs and selectors, five borderless swatches per palette and no sample-text preview; font sizes 17/18/19/20 DIP, defaults 17 DIP / 1.6 line height / 2 em indent. Retired supported options normalize in memory on load without rewriting the file.
- Microsoft Edge WebView2 Runtime required only for the Script window; not bundled. Portable single EXE remains.
- CI passed at feature revision `6789ffd`; local Windows automated checks and 10 focused manual reader checks passed. Remaining edge-case acceptance is pending. Do not treat this section as a released feature until authorized merge and release.
