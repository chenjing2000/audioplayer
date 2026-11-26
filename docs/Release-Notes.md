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
- CI passed at feature revision `6789ffd`; local Windows automated checks and 10 focused manual reader checks passed. Remaining edge-case acceptance is pending. This section describes the historical PR #17 behavior; later changes are listed below. No release tag is implied.

## Subsequent Script reader improvements (not tagged or released)

- PR #20 (merged): replace foreground word coloring with a single text layer over cumulative soft SVG backgrounds, five light playback colors, same-line whitespace continuity and crispEdges to reduce fractional-pixel seams. Existing settings migrate in memory without rewriting files on load.
- PR #21 (merged): remove the custom wheel handler and 25% multiplier. Native WebView2 wheel and scrollbar scrolling share the existing actual-offset navigation and 150 ms debounce. Disable browser zoom shortcuts; use existing subtitle appearance settings instead. Automatic follow and middle-button press suppression remain unchanged.
- PR #22 (merged at `560d4e3`): subtitle fonts: Newsreader (default, first), Source Serif 4, Georgia, Microsoft YaHei UI and Nunito. Retired Arial, Times New Roman and Segoe UI normalize to Newsreader on load; other valid selections are preserved. Reader fallback is Georgia and generic serif; status text uses Microsoft YaHei UI and generic sans-serif, with no explicit Segoe UI rendering.
- PR #22 (merged): subtitle numeric inputs replace four fixed dropdowns: font size 10–40, line height 1–3 (up to two decimals), paragraph spacing 0–40 and first-line indent 0–5. Existing valid numeric values are preserved; defaults and JSON types are unchanged. Explicit validation failure reveals and selects the invalid input, without switching tabs on ordinary focus loss.
- PR #23 (in development): extend completed timed-unit backgrounds through same-line untimed commas, periods, semicolons, colons, question marks, exclamation marks and ellipses (including consecutive marks). Preserve whitespace bridges, including across sentences, without painting across other punctuation, untimed words or line/paragraph boundaries. Timed punctuation and word progress are unchanged. Initial CI passed at `c11dd0c`; R1–R4 consolidate the allowed-character rule and expand WebView2 regression checks. Initial remediation CI failed at a font-dependent punctuation-wrap fixture; F1–F3 correct the test layout and retain explicit geometry assertions without changing application source. Corrected CI passed at `7ae0c5c` (416 distinct checks); Windows Full / Publish / Test-PublishedApp passed 457 distinct automated checks and the user accepted 23/23 focused manual checks. Final documentation CI and merge authorization pending.
