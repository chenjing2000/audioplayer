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

Before release, verify WAV/MP3 using Windows Media Foundation and AAC-in-M4A using the separately installed FFmpeg stream-copy path on a real Windows 10/11 computer. Include PCM-generated fixtures for: no silence; short, equal and longer-than-allowed silence; multiple segments; silence at both ends; stereo; fractional seconds; 10-ms fading; and old/new settings validation. Check the generated output can be decoded, has the expected length (allowing compressed-codec padding), and does not overwrite an existing or source file. Test cancellation on shutdown and selection without focus. Automated tests alone do not establish that the system AAC/MP3 encoder is available; report real-device tests separately.

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

The historical [refactor specification](specs/Results-Cache-Toast-Refactor.md) originally required Core/UI/Full tests for the old nested `silence.json` store (replaced in PR #10 by `results/<full audio filename>.silent_segments.json`), metadata-based invalidation without SHA-256, automatic detection/cancellation, navigation from verified in-memory ranges, cache-based trimming without a second scan, numbered non-overwriting outputs, independent results for newly encoded media, Toast behavior, one-decimal settings validation, and playlist alignment.

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

Follow-up defect: clicking Confirm with an empty allowed-silence field initially showed **two identical warning Toasts**, because both the field's focus-loss handler and the Confirm button validated. The feature branch now skips focus-loss validation when keyboard focus moves to a dialog action button while preserving the button's own validation, red field border, and normal input-to-input focus validation. A WPF UI regression asserts exactly one warning for the combined focus-transfer-and-confirm sequence. **Post-fix real-window reconfirmation passed**: the user updated through `27ba90b`, ran `tools/Test.ps1 -Suite UI` (**45 Core assertions and 81 WPF UI assertions passed, 0 failed**), successfully published `dist/AudioPlayer.exe`, launched it, cleared the allowed-silence field, and confirmed that clicking Confirm shows **exactly one warning Toast**. Publishing safeguards passed (5/5). The feature branch's [Windows CI run #45105610515](https://github.com/chenjing2000/audioplayer/actions/runs/45105610515) also completed successfully. The user's nine other manual feature acceptance checks remain passed. Real MP3 and WAV long-pause acceptance remain deferred; the user-confirmed M4A work does not establish them.

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

Historical branch: `feature/toolbar-refresh-and-output-naming` (merged through PR #10). The new refresh button reuses `LoadFolderAsync`; it scans before closing the player, and a failed scan preserves the existing list/playback. Right-side button gaps are reduced from 10 to 5. Progress Slider height matches the 36-unit buttons. The refresh symbol is native WPF geometry derived from the user's SVG with the existing teal/white palette.

Trimming now generates `<stem>.new[ (N)].<ext>`, and the single result per audio lives at `results/<full filename>.silent_segments.json`. No legacy directory migration or deletion; original media and collision protections remain unchanged. Core test expectations and WPF UI assertions have been updated, including add/rename/delete/failed-refresh scenarios.

**Automated verification:** Feature branch CI [#45171773385](https://github.com/chenjing2000/audioplayer/actions/runs/45171773385) passed: **47 Core**, **124 WPF UI**, **5 publishing safeguards**, and **4 published-EXE checks**, with zero failures. Real audio-device playback was skipped on the hosted runner; manual taskbar icon, visual alignment and real-media acceptance were pending at this earlier branch check and subsequently accepted as recorded below. The original 47/113/5/4 checks above belong to PR #8 only. The repository ICO has teal/white 16/32/48 pixel layers plus a 256 pixel image, but pinned-taskbar icon caching and exact progress-track visual alignment require Windows checks. Real long-pause MP3/WAV listening and the previously listed FFmpeg edge cases remain deferred.


## 2026-11-23: full AudioPlayer engineering rename (feature branch PR #10)

The main project is now `AudioPlayer/AudioPlayer.csproj`; its assembly, C# namespaces, WPF `x:Class` / resource pack URI and published EXE are all `AudioPlayer`. The test project is now `tests/AudioPlayer.Tests/AudioPlayer.Tests.csproj`, with updated `InternalsVisibleTo`. The window title and default settings path are `AudioPlayer` / `AudioPlayer.settings.json`. Original media, old settings and old EXE files are not automatically deleted or migrated.

**Post-rename automated verification passed:** Final PR #10 Windows CI [#45181394625](https://github.com/chenjing2000/audioplayer/actions/runs/45181394625) passed on `038f021`. The published binary is `dist/AudioPlayer.exe`; hosted CI audio-device playback checks could not run. User-reported Windows testing before the final slider change: **47 Core, 127 WPF UI, 5 publishing safeguards and 4 published-EXE checks**, zero failures. User subsequently visually accepted the final progress slider `TranslateTransform Y=10` DIP offset and confirmed the app launches normally, settings save properly, taskbar icon updates, refresh functions as intended, and real M4A playback and trimming work. This does **not** establish automatic migration from a legacy settings filename; that is intentionally unsupported. Earlier PR #8 FFmpeg failure-path and real long-pause MP3/WAV acceptance remain deferred.

The first rename-only Windows CI found WPF generated-code error CS0426 because a class and namespace were both named `AudioPlayer`. The implementation class was consequently renamed to `PlaybackEngine` without changing its behavior; the verification evidence from the rename-only run was a failure, not a success.

## PR #10 baseline and completed PR #11 maintenance

PR #10 merged as `main@2be037f`; [post-merge Windows CI #45182410792](https://github.com/chenjing2000/audioplayer/actions/runs/45182410792) succeeded. The user's final Windows acceptance confirmed the refreshed toolbar, 10-DIP progress offset, settings, taskbar icon, playback and AAC/M4A trim. This is historical evidence, not a fresh test of subsequent cleanup changes.

The `feature/code-and-doc-cleanup` follow-up adds a deterministic Core regression ensuring that the folder scanner excludes `.stem.new.<32-digit-GUID>.wav/.mp3/.m4a` temporary trim outputs while retaining legitimate similarly named audio files. The release workflow also attaches `FFmpeg-Setup.txt` beside the EXE asset. The code-fix commit `6c3bc3c` passed [Windows CI #45186001792](https://github.com/chenjing2000/audioplayer/actions/runs/45186001792). The full follow-up revision `225e16f` passed [Windows CI #45186558898](https://github.com/chenjing2000/audioplayer/actions/runs/45186558898): Core, workflow smoke, WPF UI, portable publish and published-EXE steps all succeeded. Hosted audio-device-dependent checks are not a manual real-media acceptance. [PR #11](https://github.com/chenjing2000/audioplayer/pull/11) was squash merged as `main@c90afec`. [Post-merge Windows CI #45190102017](https://github.com/chenjing2000/audioplayer/actions/runs/45190102017) passed; real long-pause MP3/WAV and deferred FFmpeg fault/cancel checks remain unverified.

### 2026-11-23: PR #11 cleanup Windows acceptance

User-supplied Windows PowerShell 5.1 results for `feature/code-and-doc-cleanup`, after fetching/switching/pulling the branch:

| Check | Reported result |
| --- | --- |
| `tools/Test.ps1 -Suite Core` | **48 Core assertions passed, 0 failed**; 5 publishing safeguards passed; all 3 wrapper groups passed (0 failed, 0 skipped) |
| `tools/Test.ps1 -Suite UI` | **48 Core assertions and 127 WPF UI assertions passed, 0 failed**; 5 publishing safeguards passed; all 3 wrapper groups passed (0 failed, 0 skipped) |
| `tools/Publish.ps1` | Succeeded; `dist/AudioPlayer.exe` produced |
| `tools/Test-PublishedApp.ps1` | **4 checks passed, 0 failed, 0 skipped**, including FFmpeg setup TXT beside the published EXE |
| Reopen the folder while trimming | **User-confirmed passed:** unfinished temporary audio is excluded from the list |
| Refresh after trimming | **User-confirmed passed:** final output is listed and plays normally |
| Existing M4A playback/trimming and settings persistence | **User-confirmed passed**, no observed regressions |

The new `Folder scans ignore only in-progress trim audio` Core regression explicitly passed. The optional valid-MP3 metadata test and MP3-dependent UI checks printed **SKIP** because no MP3 test input was supplied; that is distinct from the test wrapper's zero skipped *groups*. The user did not report executing `-Suite Full -AudioFile` in this run. Real MP3/WAV long-pause listening and selected FFmpeg incomplete-DLL/cancellation/source-mutation failure cases remain deferred. A future tagged GitHub Release must still be inspected to verify that both downloadable assets actually appear, despite the local TXT publishing test passing.

Revision `e392114` passed [Windows PR CI #45187026289](https://github.com/chenjing2000/audioplayer/actions/runs/45187026289). The final pre-merge revision `3cce00d` passed [Windows PR CI #45189289382](https://github.com/chenjing2000/audioplayer/actions/runs/45189289382); one simultaneous push check initially failed a Toast UI queueing assertion and [passed on retry](https://github.com/chenjing2000/audioplayer/actions/runs/45189280854). [PR #11](https://github.com/chenjing2000/audioplayer/pull/11) was squash merged into `main@c90afec`. [Post-merge Windows CI #45190102017](https://github.com/chenjing2000/audioplayer/actions/runs/45190102017) succeeded. The user verified their local `main` matches `origin/main`, the working tree is clean, and both local and remote feature branches have been removed. No new real-media or failure-path testing is claimed by this documentation update.

## Windows acceptance — PR #15

The user explicitly confirmed all ten Windows manual acceptance checks passed for the recursive persistent playlist feature:

1. Recursive discovery of supported audio files in nested directories.
2. Filename extensions hidden; complete path available in row tooltip.
3. Natural relative-path sorting.
4. Playlist JSON created beside the executable.
5. Restart restores saved playlist without automatically playing audio.
6. New files added while closed appear after Refresh, not merely startup restoration.
7. Deleted saved files are removed after restarting.
8. Trimmed audio in a subdirectory appears in the table and saved JSON.
9. Missing root on Refresh clears the playlist and saved root path.
10. Existing playback, silence navigation and settings continue working.

User-reported manual acceptance is distinct from hosted automated verification. The accepted implementation at `9479bca` passed [Windows PR CI #45305530107](https://github.com/chenjing2000/audioplayer/actions/runs/45305530107) and [push CI #45305513723](https://github.com/chenjing2000/audioplayer/actions/runs/45305513723). The earlier local log was from the pre-feature version after a failed Git fetch, so its counts are not attributed to this feature. No new local automated counts or previously deferred real-media/failure-path coverage are claimed. At this stage merge authorization was still pending; the final merge is recorded below.

### PR #15 follow-up: tooltip and readable playlist JSON

After the initial ten manual checks passed, the user requested parent-directory-only tooltips (wrapped, maximum main-window width minus 48 DIP, 8 DIP rounded corners) and literal Chinese characters in UTF-8 playlist JSON. Existing escaped JSON remains readable. Core regression assertions cover the revised tooltip text and JSON round-trip. Both [PR CI #45328234578](https://github.com/chenjing2000/audioplayer/actions/runs/45328234578) and [push CI #45328228807](https://github.com/chenjing2000/audioplayer/actions/runs/45328228807) passed at `e42547c`. The user supplied a checked screenshot confirming all six follow-up Windows manual checks:

1. Parent directory displayed without media filename.
2. Long directory paths wrap across multiple lines.
3. Tooltip has rounded corners.
4. Maximum tooltip width adjusts after main-window resizing.
5. Chinese characters are directly readable in playlist JSON.
6. Previously saved Unicode-escaped playlists still load normally.

Together with the initial ten checks, sixteen user-reported manual acceptance checks have passed. No new local automated test counts are claimed. At this stage merge was deferred at the user's request.

### PR #15 WindowsAppPlaybook review remediation

Added targeted regression checks for locked playlist save, missing/non-file saved entries, directory junction exclusion, cancellation, preservation of playlist JSON during publishing and renamed published EXE startup restoration. Removed obsolete metadata-title checks while retaining album/duration and filename coverage. Prior sixteen manual checks apply to the earlier code revisions. Remediation revision `db9fc41` passed [PR CI #45329769913](https://github.com/chenjing2000/audioplayer/actions/runs/45329769913) and [push CI #45329762960](https://github.com/chenjing2000/audioplayer/actions/runs/45329762960).

The user explicitly confirmed all seven focused Windows acceptance checks passed:

1. Open and refresh a directory containing multiple levels of subdirectories.
2. Restart and restore the saved playlist correctly.
3. Close during a large recursive scan and preserve the previous saved playlist.
4. Reopen the current root during nested-audio trimming; the completed output appears in the table and JSON.
5. Switch tracks; playback and pause indicators update correctly.
6. Tooltip appearance and readable Chinese playlist paths remain unchanged.
7. Republish while preserving the existing playlist and settings.

This records user-reported manual acceptance, not new local automated test counts. The sixteen prior checks and seven focused checks total 23 accepted checks across successive revisions, not a complete rerun on one commit. Dedicated real Windows directory-access-denial testing and previously deferred real-media/failure-path cases are not claimed by this acceptance. At this stage PR #15 remained unmerged at the user's request.

### PR #15 pronunciation directory exclusion

Added a Core regression for nested exact-name `audio` exclusions, case-insensitive matching, similarly named directories, ordinary `audio.mp3` files, saved-path filtering, and explicit root selection. Revision `18e6fe0` passed [PR CI #45332081052](https://github.com/chenjing2000/audioplayer/actions/runs/45332081052) and [push CI #45332074251](https://github.com/chenjing2000/audioplayer/actions/runs/45332074251).

The user explicitly confirmed all four focused Windows manual acceptance checks passed:

1. Directories named exactly `audio` are excluded at every subdirectory level.
2. Similarly named directories remain included.
3. Previously saved vocabulary pronunciation files disappear from the playlist after restarting.
4. Refresh does not reintroduce excluded pronunciation files.

Together with the earlier 23 checks, this records 27 user-reported manual acceptance checks across successive revisions, not a full rerun on the latest commit. No new local automated test counts are claimed. Previously unverified access-denial and real-media failure scenarios remain unchanged. At this stage PR #15 remained unmerged.

### PR #15 final merge and post-merge verification

After the user authorized merging, final feature head `10f2aee` passed [PR CI #45332463717](https://github.com/chenjing2000/audioplayer/actions/runs/45332463717) and [push CI #45332456815](https://github.com/chenjing2000/audioplayer/actions/runs/45332456815). [PR #15](https://github.com/chenjing2000/audioplayer/pull/15) was squash merged as `main@7cb288e` on 2026-11-24. [Post-merge Windows CI #45332633926](https://github.com/chenjing2000/audioplayer/actions/runs/45332633926) completed successfully. The remote branch listing now contains only `main`; local synchronization and local branch cleanup have not been independently confirmed. The 27 user-reported manual acceptance checks are cumulative across four revisions, not a complete manual rerun on the merge commit. Previously unverified directory-access-denial and real-media/failure-path cases remain outstanding. No tagged release was created by this merge.

## Windows acceptance — PR #16

The user explicitly confirmed all 16 Windows manual acceptance checks passed on 2026-11-24. The accepted application revision `ef17fe4` passed [Windows PR CI #45359288782](https://github.com/chenjing2000/audioplayer/actions/runs/45359288782) and [push CI #45359259023](https://github.com/chenjing2000/audioplayer/actions/runs/45359259023). Merge is explicitly deferred at the user's request.

1. Settings window measures 480 × 390 DIP.
2. Both parameter sections are properly aligned, without clipped text.
3. New step parameters default to 5 and 4 seconds.
4. Both input boxes accept only ASCII digits, with a maximum length of two.
5. Invalid pasted values and empty confirmed values are rejected.
6. Modified steps persist after restarting AudioPlayer.
7. Restore Defaults and Cancel behave correctly.
8. Ctrl + Left seeks backward by the configured step.
9. Ctrl + Right seeks forward by the configured step.
10. Seeking correctly handles the beginning and end of audio.
11. Seeking preserves playing or paused status, except normal end-of-media behavior.
12. A step of 0 produces no movement.
13. Ctrl + Left/Right works regardless of the focused main-window control.
14. Space toggles playback regardless of the focused main-window control.
15. Existing Left/Right silence navigation remains functional.
16. Changing only the step parameters does not trigger silence reanalysis.

These are user-reported Windows manual results, separate from hosted automated CI. No new local automated assertion counts or previously deferred failure-path coverage are claimed.

### PR #16 follow-up: configurable folder exclusion

At `885cf2c`, the user-provided Windows log reported 54 Core passes, 5 publishing safeguard passes, successful publish and 4 published-app checks. The UI suite stopped after 94 passes and one failure because the invalid-folder test left a warning Toast in the shared dialog. Test-only correction `398b33b` was then locally verified: 54 Core and 148 UI checks passed, zero failures; 5 publishing safeguards passed. Optional valid-MP3 metadata and audio-dependent UI checks were skipped because no local MP3 was available.

The user confirmed 15 of 16 manual checks passed on the folder-exclusion implementation:

1. Empty default.
2. Empty list includes `audio` folders.
3. Mixed ASCII separators and whitespace normalize correctly.
4. Chinese separators and folder names work.
5. Duplicate names and empty entries are removed.
6. Invalid Windows folder names are rejected.
7. Excluded subtrees are skipped recursively.
8. Exact case-insensitive matching preserves similarly named folders.
9. Explicit root is exempt.
10. Saving settings does not automatically refresh or interrupt playback.
11. Refresh applies new exclusions.
12. Settings persist after restart.
13. Startup restoration respects exclusions.
14. Cancel and Restore Defaults work.
15. Existing playback and shortcuts remain functional.

The remaining layout check failed: the new exclusion controls overlap the explanatory text. The layout correction retains only the allowed-silence explanation, reserves space for it, and adds a geometry assertion. Focused Windows visual acceptance and CI for that correction are pending. The 16 earlier seek-shortcut checks and these 15 checks are cumulative across revisions, not a full rerun on the new layout. No merge authorization.

The user subsequently requested a 480 × 420 DIP settings dialog. Update the height regression while retaining the single-help-line and rendered geometry assertions. Prior 480 × 390 acceptance is historical; the revised layout requires focused visual acceptance. No application logic changes.

The user subsequently confirmed that the revised settings window at `9aab81f` is acceptable, with a screenshot showing the single explanatory sentence and the exclusion row separated from the buttons without visible overlap. The outstanding layout check is accepted. All 16 folder-exclusion checks and the earlier 16 seek-shortcut checks are now accepted across successive revisions (32 total), not a complete rerun on one revision. No new local automated test counts are claimed. Merge remains deferred.

### PR #16 follow-up: playlist row appearance

User-reported intermittent left-edge strips emerge during scrolling and can disappear after Refresh. The first correction removes the unused row-header area and row border, preserving virtualization and existing row styling. The synthetic 120-file UI regression checks header visibility, border thickness and alternating backgrounds after distant scroll jumps. This is structural coverage, not pixel-level proof that the reported artifact is resolved. CI and focused Windows visual acceptance pending.

The user explicitly confirmed all six scrolling checks passed at `c3bafcf`: initial appearance, slow scrolling, rapid scrolling and scrollbar dragging, refresh and scrolling, resizing and scrolling, and preservation of row colors, playback indicators, selection and tooltips. The supplied local log reports 54 Core passes, 36 UI passes before one failure at row 119, 5 publishing safeguards passed and successful publishing. Optional MP3 metadata tests were skipped. [Push CI #45375690419](https://github.com/chenjing2000/audioplayer/actions/runs/45375690419) and [PR CI #45375701189](https://github.com/chenjing2000/audioplayer/actions/runs/45375701189) failed. The combined assertion does not identify which condition failed. The test-only follow-up separates conditions and checks background against actual WPF alternation index and adjacent realized rows rather than assuming absolute index parity after virtualized jumps. Corrected CI and local UI rerun pending. Manual checks total 38 across successive revisions, not a single full rerun. No merge authorized.

Local Windows rerun at `9cdf6d1` passed 54 Core checks, 191 UI checks and 5 publishing safeguards, zero failures. Optional MP3 metadata and audio-dependent UI checks were skipped. Row 119 had actual WPF alternation index 0, as did row 1 after a jump; row 0 had index 1 after scrolling back. All tested realized adjacent rows alternated and all tested headers/borders were absent. This confirms the original absolute-index parity assumption was incorrect. [pull_request CI #45376237012](https://github.com/chenjing2000/audioplayer/actions/runs/45376237012) success; [push CI #45376228289](https://github.com/chenjing2000/audioplayer/actions/runs/45376228289) in_progress. No application source changes were necessary for the test correction. The 38 user-reported manual checks remain cumulative across revisions; merge remains deferred.

### PR #16 follow-up: playback modes

Core tests cover mode persistence, default and invalid enum rejection, next-index policy including empty/single-file lists. UI tests cover cycling, icons, toolbar alignment and settings preservation/reset staging. Synthetic WAV integration when a waveOut device exists covers natural completion, repeat, sequential stop, wraparound, manual Stop and seek-to-end. Hosted CI and manual playback acceptance pending; previous 38 manual checks remain historical cumulative evidence.

At `34f6972`, user confirmed the four modes cycle correctly and all four playback behaviors work as expected, with toolbar screenshot. This is focused functional acceptance, not explicit confirmation of all 20 detailed checks. Local application published successfully; 5 publishing safeguards and 4 published-app checks passed. Core/UI test project compilation failed with CS0136 at UiChecks.cs:587 (duplicate local `first`). Test-only fix renames synthetic mode fixture paths. Corrected automated rerun pending; no merge authorized.


#### Playback mode final acceptance

The user explicitly confirmed all 20 playback-mode Windows manual acceptance checks passed on 2026-11-24. Local Windows rerun at `3321875` passed 55 Core checks, 225 UI checks and 5 publishing safeguards, zero failures. The actual waveOut synthetic WAV playback integration ran and passed (Play One, Repeat One, Sequential, Repeat All, manual Stop and seek-to-end). Optional valid-MP3 metadata and MP3-dependent UI checks were skipped. Application revision remains `34f6972`; earlier publishing and published-app checks passed at that revision. [pull_request CI #45379441565](https://github.com/chenjing2000/audioplayer/actions/runs/45379441565) success; [push CI #45379433218](https://github.com/chenjing2000/audioplayer/actions/runs/45379433218) in_progress. Cumulative manual acceptance is 58 checks (16 seek + 16 exclusion + 6 scrolling + 20 playback modes), across successive revisions, not one complete manual rerun. Merge remains explicitly unauthorized.

1. PASS — Button placement between Refresh and Settings.
2. PASS — Four icons clear and visually consistent.
3. PASS — Cycle A → B → C → D → A.
4. PASS — Tooltip identifies each mode.
5. PASS — Play One stops at completion.
6. PASS — Repeat One restarts current file.
7. PASS — Sequential advances to next file.
8. PASS — Sequential stops at last file.
9. PASS — Repeat All advances and wraps.
10. PASS — Single-file Repeat One and Repeat All.
11. PASS — Changing mode during playback does not interrupt.
12. PASS — Manual Stop prevents continuation.
13. PASS — Pause and Resume in all modes.
14. PASS — Manual seek to end does not continue.
15. PASS — Automatic next row scrolls into view.
16. PASS — Refresh stops playback and retains mode.
17. PASS — Mode persists on restart without autoplay.
18. PASS — Ordinary settings retain mode; confirmed defaults reset it.
19. PASS — Existing shortcuts, exclusions and scrolling still work.
20. PASS — Unreadable next file stops and reports error.

### PR #16 review remediation

Read-only review at `20b9e84` followed by user-authorized corrections: state is published before audio startup; zero-duration media stops with an error; folder selection and settings share modal protection; actual user seeks and Stop supersede pending completion; scan avoids redundant exclusion filtering and dialog validation reuses parsed names. Tests exercise real Stop and shortcut routing, shared modal deferral (simulated guard, not native folder picker), unreadable/empty next media and very short WAV completion. Settings class names retained. Documentation consolidated; previous 58 manual checks are historical, new CI and focused Windows acceptance pending.
