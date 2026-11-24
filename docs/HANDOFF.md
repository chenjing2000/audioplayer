# AudioPlayer — development handoff

**Updated:** 2026-11-24 — PR #17 feature branch, core Windows acceptance passed; remaining edge cases pending  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Merged main baseline:** `52bb0b8` (application from PR #16, `af27658`).  
**Feature branch:** `feature/script-companion-window`; latest verified feature revision `6789ffd`; [draft PR #17](https://github.com/chenjing2000/audioplayer/pull/17). Do not merge without separate explicit user authorization. Local checkout not independently verified.

## Current feature branch

All five implementation stages and source-review remediation are committed: modeless owned WPF Script window; ReadArticles Script JSON discovery and read-only rendering; progressive foreground coloring and visual-line follow; direct wheel navigation with 150 ms debounced seek and playback restoration; two-tab settings and persistent appearance. See [program overview](program-overview.md), [project contract](project-contract.md), [configuration](Configuration.md) and [publishing](Publishing.md). WebView2 is an optional runtime for the reader, not audio playback. The existing single EXE publish shape, user data and PlaybackEngine are retained.

## Verification and next action

Feature revision `6789ffd` passed [push CI](https://github.com/chenjing2000/audioplayer/actions/runs/45402314948) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45402326844): 58 Core, 223 UI, 5 publishing safeguards and 4 published EXE checks. User's local Windows run passed 58 Core, 262 UI, 5 publishing safeguards and 4 published checks (329 total), including WebView2 and all seven wheel assertion lines. The prior published reader initialization defect is resolved on the user's computer. All 10 focused manual reader checks passed; remaining full-matrix edge cases are not yet confirmed. Optional MP3-specific cases skipped; a temporary test WebView2 cache remained in use during cleanup. See [Testing](Testing.md) for exact scope and limitations. No release tag or merge authorized.

```powershell
cd C:\Projects\AudioPlayer
git status --short
git fetch origin --prune
git switch feature/script-companion-window
git pull --ff-only origin feature/script-companion-window
git log -1 --oneline
.\tools\Test.ps1 -Suite UI
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

## Prior merged baseline

PR #16 was squash merged at `af27658`; [post-merge CI](https://github.com/chenjing2000/audioplayer/actions/runs/45382847185) passed. The user confirmed 70 cumulative manual checks across successive revisions. Local `3321875` passed 55 Core, 225 UI and 5 publishing safeguards, including synthetic WAV playback; optional MP3 skipped. Real long-pause MP3/WAV listening, directory access denial, selected FFmpeg failure paths and tagged release assets remain deferred. Historical evidence is in [Testing](Testing.md).

Follow root AGENTS.md and WindowsAppPlaybook; discuss new requirements before coding; protect user data, use English commit messages and avoid unnecessary abstractions. Keep main unchanged until explicit merge authorization.

## Local acceptance update (2026-11-24)

At `0ada0d9`, user logs confirmed 58 Core, 260 UI, 5 publishing safeguards and 4 published checks passed, including all actual WAV wheel assertions. Optional MP3 checks skipped. Manual published reader failed with “原文页面加载失败。”; do not mark reader accepted. The follow-up fixes local document navigation using the ReadArticles embedded-resource approach, reports diagnostic failures and adds actual reader integration coverage. Subsequent revision `6789ffd` passed the local rerun and focused manual acceptance, as recorded above. Remaining checklist cases and separate merge authorization are pending.

## Follow-up review remediation (pending verification)

After the 10 focused manual passes, source review identified modal cancellation, seek rejection and device-error restoration risks. The branch now preserves playback intent when opening either modal, cancels on device errors, resynchronizes rejected browser previews at commit, retries a failed reader on reopen, cancels obsolete script loads, and indexes started/active timestamp intervals. The changes require fresh CI and targeted Windows regression. Playback-device clock calibration and mixed-DPI monitor sizing remain measurement-dependent; no speculative changes to the audio engine or geometry have been made.

## Owner minimization correction (pending verification)

At `f900adb` both CI workflows passed (58 Core, 226 UI, 5 safeguards, 4 published checks; 293 total). The user confirmed all seven focused remediation manual checks, including long IELTS responsiveness, but reported a residual Script window shadow at the upper-left desktop when minimizing the main window. The owner now explicitly hides a visible companion on minimize and restores the same instance without activation on restore; a previously hidden companion remains hidden. New native visibility/lifecycle assertions use the isolated WebView2 test reader. Actual shadow disappearance requires Windows desktop visual confirmation. Latest local automated log for f900adb was not supplied. No merge authorization.

## Smooth automatic line transition (superseded; not accepted)

At the user's request, automatic visual-line follow now uses 180 ms half-cosine interpolation instead of 50 ms. Existing requestAnimationFrame, progressive coloring, direct wheel navigation and 150 ms wheel debounce are preserved. A deterministic WebView2 frame-callback assertion is added; fresh CI and Windows visual acceptance remain pending, including the separate minimization-shadow check. No merge authorized.

## Continuous subtitle scrolling (pending verification)

The user clarified that subtitles should continuously move during playback, not ease between lines and stop. Replaced the 180 ms line-transition animation with an audio-time-to-position trajectory interpolated between visual-line anchors; frame callbacks extrapolate the host clock for at most 50 ms. Kept direct wheel navigation, 150 ms debounce, confirmed-clock coloring, natural bounds, pause/reset and immediate discontinuity handling. Replaced the prior animation assertion with continuous-offset and frame lifecycle checks. New CI and local visual acceptance are pending. The separate minimization-shadow visual check is still unresolved. No merge authorized.
