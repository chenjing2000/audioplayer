# Testing and acceptance

Run commands from the repository root on Windows with .NET 10 SDK. Scripts work in Windows PowerShell 5.1 and PowerShell 7. Every .ps1 source file must contain English ASCII text only; Test.ps1 checks both this rule and script syntax.

## Unified entry points

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Full
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```

Core selects fast checks that need no external content. UI includes window checks; Full adds available real-content integration. Existing runners retain their assertions and may also run core checks when UI is selected. Native input and playback checks must run serially without other desktop interaction.

The summary reports Passed, Failed, and Skipped test groups. Existing runners print individual case counts. A failed native command or assertion makes the script exit nonzero. Skipped external-content checks are explicitly printed and are not counted as verified. An explicitly supplied missing/invalid data path is an error, not a skip.

## Published EXE acceptance

Test-PublishedApp.ps1 copies only the EXE into a temporary path with spaces, renames it, and launches it from another working directory. It checks the main window, configuration placement, clean closure, and the native missing-.NET installation instructions using an isolated runtime root. Installed runtimes and user configuration are untouched.

Use -ExePath to verify an explicit binary. Tests do not publish automatically; an absent EXE is an error. Missing-runtime checks disable the graphical error popup and verify its text instructions rather than visually inspecting the popup. UI/audio checks do not replace listening and manual user acceptance.

## Workflow regression checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Workflow.ps1
```

These checks replace external test processes and verify successful/failing exit handling and skip reporting. Test-Publish.ps1 separately exercises actual staged replacement against temporary files. Full application tests and actual EXE acceptance are required beyond these orchestration tests.

## Test data and ownership

External data paths are passed as arguments; scripts do not hard-code a developer's private directories. Source data is read and copied to unique temporary directories. Temporary files and environment overrides are cleaned up/restored; existing repository fixtures and original user files are not changed. Do not commit media, personal settings, or test output.

### AudioPlayer

Use `-AudioDirectory` with a **folder containing actual MP3 files**. It copies one MP3 into a temporary directory and runs the legacy MP3 metadata, playback, and UI checks. A standalone M4A file must not be passed as an MP3 directory.

Use `-AudioFile` for a **single WAV, MP3, or M4A file containing at least one compressible silence interval**. The runner copies the media to a temporary directory with its **original extension**, runs the real long-silence trimming pipeline and verifies the output extension, that it can be decoded and that the source remains unchanged. A file with no interval longer than the configured allowed duration produces no output by design, so it is not a valid positive trimming fixture. This does not pretend that an M4A is MP3; the old MP3-specific playback/UI tests are skipped unless `-AudioDirectory` is also supplied. Large recordings may take extra time to decode and encode.

```powershell
.\tools\Test.ps1 -Suite Full -AudioDirectory '<local MP3 directory>'
.\tools\Test.ps1 -Suite Full -AudioFile '<local recording.m4a>'
.\tools\Test.ps1 -Suite Full -AudioDirectory '<local MP3 directory>' -AudioFile '<local recording.m4a>'
```

All supplied media is copied to a task-owned temporary directory; the user's originals are never modified. The test script rejects file paths passed to `-AudioDirectory` and recommends `-AudioFile` instead.

Detailed PCM/navigation and playback behavior is described in [Program Overview](program-overview.md).

## CI and results

The tag-triggered GitHub Release workflow runs Core, publishes the EXE, and then runs Test-PublishedApp.ps1 before uploading it. It cannot access private local recordings or dictionaries, so it does not certify Full real-content checks. Dated local results and remaining limitations are recorded in [Health Check](Health-Check-2026-10-13.md).

## Long-silence trimming acceptance

Before release, verify WAV, MP3 and AAC-in-M4A inputs/outputs on a real Windows 10/11 computer using the installed Media Foundation codecs. Include PCM-generated fixtures for: no silence; short, equal and longer-than-allowed silence; multiple segments; silence at both ends; stereo; fractional seconds; 10-ms fading; and old/new settings validation. Check the generated output can be decoded, has the expected length (allowing compressed-codec padding), and does not overwrite an existing or source file. Test cancellation on shutdown and selection without focus. Automated tests alone do not establish that the system AAC/MP3 encoder is available; report real-device tests separately.

### 2026-11-23: local Windows acceptance evidence for long-silence trimming

Evidence supplied by the user from Windows PowerShell on `feature/long-silence-trimming` at `7e9dbb7`, with a separately confirmed listening check:

| Check | Result |
| --- | --- |
| `Test.ps1 -Suite Full -AudioFile <AAC/M4A file>` | 43 application tests passed, 0 failed; 62 UI assertions passed, 0 failed; MP3 playback skipped |
| `Test.ps1 -Suite UI` | 42 application tests passed, 0 failed; 62 UI assertions passed, 0 failed |
| M4A end-to-end trim | 119 silence intervals compressed; removed 1673.473 s |
| Source vs resulting M4A duration | 7142.443 s vs 5468.971 s, subject to codec rounding |
| Output format and source-file safety | Test passed: same extension, output could be decoded, source retained |
| Linked minimum/allowed silence settings | User confirmed the real UI validation works |
| Listening test | User confirmed that speech remains complete and no obvious popping is heard at joins |
| Real MP3 and WAV long-pause recordings | **Deferred at user request**: no suitable recordings available |

The M4A end-to-end test and listening confirmation are **real-machine acceptance evidence**, not evidence that every system codec, sample rate, MP3 file or WAV file has been tested. Synthetic WAV/PCM regression tests remain in Core. Do not change MP3/WAV real-media coverage from deferred to passed without a separate test.


## Results/Toast refactor: automated coverage and acceptance

The approved [refactor specification](specs/Results-Cache-Toast-Refactor.md) and [implementation checklist](guides/Results-Cache-Toast-Implementation.md) require updated Core/UI/Full tests for the single `results/<full audio filename>/silence.json` store, metadata-based invalidation without SHA-256, automatic detection/cancellation, navigation from verified in-memory ranges, cache-based trimming without a second scan, numbered non-overwriting outputs, independent results for newly encoded media, Toast behavior, one-decimal settings validation, and playlist alignment.

The implementation and new Core/UI assertions are committed on `feature/results-cache-toast-refactor`. Their actual pass/fail evidence must be taken from this branch's CI runs and new Windows acceptance, not from the earlier M4A/Windows records. Use temporary synthetic WAV/PCM fixtures where possible. Real long-pause MP3/WAV testing remains deferred until suitable files exist; do not turn a Skip into Pass.


### 2026-11-23: feature-branch Windows CI evidence

Workflow: [Feature validation #45101311195](https://github.com/chenjing2000/audioplayer/actions/runs/45101311195), commit \`1c50a6cdb\` on \`feature/results-cache-toast-refactor\`.

| Verification | Result |
| --- | --- |
| \`tools/Test.ps1 -Suite Core\` | **Passed**: 44 application assertions, 0 failed; publishing safeguard checks passed |
| \`tools/Test.ps1 -Suite UI\` | **Passed**: 44 core assertions and 77 WPF UI assertions, 0 failed |
| Portable EXE publish | **Passed** |
| Published EXE acceptance | **Passed**: 3 groups, 0 failed |
| Actual audio-device playback during hosted UI tests | **Skipped**: hosted Windows runner reported no available \`waveOut\` output device; playback is not claimed verified |
| Optional local real MP3 metadata/playback | **Skipped**: no local MP3 supplied to CI |
| This refactor's real M4A cache/navigation/numbered-trim/output-result/Toast and listening acceptance | **Passed later on the user's Windows machine**; see the dated local acceptance entry below |
| Real long-pause MP3 and WAV media acceptance | **Deferred** as previously agreed |

This initial CI evidence covers compilation, synthetic WAV/PCM cache/trimming cases, WPF layout and settings validation, and published executable checks. The later local Windows evidence below independently verifies M4A playback, interaction, trimming, and listening for this refactor. Real MP3/WAV long-pause recordings remain deferred.

### 2026-11-23: feature refactor local Windows acceptance

User-supplied local results after the EOF cache-rounding fix at `f12777d`: **Core 45 passed / 0 failed; UI 79 passed / 0 failed; portable publish passed**. The user explicitly confirmed all nine manual acceptance items: first detection and persistent task toast; cache reuse without a second full scan; left/right silence navigation; three repeated trims with unique numbered names; independent `silence.json` for each output and unchanged source; Toast levels and dismissal; one-decimal/empty/minimum settings validation; playlist layout; and listening checks for intact speech and no obvious splice pops. Selecting an already-trimmed output correctly leaves scissors disabled when no compressible intervals remain.

Follow-up defect: clicking Confirm with an empty allowed-silence field initially showed **two identical warning Toasts**, because both the field's focus-loss handler and the Confirm button validated. The feature branch now skips focus-loss validation when keyboard focus moves to a dialog action button while preserving the button's own validation, red field border, and normal input-to-input focus validation. A WPF UI regression asserts exactly one warning for the combined focus-transfer-and-confirm sequence. **Post-fix real-window reconfirmation passed**: the user updated through `27ba90b`, ran `tools/Test.ps1 -Suite UI` (**45 Core assertions and 81 WPF UI assertions passed, 0 failed**), successfully published `dist/EnglishAudioPlayer.exe`, launched it, cleared the allowed-silence field, and confirmed that clicking Confirm shows **exactly one warning Toast**. Publishing safeguards passed (5/5). The feature branch's [Windows CI run #45105610515](https://github.com/chenjing2000/audioplayer/actions/runs/45105610515) also completed successfully. The user's nine other manual feature acceptance checks remain passed. Real MP3 and WAV long-pause acceptance remain deferred; the user-confirmed M4A work does not establish them.

### 2026-11-23: focused code-audit simplification

Branch: `feature/code-audit-simplification`, based on `main@67bf351`. This is a behavior-preserving cleanup following the Windows acceptance of PR #2, not a new media feature.

- Removed duplicate analysis/selection state resets, the legacy boolean notification adapter, and an unused Node.js workflow-test mock.
- CI now checks `main`, future `feature/**` pushes, and PRs targeting `main`. The unified workflow test harness also runs explicitly.
- The shutdown path waits for all still-running selection checks, including checks canceled by a newer selection. Added deterministic WPF coverage for an older outstanding task and rapid selection changes.
- Reused the already-validated audio fingerprint when loading cached ranges, removing redundant caller-side `AudioFileReader` opens without removing the store's before/after metadata verification.
- Retained the existing playback synchronization, PCM detection/trimming logic, small settings validation duplication, and the test-facing `analysisTask` field; no framework or new service layer was introduced.
- [Windows feature CI #45117264424](https://github.com/chenjing2000/audioplayer/actions/runs/45117264424) passed: Core **45/45**, WPF UI **82/82**, publishing safeguards **5/5**, portable EXE publish and published-app checks. A later CI run must also confirm the newly included workflow harness step before merge.

This cleanup has **not** yet received a new Windows physical-device listening check. Earlier user-confirmed M4A acceptance belongs to the preceding release; real long-pause MP3/WAV checks remain deferred.

### 2026-11-23: AudioPlayer unified v3 icons

- The toolbar now renders the approved v3 SVG geometry as six native WPF `DrawingImage` resources. The icons retain a `#26798A` circular background, white symbols, and transparent surroundings. Most white glyphs occupy approximately 50% of the circle height; the Stop square is intentionally 35%, and the Play triangle is shifted slightly right for balanced corner margins.
- Open, Play/Pause, Stop, Trim and Settings share 36×36 transparent hit targets with 28×28 visible images; hovering or pressing changes only the symbol's overall opacity, never its border or background.
- Play/Pause remains a single button with two states. The application icon is the approved leaf artwork in a compact multi-size ICO (16, 32, 48, 256 pixels); no SVG runtime dependency was added.
- WPF checks cover dimensions, resource colors, borderless button templates, and the existing playback UI. Automatic checks do not replace Windows taskbar/title-bar appearance review, hover feedback inspection, or real audio acceptance.

### 2026-11-23: merged icon refinements and current main verification

- [PR #4](https://github.com/chenjing2000/audioplayer/pull/4) introduced the approved v3 icon set, Windows-compatible leaf ICO and borderless 28 px vector toolbar; [PR #5](https://github.com/chenjing2000/audioplayer/pull/5) shifted the white Play triangle slightly right and resized the centered white Stop square to **35%** of the teal circle diameter.
- The user reported running the updated app on Windows, approved the icon appearance and authorized both merges. The user also showed local `main` and `origin/main` synchronized at **`8aa15b1`**, remote/local feature branches deleted, and a clean `git status --short`.
- [Windows post-merge main CI #45126656033](https://github.com/chenjing2000/audioplayer/actions/runs/45126656033) **passed**: **45 Core assertions, 111 WPF UI assertions**, workflow smoke checks, portable EXE publishing, and published-app verification. The WPF UI suite includes the Play position and Stop geometry regression assertions.
- This round changes only icon geometry and documentation. The prior real-M4A and playback listening evidence remains historical evidence; real MP3/WAV long-pause acceptance remains deferred.

For the latest handoff and next development baseline, read [HANDOFF](HANDOFF.md).

## Original FFmpeg AAC/M4A acceptance plan (historical, before implementation)

The following [approved design](specs/FFmpeg-M4A-Stream-Copy.md) checklist was written **before implementation**. For actual Windows verification and remaining gaps, see the dated PR #8 acceptance evidence below. The original checklist required:

- Launch, play, seek, detect, cache and MP3/WAV trim without FFmpeg; only M4A trimming checks the dependency. Missing EXE or its DLLs gives an **error Toast referring to `FFmpeg-Setup.txt`**, no MessageBox, fallback encoding, source alteration or committed output.
- AAC/M4A trim from multiple silence intervals with BtbN Windows x64 GPL Shared: `-c:a copy` without AAC re-encoding or 10 ms fades; reject unsupported M4A codecs. Verify AAC codec, sample rate, channels, packet timestamps, complete decoding, realistic duration/accumulated drift and listening at joins. Do not assume concat `inpoint`/`outpoint` is sample-exact.
- Real long recording with many cuts; Unicode/space/apostrophe file paths; source modification during trim; cancel/FFmpeg failure; existing destination collisions; temporary manifest/output cleanup and final output-cache behavior.
- Local build and published portable EXE each contain **only the user-facing setup TXT `FFmpeg-Setup.txt`** as added deployment documentation, not a copied repository README or bundled FFmpeg. Check the published runtime can find the TXT irrespective of working directory and that normal CI requires no downloaded FFmpeg.

Use a **pinned and recorded** FFmpeg build (version and package SHA-256) for actual Windows media testing; the downloadable `latest` URL is not a reproducible version. Earlier historical Windows acceptance does not cover the then-forthcoming change; the later PR #8 evidence is recorded below.

## Original feature-branch implementation note (before Windows acceptance)

The initial implementation branch added deterministic concat manifest and optional-dependency checks. At that time, Windows BtbN media/listening acceptance was still pending; later user-reported checks are recorded below. The M4A runtime dependency is intentionally not downloaded in ordinary CI.

## 2026-11-23: PR #8 Windows AAC/M4A stream-copy acceptance evidence

**Core feature accepted by user on Windows; PR #8 subsequently merged into `main@4b4afc6`.** The user ran the feature branch in Windows PowerShell 5.1 and reported these results:

| Test | User-reported outcome |
| --- | --- |
| Core assertions | **47 passed, 0 failed**; optional valid-MP3 metadata test skipped |
| WPF UI assertions | **113 passed, 0 failed**; optional audio-dependent MP3 UI test skipped |
| Publishing safeguards | **5 passed, 0 failed** |
| Published EXE checks | **4 passed, 0 failed**, including `FFmpeg-Setup.txt` |
| Portable EXE publish | **Succeeded** |

The user provided a screenshot of the expected missing-FFmpeg error Toast referring to `FFmpeg-Setup.txt`, an output M4A appearing in the audio list, and the Windows file sizes. **The user confirmed real M4A playback is normal and that joins after removed silences sound natural, without obvious jumps.**

The user's separate `ffprobe.exe` inspection of the actual IELTS listening source and new M4A output yielded:

| Field | Original M4A | Trimmed M4A |
| --- | ---: | ---: |
| `codec_name` | `aac` | `aac` |
| `profile` | `LC` | `LC` |
| `sample_rate` | `48000` | `48000` |
| `channels` | `2` | `2` |
| Audio `bit_rate` (bps) | `112781` | `117800` |
| Format `duration` (s) | `7142.442667` | `5468.970021` |
| Format `size` (bytes) | `102033724` | `81561519` |
| Format `bit_rate` (bps) | `114284` | `119308` |

Duration reduced by **1673.472646 seconds** (27 min 53.473 s) and file size by **20,472,205 bytes** (approximately **20.1%**). AAC-LC, 48-kHz and 2-channel parameters were unchanged; the increased *average* audio bitrate is compatible with removing highly compressible silence while retaining AAC packets. Stream copy is specified via `-c:a copy` in the code. The user's comparison with the old trimmed output found a nearly identical resulting duration; this alone does not prove packet-perfect timing.

**Not yet verified by Windows real-media acceptance:** defective/missing shared DLLs beyond the absent-executable Toast, active-process cancellation, source changes during trimming, detailed timestamp checks at joins, or every pathological path spelling. The exact downloaded BtbN build and ZIP SHA-256 were not supplied, so its binary version is **not** recorded as pinned. The earlier long-pause MP3/WAV listening work remains deferred. Final feature-branch Windows PR CI [#45164533459](https://github.com/chenjing2000/audioplayer/actions/runs/45164533459) passed. This is not a claim of an additional post-merge `main` CI run.


## 2026-11-23: toolbar refresh and result naming feature branch

Branch: `feature/toolbar-refresh-and-output-naming` (not merged). The new refresh button reuses `LoadFolderAsync`; it scans before closing the player, and a failed scan preserves the existing list/playback. Right-side button gaps are reduced from 10 to 5. Progress Slider height matches the 36-unit buttons. The refresh symbol is native WPF geometry derived from the user's SVG with the existing teal/white palette.

Trimming now generates `<stem>.new[ (N)].<ext>`, and the single result per audio lives at `results/<full filename>.silent_segments.json`. No legacy directory migration or deletion; original media and collision protections remain unchanged. Core test expectations and WPF UI assertions have been updated, including add/rename/delete/failed-refresh scenarios.

**Automated verification:** Feature branch CI [#45171773385](https://github.com/chenjing2000/audioplayer/actions/runs/45171773385) passed: **47 Core**, **124 WPF UI**, **5 publishing safeguards**, and **4 published-EXE checks**, with zero failures. Real audio-device playback was skipped on the hosted runner; manual taskbar icon, visual alignment and real-media acceptance remain outstanding. The original 47/113/5/4 checks above belong to PR #8 only. The repository ICO has teal/white 16/32/48 pixel layers plus a 256 pixel image, but pinned-taskbar icon caching and exact progress-track visual alignment require Windows checks. Real long-pause MP3/WAV listening and the previously listed FFmpeg edge cases remain deferred.
