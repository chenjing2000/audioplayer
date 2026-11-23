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

Test-PublishedApp.ps1 copies only the EXE into a temporary path with spaces, renames it, and launches it from another working directory. It checks the main window, configuration placement, clean closure, and the native missing-.NET installation instructions using an isolated runtime root. Installed runtimes and user configuration are untouched. MdictViewer also runs its existing nine application-specific published checks, covering settings, dictionary rendering, and missing WebView2.

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

Use `-AudioFile` for a **single WAV, MP3, or M4A file**. The runner copies the media to a temporary directory with its **original extension**, runs the real long-silence trimming pipeline and verifies the output extension, that it can be decoded and that the source remains unchanged. This does not pretend that an M4A is MP3; the old MP3-specific playback/UI tests are skipped unless `-AudioDirectory` is also supplied. Large recordings may take extra time to decode and encode.

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


## Feature-branch results/Toast refactor: automated coverage and outstanding acceptance

The approved [refactor specification](specs/Results-Cache-Toast-Refactor.md) and [implementation checklist](guides/Results-Cache-Toast-Implementation.md) require updated Core/UI/Full tests for the single `results/<full audio filename>/silence.json` store, metadata-based invalidation without SHA-256, automatic detection/cancellation, navigation from verified in-memory ranges, cache-based trimming without a second scan, numbered non-overwriting outputs, independent results for newly encoded media, Toast behavior, one-decimal settings validation, and playlist alignment.

The implementation and new Core/UI assertions are committed on `feature/results-cache-toast-refactor`. Their actual pass/fail evidence must be taken from this branch's CI runs and new Windows acceptance, not from the earlier M4A/Windows records. Use temporary synthetic WAV/PCM fixtures where possible. Real long-pause MP3/WAV testing remains deferred until suitable files exist; do not turn a Skip into Pass.
