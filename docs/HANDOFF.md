# AudioPlayer — development handoff

**Updated:** 2026-11-25 — PR #17 subtitle settings accepted at 73df725; size and typography follow-up pending verification  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Merged main baseline:** `52bb0b8` (application from PR #16, `af27658`).  
**Feature branch:** `feature/script-companion-window`; latest verified application revision `73df725`; [draft PR #17](https://github.com/chenjing2000/audioplayer/pull/17). Do not merge without separate explicit user authorization. User-provided Windows log verifies local HEAD and origin at that revision; local checkout not independently inspected.

## Current feature branch

All five implementation stages and source-review remediation are committed: modeless owned WPF Script window; ReadArticles Script JSON discovery and read-only rendering; progressive foreground coloring and visual-line follow; direct wheel navigation with 150 ms debounced seek and playback restoration; two-tab settings and persistent appearance. See [program overview](program-overview.md), [project contract](project-contract.md), [configuration](Configuration.md) and [publishing](Publishing.md). WebView2 is an optional runtime for the reader, not audio playback. The existing single EXE publish shape, user data and PlaybackEngine are retained.

## Verification and next action

Feature revision `b662cba` passed [Feature validation CI](https://github.com/chenjing2000/audioplayer/actions/runs/45434378532) and the user-provided Windows full suite: 58 Core, 276 UI, 5 publishing safeguards and 4 published EXE checks (343 total, zero failures). All 22 focused manual regression checks passed, including scrollbar navigation, operation competition, fine-grained wheel seeking, and close-before-minimize without residual shadow. Local publish succeeded. Optional MP3 checks skipped; isolated temporary WebView2 cache cleanup reported a nonfatal in-use warning. Earlier full-matrix edge cases not separately confirmed remain unverified. See [Testing](Testing.md). Preserve the accepted application code; await explicit merge authorization. No release tag authorized.

```powershell
cd C:\Projects\AudioPlayer
git status --short
git fetch origin --prune
git switch feature/script-companion-window
git pull --ff-only origin feature/script-companion-window
git log -1 --oneline
.\tools\Test.ps1 -Suite Full
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

## Owner minimization correction (superseded)

At `f900adb` both CI workflows passed (58 Core, 226 UI, 5 safeguards, 4 published checks; 293 total). The user confirmed all seven focused remediation manual checks, including long IELTS responsiveness, but reported a residual Script window shadow at the upper-left desktop when minimizing the main window. The owner now explicitly hides a visible companion on minimize and restores the same instance without activation on restore; a previously hidden companion remains hidden. New native visibility/lifecycle assertions use the isolated WebView2 test reader. Actual shadow disappearance requires Windows desktop visual confirmation. Latest local automated log for f900adb was not supplied. No merge authorization.

## Smooth automatic line transition (superseded; not accepted)

At the user's request, automatic visual-line follow now uses 180 ms half-cosine interpolation instead of 50 ms. Existing requestAnimationFrame, progressive coloring, direct wheel navigation and 150 ms wheel debounce are preserved. A deterministic WebView2 frame-callback assertion is added; fresh CI and Windows visual acceptance remain pending, including the separate minimization-shadow check. No merge authorized.

## Continuous subtitle scrolling (focused Windows acceptance passed)

The user clarified that subtitles should continuously move during playback, not ease between lines and stop. Replaced the 180 ms line-transition animation with an audio-time-to-position trajectory interpolated between visual-line anchors; frame callbacks extrapolate the host clock for at most 50 ms. Kept direct wheel navigation, 150 ms debounce, confirmed-clock coloring, natural bounds, pause/reset and immediate discontinuity handling. Replaced the prior animation assertion with continuous-offset and frame lifecycle checks. The feature validation workflow for `8cc723e` completed successfully; the user reported all eight focused Windows checks passed (see Testing). No separate local SHA or automated log was supplied. The separate minimization-shadow visual check is still unresolved. No merge authorized.

## Initial wheel sensitivity reduction (superseded)

At the user's request, the original wheel displacement is multiplied by 0.5 after pixel/line/page normalization. Center-line seek, debounce (150 ms), direct wheel motion and continuous automatic scrolling are unchanged; elapsed-time navigation is approximate due to discrete lines and speech timing. A browser assertion checks both directions and wheel modes. New CI and Windows wheel acceptance remain pending. No merge authorized.

## Further wheel sensitivity reduction (historical; accepted on refactored revision)

The user found the 50% wheel displacement at `ba1f351` still too large. The normalized wheel multiplier is now 0.25 (another 50% reduction); automatic follow, line selection and 150 ms debounce are unchanged. The existing wheel regression assertion now checks quarter displacement. New CI and Windows acceptance are pending; no merge authorized.

## Reader controls and owner minimize (historical; accepted on refactored revision)

Reader native minimize/maximize caption buttons removed without changing resizable border. Native scrollbar changes now use the same center-line preview/150 ms debounced seek as the wheel; automatic programmatic scroll events are excluded, and double-click seek is removed. Owner SC_MINIMIZE closes/disposes reader before default minimize, with StateChanged fallback; no auto-reopen on restore. New browser/native lifecycle assertions replace hide/restore assertions. Fresh CI and manual Windows confirmation (including shadow) pending. No merge authorized.

## Reader state and trajectory refactor (focused Windows acceptance passed)

Pre-refactor `6917294` feature validation passed; this does not establish local visual acceptance. Replaced four main-window navigation flags/target with one pending transaction and consumed it through the existing operation gate. Removed cancel/commit browser messages in favor of one authoritative epoch-tagged playback snapshot; preview coloring cannot overwrite the host clock, and only a matching confirmed target preserves the user offset. Removed duplicate sent snapshot, previous position and manual boolean; consolidated reader disposal and stale-instance event guards. The scroll-to-time inverse now shares the same continuous trajectory as time-to-scroll; no per-line quantization, no invented word timestamps, no seek when no anchors. Wheel 25%, 150 ms debounce, continuous follow, window behavior and media engine unchanged. Exact revised JS passed isolated simulated checks. At b662cba, CI and local Windows full suite passed; all 22 focused manual checks passed, including the residual-shadow correction (see Testing). No merge authorized.

## Subtitle settings refinement (accepted at 73df725)

User approved modern local WPF tabs (音频设置/字幕参数), consistent rounded selectors and spacing, removing the sample-text preview, three five-option borderless swatch groups, and narrower typography ranges with defaults 16 DIP / 1.6 / 2 em. Known retired choices normalize only in memory on load; unknown/invalid settings remain errors and reading never overwrites the file. Existing explicit save workflow, audio controls, config schema, playback and subtitle synchronization are preserved. Updated core/UI coverage and configuration documentation. Fresh CI and Windows visual and functional checks pending. Prior b662cba acceptance is unchanged evidence for its reader code. No merge authorized.


At `73df725`, [Feature validation CI](https://github.com/chenjing2000/audioplayer/actions/runs/45515160423) passed. User-provided Windows log confirms local HEAD at that revision, 59 Core + 307 UI + 5 publishing safeguards + 4 published EXE checks = 375 passed, zero failed; local publish succeeded. Optional real MP3, MP3 metadata and MP3-dependent UI tests skipped; temporary WebView2 test-cache cleanup still reported a nonfatal in-use warning. All 14 focused manual checks passed: modern tabs, consistency with audio page, rounded inputs/alignment/spacing, full font names, correct option ranges, five borderless swatches per group, single selection/checkmark, no text preview, then-current defaults 16/1.6/2, confirmation applies, cancel preserves, restart restores, audio settings work, and reader scrolling/seeking/coloring unchanged. This acceptance applies to 73df725, before the next settings adjustment.

## Subtitle size and input typography follow-up (pending verification)

User requested font sizes 17/18/19/20 DIP, fixed window height 430 (width 480 unchanged), and normal-weight text in edit boxes and combo boxes. With 16 retired, default is 17; former 14/15/16 normalize to 17, former 22/24/26/28 to 20; 17–20 stay valid (20 no longer normalizes). Only known retired values are normalized on load; unknown values (e.g. 21) still fail validation and loading never rewrites the file. Selected tab header alone remains semibold; text boxes, selected combo values and dropdown items explicitly use normal weight. Core and UI assertions updated; CI and focused local checks for this revision pending. Playback/reader source unchanged. No merge authorized.
