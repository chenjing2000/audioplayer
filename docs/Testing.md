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


#### Review remediation manual acceptance

The user explicitly confirmed all 12 focused WindowsAppPlaybook remediation manual acceptance checks passed on 2026-11-24 at application revision `dbc2886`. Cumulative manual acceptance is 70 checks across successive revisions (16 seek, 16 exclusions, 6 scrolling, 20 playback modes, 12 remediation), not a complete rerun of all checks at one revision. Hosted verification: [pull_request CI #45380365612](https://github.com/chenjing2000/audioplayer/actions/runs/45380365612) success; [push CI #45380356119](https://github.com/chenjing2000/audioplayer/actions/runs/45380356119) success. A new local automated test/publishing log has not been supplied for this revision; previous 55 Core and 225 UI passes at `3321875` remain historical evidence. Merge remains explicitly unauthorized.

1. PASS — Very short pronunciation audio completes without incorrect playing indicator.
2. PASS — All four playback modes continue working.
3. PASS — Stop near natural completion prevents continuation.
4. PASS — Backward seek near end does not advance to another file.
5. PASS — Forward seek to end stops without continuation.
6. PASS — Open Folder dialog defers advancement.
7. PASS — Cancel Open Folder processes valid pending continuation.
8. PASS — Confirm new folder cancels old playlist continuation.
9. PASS — Natural completion during Settings is handled correctly after closing.
10. PASS — Empty or unreadable next audio stops with error.
11. PASS — Folder exclusions work for opening, refresh and restoration.
12. PASS — Closing during repeated or sequential playback releases resources.


### PR #16 final merge and post-merge verification

Final tooltip revision `c3a4c21` removes the visible `播放模式：` prefix while preserving accessible names and updates UI assertions. Both [push CI #45382457033](https://github.com/chenjing2000/audioplayer/actions/runs/45382457033) and [PR CI #45382467366](https://github.com/chenjing2000/audioplayer/actions/runs/45382467366) passed. No separate manual tooltip result was supplied. The user explicitly authorized merging on 2026-11-24; [PR #16](https://github.com/chenjing2000/audioplayer/pull/16) was squash merged into `main@af27658`. [Post-merge Windows CI #45382847185](https://github.com/chenjing2000/audioplayer/actions/runs/45382847185) passed. Remote branch listing verified only `main` remains. Local branch cleanup/synchronization and a new local automated/publishing log for the remediation revision have not been independently confirmed. The 70 user-reported manual checks are cumulative, not a full rerun on the merge commit. Earlier pending/deferred merge statements above describe historical stages. Optional MP3 and previously deferred real-media/failure-path checks remain unverified; no release tag was created.

## PR #17 — Script companion Windows acceptance (pending)

Application revision `14a354a`, 2026-11-24: [push CI](https://github.com/chenjing2000/audioplayer/actions/runs/45398456432), [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45398466410), 58 Core + 221 UI + 5 publishing safeguards + 4 published EXE checks passed; zero failures. Workflow smoke also passed. Six script wheel playback assertions explicitly skipped because hosted runner has no WaveOut device; other audio-device and optional MP3 checks also skipped. This is not evidence of real WebView2 interaction. No manual result yet.

On Windows, close running AudioPlayer, switch to the feature branch and pull fast-forward only; do not discard local changes. Run UI, publish and published checks as in HANDOFF. Check the actual log contains the six wheel playback assertions and no WaveOut skip. Use a separate copied media folder with one correctly matched ReadArticles Script JSON, including multiple pages of text, timing gaps and null intervals. Use disposable copies for malformed/missing cases and test configuration; do not modify originals. Record commit, test logs, pass/fail per ID, environment/DPI and any skipped scenarios. Do not merge before separate authorization.

| ID | Windows manual acceptance check |
| --- | --- |
| A01 | Icon order, consistent shape, tooltip show/hide, minimum main width without overlap |
| A02 | Opens right of main, initial 400 DIP width and same height |
| A03 | Minimum 200 × 300 DIP; independent resize |
| A04 | Main movement and horizontal resize follow right edge |
| A05 | Main vertical resize preserves independent script height |
| A06 | Hide, reopen and close button; native main minimize closes reader first, restore leaves closed, main close |
| A07 | Main maximize and insufficient space keep reader accessible; reader minimize/maximize disabled, resize remains enabled |
| A08 | Secondary monitor and mixed DPI; report unavailable hardware as skipped |
| B01 | Arbitrary JSON filename, only current loaded audio parent searched |
| B02 | Ignore unrelated JSON and nonmatching selected row |
| B03 | Missing script message; audio continues |
| B04 | Recognizable Script with invalid schema/interval shows error; audio continues |
| B05 | Empty/nonempty title and original punctuation/text |
| B06 | Null/unmatched units and empty timed_units remain visible without coloring |
| B07 | Switch tracks/folders without stale original text |
| B08 | Source JSON unchanged; no auto timestamp repair |
| C01 | Progressive partial-word color follows actual playback |
| C02 | Wrapped timed unit retains one interval |
| C03 | Natural top boundary; continuous follow begins when scroll range permits |
| C04 | Current visual line near center; interpolated motion between timed visual lines, including gaps |
| C05 | Bottom natural scroll limit, short document no extra padding |
| C06 | Continuous automatic scrolling within and across lines, no per-line dwell |
| C07 | Pause/resume, forward/backward seek, Stop and natural end |
| C08 | Resize/font changes recalculate layout without audio seek |
| D01 | Wheel at 25% of original displacement and native scrollbar both directions directly scroll, no auto animation |
| D02 | Rapid wheel input previews latest continuous target on main slider; small moves within a visual line change target |
| D03 | One committed seek after approximately 150 ms idle |
| D04 | Playing resumes, paused/stopped behavior remains consistent |
| D05 | Top/bottom first/last valid anchor; interpolation across untimed text, no valid anchors/no movement does not seek |
| D06 | Double-click text does not seek; left selection/copy and middle press behavior |
| D07 | Hide/close, Stop, Space and main slider/keyboard supersede pending seek correctly |
| D08 | Switch track, refresh, open/settings dialogs and close app during pending navigation |
| E01 | Modern borderless audio/subtitle tabs, rounded selectors, original audio controls and footer fit |
| E02 | All fonts and font sizes, missing font fallback |
| E03 | Line height, paragraph spacing and first-line indentation |
| E04 | Five borderless swatches per group; one checkmark, confirmed colors apply to reader, no sample preview |
| E05 | Cancel leaves saved appearance unchanged; OK persists |
| E06 | Restart restores settings; Restore Defaults stages values until confirmation |
| E07 | Older config defaults; invalid config retained and reported |
| E08 | Appearance edits do not trigger silence reanalysis; shortcuts and copying remain usable |
| F01 | All four playback modes, repeat reset and next-track script |
| F02 | Long script performance and responsiveness |
| F03 | Resize and scroll during playback; no unexpected seek |
| F04 | Hidden reader and reopen synchronize without stale playback |
| F05 | WebView2 unavailable/failure does not stop audio (only isolated safe environment) |
| F06 | Published single EXE opens actual script, runtime cache beside EXE |
| F07 | Existing settings/playlist/media and trim outputs retained |
| F08 | Existing playback, silence navigation, scan and trim regression smoke |

No item above is marked passed until user reports actual Windows results. Timing estimates based on observation are qualitative unless instrumented. Real device failure scenarios or mixed-DPI hardware unavailable locally must be marked skipped, not passed.

### Local Windows result at 0ada0d9 (2026-11-24)

User-supplied PowerShell log: 58 Core, 260 UI, 5 publishing safeguards, 4 published EXE checks passed; optional MP3 skipped. All seven wheel-related assertion lines passed (the prior skip label counted six grouped scenarios). Manual launch of the published EXE displayed the companion but reported “原文页面加载失败。”; reader acceptance failed and further manual checks are pending. The navigation implementation is corrected to serve the embedded reader through a fixed local HTTPS resource response, as in ReadArticles, with exact navigation/message source checks and diagnostic errors. A real WebView2 ready/render smoke test now runs before the audio-device gate when the runtime is available. This correction still requires CI and local re-verification.

### Local Windows result at 6789ffd (2026-11-24)

Both [push CI](https://github.com/chenjing2000/audioplayer/actions/runs/45402314948) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45402326844) passed: 58 Core, 223 UI, 5 publishing safeguards and 4 published EXE checks (290 total, zero failures; workflow smoke also passed). Both real WebView2 handshake/render assertions and stabilized Toast checks ran successfully. Hosted WaveOut-dependent assertions remain skipped.

User-supplied Windows PowerShell log at `6789ffd`: 58 Core, 262 UI, 5 publishing safeguards and 4 published EXE checks (329 total, zero failures). Both actual WebView2 assertions and all seven wheel playback assertion lines passed. Optional MP3-specific cases skipped. Temporary WebView2 test cache was still in use during cleanup (nonfatal); no production data loss reported. Publish and published EXE checks passed. The published EXE screenshot and user report confirm the earlier reader initialization failure is resolved: real Script JSON loads, played/unplayed coloring is visible, direct wheel scrolling and double-click update playback progress.

User explicitly confirmed all 10 focused manual checks on 2026-11-24:

1. Continuous playback with progressive partial-word coloring.
2. Automatic active visual-line centering.
3. Smooth automatic line transition (visual assessment, not instrumented timing).
4. Wheel scrolling during playback pauses, seeks and resumes.
5. Wheel scrolling while paused seeks without resuming.
6. Top and bottom scrolling boundaries.
7. Appearance changes persist after restart.
8. Switching audio files loads corresponding scripts.
9. Hide and reopen during playback.
10. Space, Left/Right and Ctrl+Left/Right in reader.

These results establish core reader acceptance, not completion of the full 48-item matrix above. Remaining compound or unconfirmed cases include exact window geometry and monitor/DPI behavior, missing/invalid and untimed script fixtures, precise debounce/animation timing if required, all cancellation paths, settings option/cancel/default coverage, all playback modes with the reader visible, long-script performance, missing runtime isolation, cache placement and legacy regression/data-protection checks not separately confirmed. Do not infer full passes from partial evidence. No merge or release authorization.

### Post-acceptance source review remediation (pending)

The follow-up code review changes add indexed active-interval painting (including overlaps and rewind/reset), conditional browser resynchronization, modal playback-intent restoration, device-error cancellation, cooperative script loading cancellation and failed-reader recreation. New browser assertions verify overlapping/out-of-order intervals, rewind and reset; a local WAV/UI assertion covers Settings during a pending seek. Test-cache cleanup now retries for a bounded interval, and the skipped wheel assertion label correctly counts seven plus the new Settings check. Earlier user acceptance applies to `6789ffd`, not this new revision. Re-run CI and focused Windows tests; measure audio presentation timing and multi-monitor DPI before any further engine/geometry changes.

### Owner minimize/restore regression (pending)

At `f900adb`, [push CI](https://github.com/chenjing2000/audioplayer/actions/runs/45405767173) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45405781370) passed 58 Core, 226 UI, 5 publishing safeguards and 4 published EXE checks (293 total). Audio-dependent cases skipped on hosted runner. User reported 7/7 focused remediation checks passed: Settings cancellation during wheel preview, rapid wheel synchronization, backward coloring, Stop reset, long Cambridge IELTS responsiveness, rapid track switches, and smooth follow/coloring. A06 remains failed on visual minimization: residual reader shadow in upper-left desktop. Latest local automated log not supplied. Correction explicitly hides the owned reader on minimize and restores only if previously shown, preserving browser instance and dimensions; automated checks verify native visibility, same reader on restore, and hidden-reader preference. Repeat minimize/restore with visible reader while playing and paused, with hidden reader, and check taskbar/desktop for residual shadow; verify no unintended playback change. Visual artifact resolution is not established by native visibility checks alone. No merge authorized.

### Smooth automatic line transition (pending)

Automatic visual-line follow now uses a 180 ms half-cosine animation rather than 50 ms; the existing frame-driven interpolation, progressive coloring and 150 ms wheel-seek debounce remain unchanged. A browser assertion drives synthetic frame timestamps and checks intermediate displacement and the exact final target. That implementation was superseded by continuous time-driven follow below; no visual acceptance was recorded. Recheck C06, C07, D01–D04, F02 and rapid line changes; previous manual passes do not establish acceptance of the new duration. No merge authorized.

### Continuous audio-time-driven scroll (accepted, focused Windows checks)

The user requested continuously moving text rather than per-line animations with dwell. The reader now interpolates document Y between strictly increasing visual-line timestamps and uses requestAnimationFrame with bounded 50 ms clock extrapolation between host samples. Natural scroll boundaries, paused/reset states, direct wheel/150 ms debounced seek, and confirmed-clock coloring are retained. New synthetic WebView2 assertions cover intermediate offsets across line boundaries, irregular timestamps, limits and frame lifecycle. Recheck C03–C08, D01–D07 and F02/F03: uninterrupted in-line motion, speed changes with line duration, pause/resume, seek, resize, wheel interaction and long recordings. The user subsequently confirmed the eight focused Windows checks below; no full-matrix acceptance or merge authorization.

#### User-reported Windows acceptance of continuous scrolling (2026-11-24)

Baseline: continuous-scroll revision `8cc723e` (user did not provide a separate local SHA or automated test log). The user confirmed all eight focused manual checks:

1. Normal playback continuously scrolls upward without obvious per-line dwell.
2. Scroll speed responds to speech-rate changes.
3. No obvious jumps across visual lines.
4. Scrolling remains synchronized with progressive coloring.
5. Pause stops scrolling immediately; resume continues scrolling.
6. Manual and automatic scrolling hand off correctly.
7. Forward/backward seek and Stop behave correctly.
8. Long IELTS listening material plays smoothly.

The user requested about half the manual wheel navigation span. The subsequent change scales normalized wheel displacement to 50% in both directions, without changing the center-line timestamp selection, 150 ms debounce, or automatic-follow algorithm. Because seek targets are discrete visual lines and speech timing varies, the time span is approximate rather than exactly halved. A synthetic WebView2 assertion checks pixel/line/page wheel modes and direction. Fresh CI and focused Windows wheel sensitivity regression remain pending; prior acceptance does not establish the new wheel behavior. The separate minimize-shadow visual check remains unresolved. No merge authorized.

#### Further reduction of wheel sensitivity (pending)

The user reported the 50% setting at `ba1f351` still moves too far and requested another 50% reduction. The normalized wheel displacement is now 25% of the original (half of the preceding setting). Updated the browser assertion for pixel, line and page wheel modes; continuous follow, visual-line seek selection and 150 ms debounce remain unchanged. Fresh CI and local wheel acceptance pending. No merge authorized.

### Reader controls and close-before-minimize (pending)

User requested no reader minimize/maximize buttons (retain resize/close), native scrollbar movement to preview/seek like wheel, removal of double-click seek, and actual reader close before main minimize. Native SC_MINIMIZE closes/disposes reader synchronously before default minimize; StateChanged closes as fallback for programmatic/shell paths. Restoring owner leaves reader closed until the user opens it. Scroll events distinguish actual external movement from programmatic scrollTo (including clamping/rounding), reuse center-line preview and 150 ms debounce; automatic scroll does not seek. Wheel remains 25%. New WebView2 and native lifecycle assertions are pending CI and Windows visual acceptance, including real scrollbar drag, track clicks, ongoing playback and residual shadow. No merge authorized.

### Reader state and trajectory refactor (implementation and pre-acceptance evidence)

At `6917294`, [Feature validation](https://github.com/chenjing2000/audioplayer/actions/runs/45430367570) completed successfully. This is pre-refactor evidence only; no new Windows manual log was supplied. Source review identified separate quantized manual and continuous automatic mappings, four host navigation-state fields with a separate committing path, browser preview overwriting authoritative playback, cancellation without an actual-state snapshot, and stale-window callbacks. Refactor uses one pending transaction, the existing serialized host command gate, one epoch-tagged playback snapshot protocol, separate preview coloring, matching committed target for offset preservation, and one monotone timeline used in both directions. The reader measures timed rectangles only. Existing wheel scale (0.25), 150 ms debounce, natural bounds, real word timestamps for coloring, no double-click seek and owner-minimize close remain. Manual behavior deliberately changes from line-start quantization to continuous interpolation; untimed text between valid anchors can be traversed without fabricating word timings. No timed anchors means no seek.

The exact revised inline JavaScript was executed in a simulated DOM/clock environment: all 401 forward/inverse round-trip samples, empty-trajectory handling, preview isolation, equal-position cancel, stale epoch rejection, matching commit offset and superseded commit reconciliation passed. This is not WebView2/Windows execution. Updated browser assertions exercise continuous sub-line external scroll, programmatic-scroll exclusion, same-position cancellation, stale messages and committed-target matching. Audio-device integration includes consumed-transaction repeated commit. Fresh CI and local Windows full suite are required. Focus D01–D08, C01–C08, A06–A08, F01–F06; test a real scrollbar drag, rapid interaction with Space/Stop/main slider/Settings, minimize during preview/commit, reopen and track changes, long scripts, untimed text, short document and device failure where feasible. Native scrollbar attribution still uses observed scroll offsets; physical input and resize/DPI behavior must be verified locally. No merge authorized.

### Refactored reader Windows acceptance (2026-11-25)

Verified application revision: `b662cba` (user-provided PowerShell log confirms local HEAD and origin/feature/script-companion-window matched after a fast-forward from `2a22de9`; working tree was clean before pull). [Feature validation CI #45434378532](https://github.com/chenjing2000/audioplayer/actions/runs/45434378532) completed successfully; verify job passed.

User-provided local Windows PowerShell 5.1 results:

| Check | Passed | Failed |
| --- | ---: | ---: |
| Core | 58 | 0 |
| UI (including real WebView2 and WAV navigation) | 276 | 0 |
| Publishing safeguards | 5 | 0 |
| Published EXE checks | 4 | 0 |
| Total | 343 | 0 |

`Test.ps1 -Suite Full` passed (3 top-level groups, 0 failed, 1 skipped). PowerShell source/syntax checks passed. `Publish.ps1` successfully built and published the win-x64 .NET 10 executable to dist; `Test-PublishedApp.ps1` passed 4/4. Optional real MP3 playback, valid-MP3 metadata and MP3-dependent UI checks were skipped because the requisite local MP3 fixture was unavailable. The isolated temporary WebView2 test cache remained in use during cleanup (nonfatal; no claim that cleanup succeeded).

User explicitly confirmed all 22 focused manual regression checks at this revision:

- [x] Continuous smooth scrolling without obvious dwell.
- [x] Progressive word coloring synchronized to audio.
- [x] Long IELTS listening material plays smoothly.
- [x] Pause/resume behaves correctly.
- [x] Window resizing and font changes do not trigger unintended seeks.
- [x] Fine-grained wheel navigation.
- [x] Rapid repeated wheel navigation stays synchronized.
- [x] Right scrollbar dragging updates playback progress.
- [x] Clicking the scrollbar track updates playback progress.
- [x] Manual scrolling pauses and restores prior playback state.
- [x] Double-click text does not seek.
- [x] Space during scrolling.
- [x] Stop during scrolling.
- [x] Arrow shortcuts during scrolling.
- [x] Main slider immediately after scrolling.
- [x] Open and cancel Settings immediately after scrolling.
- [x] Switch audio immediately after scrolling.
- [x] Reader cannot minimize/maximize but remains resizable.
- [x] Minimize owner during playback closes reader with no desktop shadow.
- [x] Minimize owner during manual navigation.
- [x] Restore owner and reopen reader with correct synchronization.
- [x] Repeated reader open/close without errors or stale text.

The previously reported residual reader shadow is resolved in the user's tested Windows environment. The focused regression is accepted; this does not establish every earlier full-matrix case (e.g. mixed-DPI/multiple monitors, missing runtime, actual device failure, and optional MP3 checks). No application source changes, merge, release tag or release authorization in this documentation update.

### Subtitle settings refinement (accepted at 73df725)

At the user's request: preserve the 480 × 420 DIP dialog, audio controls and shared action buttons; modern local tab templates (音频设置/字幕参数), rounded selectors, aligned spacing, delete sample preview, three groups of five borderless color swatches with checkmarks. Defaults 16 DIP, 1.6 line height, 2 em indent; allowed choices and exact retired-value normalization are in Configuration. SettingsStore normalizes only recognized removed options on load, preserves the source file until an explicit save, and still rejects invalid values. Added core coverage for all 40 retired-value combinations, partial settings and invalid values; UI assertions cover tab labels, fit, selector styling, swatch fills/checks/required selection, defaults, confirmation and cancellation. No change to main/reader window, playback engine or script.html. Fresh CI and local visual/functional verification required; prior b662cba reader regression remains historical evidence. No merge authorized.


At `73df725`, [Feature validation CI](https://github.com/chenjing2000/audioplayer/actions/runs/45515160423) passed. User-provided Windows log confirms local HEAD at that revision, 59 Core + 307 UI + 5 publishing safeguards + 4 published EXE checks = 375 passed, zero failed; local publish succeeded. Optional real MP3, MP3 metadata and MP3-dependent UI tests skipped; temporary WebView2 test-cache cleanup still reported a nonfatal in-use warning. All 14 focused manual checks passed: modern tabs, consistency with audio page, rounded inputs/alignment/spacing, full font names, correct option ranges, five borderless swatches per group, single selection/checkmark, no text preview, then-current defaults 16/1.6/2, confirmation applies, cancel preserves, restart restores, audio settings work, and reader scrolling/seeking/coloring unchanged. This acceptance applies to 73df725, before the next settings adjustment.

### Subtitle size and input typography follow-up (manual acceptance passed; automated failures)

User requested font sizes 17/18/19/20 DIP, fixed window height 430 (width 480 unchanged), and normal-weight text in edit boxes and combo boxes. With 16 retired, default is 17; former 14/15/16 normalize to 17, former 22/24/26/28 to 20; 17–20 stay valid (20 no longer normalizes). Only known retired values are normalized on load; unknown values (e.g. 21) still fail validation and loading never rewrites the file. Selected tab header alone remains semibold; text boxes, selected combo values and dropdown items explicitly use normal weight. Core and UI assertions updated; CI and focused local checks for this revision pending. Playback/reader source unchanged. No merge authorized.

At `a0f5540` the user confirmed all seven focused manual checks (17/18/19/20 options, default 17, 430 DIP height, normal edit and dropdown text, selected header semibold, persistence/cancel/restart). The supplied local log verifies this HEAD and 59 Core, 307 UI, 5 publishing safeguards passed; publish succeeded; published EXE checks 3 passed, 1 failed (374 automated passes, 1 failure total). The failing isolated renamed-EXE check reported the seeded playlist path missing during the asynchronous restore poll. Optional MP3 checks skipped; temporary WebView2 cache in-use warning repeated. [CI #45516634249](https://github.com/chenjing2000/audioplayer/actions/runs/45516634249) failed in WPF UI at the 25-second WebView2 ready handshake with no ReaderFailure reported; Core and workflow smoke passed, publish steps skipped. These are distinct failures, not proof of a settings regression.

### Test reliability follow-up (verified)

Test-only follow-up: the published EXE check now polls for the complete expected restored snapshot for a separate bounded 20-second interval, tolerates transient IO exceptions during file replacement, and still fails on malformed JSON, premature process exit or incorrect/missing persisted state at deadline. The WebView2 cold-start test allows a bounded 60-second ready handshake, still stops on reader-reported error and identifies timeout explicitly. Both suspected timing issues require reruns; neither cause is proven by the previous logs alone. No application source changes, no skipped assertions, no merge authorized.


### PR #17 final acceptance and merge

PR #17 was squash-merged into main at `5b5c428` on 2026-11-25 after explicit user authorization. The accepted feature head `d3920b6` passed [Feature validation #251](https://github.com/chenjing2000/audioplayer/actions/runs/45517740732), including Core, workflow smoke, WPF UI, portable publish and published EXE checks. The user confirmed the local published EXE rerun passed 4/4 at d3920b6 using the unchanged application from a0f5540. Combined final-code local evidence: 59 Core + 307 UI + 5 publishing safeguards + 4 published EXE checks = 375 passed, zero outstanding failures, across two runs (not a single full rerun). The 22 reader, 14 settings and 7 final typography manual checks passed across successive revisions. Optional MP3 checks were skipped; temporary WebView2 test-cache cleanup warning and unverified mixed-DPI/device-failure cases remain recorded. The two earlier automated failures did not recur; their exact original causes were not established. No release tag or release assets were requested.

Post-merge CI is separate; its result was not yet available when this documentation was prepared.


### PR #17 post-merge Windows verification and branch cleanup (2026-11-25)

The user-provided Windows PowerShell 5.1 log confirms that local main fast-forwarded to `8a165d1` (HEAD, origin/main and origin/HEAD matched), and that both local and remote `feature/script-companion-window` were deleted. The remote branch listing was independently verified: only main remains. At `8a165d1`, the complete local post-merge sequence passed: `Test.ps1 -Suite Full` (59 Core, 307 UI and 5 publishing safeguards, zero failures), `Publish.ps1` (successful win-x64 .NET 10 portable EXE at dist/AudioPlayer.exe), and `Test-PublishedApp.ps1` (4/4, zero failures). Total: 375 executed checks passed in this single verification sequence. Both earlier failing checks (WebView2 ready handshake and renamed EXE playlist restoration) passed. The WebView2 success message misleadingly includes the timeout fallback text; the assertion passed and subsequent reader tests passed. Optional real MP3 playback, MP3 metadata and MP3-dependent UI checks were skipped; temporary test WebView2 cache remained in use during cleanup (nonfatal). No post-merge workflow run was returned for either `5b5c428` or `8a165d1` at the latest check; pre-merge CI #45517740732 remains verified successful. No new release tag or assets were requested.

## 2026-11-25: modular architecture refactor

Branch: `refactor/modular-architecture`, based on `main@bca0333`. No merge or release authorized. The user provided full Windows test/publish logs at each accepted stage; remote CI was independently checked.

| Stage | Accepted commit | Core | UI | Publish safeguards | Published EXE | CI |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| R1 — source organization | `9a6c45e` | 59 | 307 | 5 | 4 | [pass](https://github.com/chenjing2000/audioplayer/actions/runs/45520972876) |
| R2 — Playlist | `96bc4a4` | 62 | 307 | 5 | 4 | [pass](https://github.com/chenjing2000/audioplayer/actions/runs/45521718204) |
| R3 — settings/options | `88ef87d` | 63 | 307 | 5 | 4 | [pass](https://github.com/chenjing2000/audioplayer/actions/runs/45522142184) |
| R4 — SilenceTasks | `e7ab8c5` | 66 | 307 | 5 | 4 | [pass](https://github.com/chenjing2000/audioplayer/actions/runs/45523315002) |
| R5 — ScriptWindowHost | `d34a3e7` | 66 | 311 | 5 | 4 | [pass](https://github.com/chenjing2000/audioplayer/actions/runs/45523913114) |
| R6 — session/controller | `3978cab` | 66 | 313 | 5 | 4 | [pass](https://github.com/chenjing2000/audioplayer/actions/runs/45524695021) |
| R7 — final cleanup | `e655cfa` | 66 | 313 | 5 | 4 | [pass](https://github.com/chenjing2000/audioplayer/actions/runs/45525173631) |

Each listed local run had zero failures and published a fresh EXE. R1 fixed a missing Settings import. R4 initially failed a rename race: the cache verifier opened audio even when no result existed. The missing-cache fast path and exclusive-file regression passed without weakening cache validation. R6 initially failed its command-gate test; the test was isolated from asynchronous startup restoration and now explicitly verifies that the held command entered the gate. The fixed test passed without business-code changes.

The current test runners remain `Program.cs` (core and entry point) and `UiChecks.cs` (WPF integration), organized by test purpose; existing assertions are retained. Direct module access replaces obsolete MainWindow reflection where practical. No additional runner or test framework is required. R7 passed all 388 executed checks locally and completed GitHub CI successfully at `e655cfa`.

### Final refactor acceptance (passed, 2026-11-25)

Application revision: `e655cfa`. Full, Publish and Test-PublishedApp passed on Windows. The user explicitly confirmed the following 21 manual checks:

| Area | Confirmed checks | Result |
| --- | --- | --- |
| Library | Open directory, recursive scan and natural sort; refresh after adding/deleting audio; restart restores list without autoplay | 3/3 |
| Playback | Play/pause/stop and slider; focus-independent Space/Left/Right; Ctrl+Left/Right timed seeks; four modes; manual stop/seek does not trigger auto-advance | 5/5 |
| Silence | First-play analysis and subsequent cache reuse; trim eligibility for a different selected track; safe trim without source changes or overwrite; listening confirms complete speech without obvious pops | 4/4 |
| Script | Show/hide/reopen; progressive coloring and smooth scrolling; wheel/scrollbar seek; restore prior playback state; main-window command priority; owner minimize/restore/close | 6/6 |
| Settings and shutdown | Save/cancel audio and subtitle settings; restart restores configuration; shutdown without apparent hang, residual window or file lock | 3/3 |

Totals: **388 automated checks + 21 manual checks = 409 passed**, zero reported failures within the executed scope. The media format used for the manual trim was not specified; do not claim format-specific coverage. Real MP3-specific automated tests were skipped; mixed DPI and actual playback device failure remain unverified. Real long-pause WAV/MP3 format-specific acceptance remains deferred. The nonfatal temporary WebView2 test-cache cleanup warning persists. Earlier PR #17's 43 manual checks are historical evidence only. No merge or release authorization has been given.

### PR #18 merge and post-merge verification (2026-11-25)

With explicit user authorization, [PR #18](https://github.com/chenjing2000/audioplayer/pull/18) was squash-merged at `21f5f75`. Accepted feature head `867638d` and main merge commit have identical Git tree SHA `e007d27c1e47ee8aeff12b11659c925d51f37dfd`. Final [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45526090749), [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45526106161) and [post-merge main CI](https://github.com/chenjing2000/audioplayer/actions/runs/45526540916) all passed.

The user-provided Windows PowerShell log confirms local main synchronized to `21f5f75` (HEAD, origin/main, origin/HEAD matched). A separate complete post-merge run passed 66 Core, 313 UI, 5 publishing safeguards and 4 published EXE checks (**388/388**, zero failures), with successful win-x64 portable publish. Real MP3 playback, MP3 metadata and MP3-dependent UI checks skipped; temporary WebView2 cache cleanup reported a nonfatal in-use warning. The 21 manual acceptance checks were performed on the identical pre-merge application, not repeated after merge. Mixed DPI, actual playback device failure and format-specific deferred tests remain unverified. No release tag or assets requested. The subsequent user log confirms local main fast-forwarded to `593abd6`, the local and remote refactor branches were deleted, and the working tree is clean. The local deleted refactor branch was at `e655cfa`; the remote accepted head was `867638d`. The remote branch listing independently confirms only main remains. No application changes after `21f5f75`.

## Playback color palette refresh (accepted, 2026-11-25)

Approved playback colors: `#D71920`, `#BC4600`, `#007D45`, `#1685B8`, `#8A2BE2` (default red). Existing retired playback colors normalize on load only, including the older `#A33D30`; see Configuration for the exact mapping. Core coverage checks the exact palette, default, persistence of all five colors, legacy mappings without rewriting on load, and rejection of unknown colors. The reader CSS fallback matches the new default. UI layout, text/background palettes and playback logic are unchanged. At application revision `8379a96`, both [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45601380402) and [PR #19 CI](https://github.com/chenjing2000/audioplayer/actions/runs/45601411735) passed all workflow steps. The user-provided Windows PowerShell log confirms the local branch was synchronized and clean at `8379a96` before testing. Full / Publish / Test-PublishedApp succeeded: 66 Core, 313 UI, 5 publishing safeguards and 4 published EXE checks = **388 passed, zero failed**. A fresh portable EXE was published to `dist/AudioPlayer.exe`. The user explicitly confirmed all seven manual checks: new swatches and order, reader coloring with every color, unchanged text and background palettes, default red, cancellation, persistence after restart, and old configuration loading. Optional real MP3 playback, metadata and dependent UI tests were skipped; the temporary WebView2 test cache remained in use during cleanup (nonfatal). Previously unverified mixed DPI and real device failure remain outside this acceptance. No merge or release authorized.

## Subtitle settings layout refinement (automated verification passed; superseded sizing)

On the same PR #19 branch, the user approved equal-width halves separated by 12 DIP, 33-DIP minimum selector height, equal numeric selector widths, and a wider 160-DIP font selector. Text and playback palettes share the first color row; background stays in the left half below. The existing 24-DIP circles keep their size, with 28-DIP item width and height. Window size remains 480 × 430 DIP, audio tab, buttons, colors and settings behavior are unchanged. WPF checks cover actual equal widths, selector heights and alignment, font-name measure, palette positioning, label clearance and swatch bounds. At `992dd59`, [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45604867185) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45604875978) passed. The user-provided Windows log confirms a clean synchronized checkout and Full / Publish / Test-PublishedApp success: 66 Core, 323 UI, 5 publishing safeguards and 4 published EXE checks = 398 passed, zero failed. Optional MP3 checks skipped and nonfatal temporary WebView2 cleanup warning remained. The user provided a screenshot and requested another sizing adjustment; no claim of full eight-item manual acceptance. No merge authorized.

## Shared settings control dimensions (superseded by 30-DIP correction)

User approved fixed 26-DIP height for existing rounded text inputs and combo boxes, 80-DIP numeric controls, 160-DIP font selector, and fixed 43-DIP parameter rows across both tabs, including the two subtitle palette rows. Remove prior vertical margins in these rows and center controls. Keep window size, two-column subtitle layout, color swatches, audio help/exclusion layout and all settings behavior. Existing UI checks now compare measured sizes, row heights and vertical positions across tabs. At `2fff393`, [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45607206303) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45607218680) passed. The user-provided Windows log confirms 66 Core, 326 UI, 5 publishing safeguards and 4 published EXE checks = 401 passed, zero failed, with portable publish successful. Optional MP3 cases skipped; nonfatal temporary WebView2 cleanup warning remains. However, the user screenshot shows clipped text in the audio inputs, so manual acceptance is not complete. The existing size tests did not detect internal text clipping. No merge authorized.

## Settings input text clipping correction (accepted, 2026-11-25)

User requested 30-DIP input and selector heights and reduced internal vertical padding after observing clipped text. Remove the redundant PART_ContentHost margin (WPF forwards TextBox padding to the ScrollViewer); set input padding once in the existing style to 6 horizontal / 1 vertical DIP and reduce the combo selection presenter vertical margin to 1 DIP. Keep 43-DIP rows, widths, alignments, palettes and all behavior. Add viewport-height and selected-value content-height checks rather than relying on outer dimensions alone. At final application revision `98b1b68`, [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45608222245) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45608231419) passed all workflow steps. The user-provided Windows log confirms a synchronized clean checkout and successful Full / Publish / Test-PublishedApp: 66 Core, 339 UI, 5 publishing safeguards and 4 published EXE checks = **414 passed, zero failed**, including all 13 new text viewport/selected-value checks. Portable EXE published successfully. The user explicitly confirmed all six focused manual checks: complete input text, complete centered selector text, matching control heights, matching parameter rows and vertical positions, exclusion field display/editing, and normal color layout/settings behavior. The two supplied screenshots show both tabs with complete visible text and aligned rows. Optional real MP3 playback/metadata/UI checks skipped; nonfatal temporary WebView2 cache cleanup warning persists. Mixed DPI and actual device failure remain unverified. No merge or release authorized.

## Subtitle dropdown item styling (visual acceptance and automated verification passed)

User requested a less harsh dropdown highlight after seeing the default Windows blue hover and gray selected outlines. Replace the existing local ComboBoxItem template with a borderless 4-DIP rounded surface: neutral transparent, highlighted #E8EDF2, selected #D9EBEF with #26798A foreground; selected state remains distinct and takes precedence. Retain WPF selection, keyboard navigation, values, control sizes and popup shell. UI checks cover each selector's item template, normal and selected appearance, and highlight trigger; actual pointer/keyboard behavior and appearance require focused manual acceptance. At application revision `c01bad9`, [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45609486330) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45609494130) passed. The supplied Windows log shows a fast-forward to this revision followed by successful Full / Publish / Test-PublishedApp: 66 Core, 349 UI, 5 publishing safeguards and 4 published EXE checks = **424 passed, zero failed**, including all ten new dropdown style assertions. Portable EXE published successfully. The user supplied a dropdown screenshot and explicitly accepted the highlight appearance. Separate manual keyboard-navigation verification for this revision was not explicitly reported. Optional real MP3 tests were skipped; the nonfatal WebView2 cache cleanup warning remains. No merge authorized. The subsequent review recommended a local settings cleanup rather than another architecture refactor.

## Settings implementation cleanup (accepted, 2026-11-25)

User authorized the R1–R6 review recommendations. Consolidate existing rounded input and color-list properties; numeric input widths come from their 80-DIP Grid columns; remove unused selector vertical-content alignment and zero margin, retain scroll settings because their template behavior was not proven redundant. Merge signed/unsigned integer parsing and reuse the existing paste validator for all four input grammars. Share four numeric range bounds in SilenceOptions while keeping field-local and final validation separate. Combine migration-test setup/assertions without dropping cases; use measured control widths instead of requiring redundant Width attributes. No new files/classes/dependencies, no changes to UI design, settings data, or seven-module architecture. At final application revision `d831725`, [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45610688411) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45610697521) completed successfully. The user-provided Windows log confirms a synchronized clean checkout and successful Full / Publish / Test-PublishedApp: 66 Core, 349 UI, 5 publishing safeguards and 4 published EXE checks = **424 passed, zero failed**. Portable EXE published successfully. The user explicitly confirmed all seven focused manual checks: unchanged audio and subtitle layouts, input grammar, valid/invalid paste, range-error feedback and recovery, mouse and keyboard dropdown selection (including Enter/Esc), and restore/cancel/confirm/persistence. Optional real MP3 playback, metadata and dependent UI tests skipped; nonfatal temporary WebView2 cache cleanup warning remains. Mixed DPI and actual device failure remain unverified. No merge authorized.

## PR #19 post-merge verification (2026-11-25)

After explicit authorization, both final feature-head CI runs ([push](https://github.com/chenjing2000/audioplayer/actions/runs/45611295437), [PR](https://github.com/chenjing2000/audioplayer/actions/runs/45611303958)) passed and PR #19 was squash-merged into main at `d2b69d7`. The Git tree matches `3257d74` exactly. [Post-merge main CI](https://github.com/chenjing2000/audioplayer/actions/runs/45612193760) passed. The user supplied a clean synchronized main checkout and successful Full suite: 66 Core, 349 UI, 5 publishing safeguards, zero failures. First Publish compiled successfully but refused to replace the occupied destination EXE; the first published-app check therefore did not establish a new binary. A second Publish succeeded and the subsequent Test-PublishedApp passed all four checks, for **424 final executed checks passed**. Seven prior focused manual checks apply to identical application code. Optional real MP3 playback/metadata/dependent UI checks skipped; nonfatal temporary WebView2 cache cleanup warning remains; mixed DPI and actual device failure remain unverified. No version change, release tag or release assets. Branch cleanup not yet confirmed.

## Playback background highlighting (accepted; merge pending)

Approved on 2026-11-25: keep text color constant and retain cumulative per-unit progress using SVG rectangles below the single text layer; remove the duplicate text and clip paths. Preserve partial-word, rewind/reset and scroll/seek behavior. Five light playback colors and direct legacy migration use the existing ScriptColor field. Added checks for exact nonduplicated text, background layer/fill/geometry, dark palette checkmarks and all five previous foreground values. At `90c179f`, [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45614471162) and [PR #20 CI](https://github.com/chenjing2000/audioplayer/actions/runs/45614498814) passed. The user's clean synchronized Windows checkout passed 66 Core, 350 UI, 5 publishing safeguards and 4 published EXE checks (**425 passed, zero failed**); publish succeeded. Optional real MP3 checks skipped; nonfatal WebView2 cache cleanup warning remains. Nine of ten manual checks passed; palette softness was not accepted. No sustained-reading acceptance claimed. The user requested lighter colors; each first-palette RGB value is blended 65:35 with the default page background #F8F8F6 and rounded to the nearest integer. The five resulting colors are #F6EDC2, #F5E4D6, #E1EEDB, #E0ECF4, #ECE6F3. First-palette values normalize directly to the corresponding new color; default CSS and existing tests updated. No geometry, timing, scroll or layout changes. At final application commit `1c62c19`, both [branch CI](https://github.com/chenjing2000/audioplayer/actions/runs/45615671826) and [PR #20 CI](https://github.com/chenjing2000/audioplayer/actions/runs/45615680988) passed. The user's synchronized clean Windows checkout completed Full / Publish / Test-PublishedApp successfully: 66 Core, 350 UI, 5 publishing safeguards and 4 published EXE checks (**425 passed, zero failed**). User confirmed the softer palette achieves the expected result; together with the previous nine accepted checks, all ten focused manual acceptance items are satisfied across the two revisions. No claim of a separate ten-item rerun or sustained-reading test. Optional real MP3 playback/metadata/dependent UI checks skipped; nonfatal temporary WebView2 cache cleanup warning remains. Mixed DPI and actual device failure remain unverified. No merge authorized.
