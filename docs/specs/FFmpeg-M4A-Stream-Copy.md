# M4A AAC stream-copy trimming — implemented specification

**Status: implemented and merged to `main` via [PR #8](https://github.com/chenjing2000/audioplayer/pull/8), squash commit `4b4afc6` (2026-11-23).** The historical [long-silence trimming specification](Long-Silence-Trimming.md) describes the earlier NAudio/Media Foundation implementation. This document supersedes it for AAC/M4A, while WAV/MP3 retain their existing behavior. Windows real-recording and core feature checks passed; certain failure-path cases are still unverified.

## Goal and unchanged behavior

Eliminate AAC re-encoding (and its quality/bitrate changes) when compressing long silences in AAC-encoded M4A audio. Keep NAudio 2.2.1 for playback, pause, stop, seek, PCM RMS silence detection and navigation; keep the existing silence cache, settings, output naming, Toast UI and source-file protections. MP3 and WAV trimming continue on their existing paths. Do not introduce a new audio framework, settings option, codec conversion or automatic encoder fallback.

The existing validated `SilenceResultStore.LoadOrDetect` result and `SilenceTrimmer.PlanCuts` determine cuts in the *original* audio time line. For each detected silence `[s,e)` longer than the configured allowed duration `T`, remove approximately its middle `[s+T/2,e-T/2)` and retain approximately `T/2` at each end. Convert the planned cuts into an ordered set of intervals to **keep**. Handle leading/trailing silence and the original timeline correctly. If there are no cuts, retain the current no-output behavior.

## FFmpeg and supported input

- Use an independently launched FFmpeg executable for **AAC-encoded M4A stream copy**, preferably with one concat-demuxer invocation and `-c:a copy`, `-map 0:a:0`. The exact invocation and timestamp handling must be validated with real audio before acceptance.
- **Do not decode/re-encode AAC** and **do not apply the current 10 ms PCM fade-in/fade-out** on the M4A path. Accept minor AAC-packet-alignment deviations from requested cut points; no sample-accurate cut guarantee.
- M4A is a container; reject unverified ALAC or other non-AAC streams rather than silently transcoding. Preserve source codec, sample rate and channel arrangement.
- Treat FFmpeg success exit status as necessary but not sufficient: concat `inpoint`/`outpoint` can include packets outside boundaries and can cause overlapping/nonmonotonic timestamps. Test a short sample first, then real long AAC with many removed intervals. If the single-invocation approach fails validation, analyze the failure before introducing a multi-step alternative; do not prebuild a fallback encoder or second pipeline.
- Do not use arbitrary percentages as sufficient proof of accuracy on long recordings. Validate output duration against expected removal using packet-scale and segment-count considerations established by actual tests; investigate substantial accumulated drift, timestamp faults, interrupted words or audible artifacts.
- Protect against concat-list path injection and quoting errors: safely escape local Windows paths (including spaces, quotes, apostrophes and non-ASCII names), emit UTF-8 **without BOM**, use a local temporary list, pass process arguments with `ProcessStartInfo.ArgumentList`, and never run the command through a shell.

## Optional dependency and user experience

- Selected distribution: **BtbN Windows x64 GPL Shared** (a tested, fixed build from the `ffmpeg-master-latest-win64-gpl-shared.zip` family). Record the exact executable version/build and package checksum used for acceptance, because the `latest` URL changes. Do not automatically update or download binaries.
- Locate FFmpeg relative to the actual running executable, at `runtimes/ffmpeg/win-x64/bin/ffmpeg.exe`. Its shared DLL dependencies must be colocated; `ffprobe.exe` is recommended for inspection but not required by the application; `ffplay.exe` is unnecessary. No dependence on system `PATH` or SwiftCut's installation.
- Do **not** check FFmpeg at program startup or while browsing, playing, scanning or detecting silences. Check only when M4A trimming requires it. On missing/broken dependency, cancel that trim cleanly and show the **existing error Toast**, not a MessageBox: `FFmpeg 未安装或无法运行，无法裁剪 M4A。请查看程序目录下的 FFmpeg-Setup.txt，按照说明安装。`
- The complete user-facing instructions live in exactly one maintained `docs/FFmpeg-Setup.txt`; build and publish must place only this **setup TXT**, not the repository's whole README, beside the executable. Installation needs no application restart.
- FFmpeg is **optional and separately installed by the user**; do not bundle downloaded FFmpeg binaries in the release or repository. GPL Shared redistribution considerations remain relevant if distribution changes later.

## Output safety and acceptance

Keep the original source untouched and verify its fingerprint before committing output. Generate temporary output and concat manifest under controlled local paths, clean both on failure/cancellation, and only commit a new non-overwriting `<stem>.lesssilence[ (N)].m4a` after validation. Check that output is nonempty, is a valid readable AAC/M4A, decodes fully, has reasonable duration and valid playback timestamps, and retains source audio parameters. Reuse the existing source-change check, atomic rename behavior, result Toast and output `results/<full filename>/silence.json` re-analysis. Output-cache failure after successful audio commit must not delete the audio.

Terminate the **specific** FFmpeg child process on cancellation; avoid leaving orphaned processes or temporary files. Limit captured diagnostics to a useful failure message; do not add a logging framework. Handle absent executable, incomplete/mismatched DLLs, unsupported codec, quotes/Chinese/Unicode paths, no cuts, FFmpeg errors, cancel while running, output collisions, source modification, and incomplete output.

### Verification before merge

1. Unit tests for kept intervals and safe concat-list formatting, without external media.
2. Windows dependency-missing and incomplete-DLL checks: normal startup/playback/detection still work; M4A trimming gives the short Toast referring to `FFmpeg-Setup.txt`; no MessageBox, source changes or committed output.
3. Windows M4A integration with a pinned BtbN GPL Shared build: multiple cuts, original AAC codec/sample rate/channels retained, no encode step, output duration/size and successful full decode, sensible timestamps, and audio listening at representative joins.
4. Long AAC file with many cuts, Unicode/space/apostrophe paths, cancellation, source-change, collision and cleanup verification. Regression tests for MP3/WAV and existing cache, UI and publish checks.
5. Build and publish must place `FFmpeg-Setup.txt` beside the *actual* development/published EXE; published-EXE tests check it. Do not require an FFmpeg download in ordinary CI or publishing.

The original pre-merge checklist above is retained as a specification, not a claim that every edge-case test has passed. See the dated acceptance evidence below and in Testing for verified results and remaining gaps.

## Implementation tracking

[PR #8](https://github.com/chenjing2000/audioplayer/pull/8) merged the stream-copy implementation into `main@4b4afc6`. The remote feature and earlier design branches have been cleaned up; only `main` remains. Feature-branch Windows CI and user-reported M4A listening/ffprobe acceptance passed. Do not confuse these with unperformed failure-path tests or a new post-merge CI run.

## Feature-branch verification (2026-11-23)

The user verified Windows Core/UI/publish/published-EXE tests (47/113/5/4 passed). The real AAC-LC stereo 48-kHz recording changed from 7142.442667 s / 102033724 bytes to 5468.970021 s / 81561519 bytes, retaining its codec, profile, sample rate and channels. Playback and join listening were accepted by the user. See [Testing](../Testing.md#2026-11-23-pr-8-windows-aacm4a-stream-copy-acceptance-evidence). Exact BtbN binary hash, incomplete-DLL, active cancellation and source-change edge cases remain to be separately checked. PR #8 is merged.
