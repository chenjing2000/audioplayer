# AudioPlayer portable release notes (next release)

Windows x64; requires **.NET 10 Windows Desktop Runtime x64**. Version in the project: **0.1.7**. This text is prepared for the next `portable-*` release; it is not evidence that a new release tag has been published.

- Playback, pause, stop, seek and RMS-based silence navigation for WAV/MP3/AAC-M4A.
- Recursive folder scanning and manual refresh; skip subdirectories named exactly `audio` (case-insensitive), reparse points and temporary trim audio. An explicitly selected root named `audio` is allowed.
- Persistent playlist JSON beside the EXE, readable Chinese paths, startup restoration without autoplay or rescanning, and safe handling of missing paths and access failures.
- Natural relative-path sorting, extension-free filenames and wrapped rounded parent-directory tooltips; updated 36-DIP toolbar, vector icons and progress track offset.
- Safe trim output names: `<stem>.new.<ext>`, numbered on collision; output silence data stored as `results/<full audio filename>.silent_segments.json`.
- AAC/M4A stream-copy trimming uses separately installed FFmpeg; no FFmpeg binaries or .NET runtime are bundled. The Release provides `AudioPlayer.exe` and `FFmpeg-Setup.txt` as separate assets; put the TXT beside the EXE after downloading.
- Existing user audio, settings JSON, playlist JSON and old executables are not automatically removed or migrated by publishing.

PR #15 was squash merged at `7cb288e` with successful post-merge Windows CI and 27 cumulative user-reported manual checks. Core/UI/portable checks do not replace manual listening. Real long-pause MP3/WAV testing and selected FFmpeg failure-path tests remain outstanding; see [Testing](Testing.md).
