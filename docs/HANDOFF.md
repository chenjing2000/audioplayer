# AudioPlayer — development handoff

**Updated:** 2026-11-24 — PR #16 review remediation; verification pending  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Main:** `00d4928` (application baseline `7cb288e`, PR #15 squash merged).  
**Active branch:** `feature/configurable-seek-shortcuts`, [PR #16](https://github.com/chenjing2000/audioplayer/pull/16), not merged.

## Current application

C# / WPF / .NET 10, NAudio 2.2.1, TagLibSharp 2.3.0. Portable framework-dependent executable `dist/AudioPlayer.exe`. Current behavior and data ownership are defined by [program overview](program-overview.md), [project contract](project-contract.md) and [configuration](Configuration.md); publishing is described in [Publishing](Publishing.md).

PR #16 adds configurable Ctrl+Left/Right seeking, configurable exact-name folder exclusions (empty by default), 480 × 420 DIP settings dialog, virtualized playlist row-header correction and four persistent playback modes. Startup restoration never autoplays. Source media and existing outputs remain protected.

## Current verification status

The user accepted 58 manual checks across successive revisions (16 seek, 16 exclusions, 6 scrolling, 20 modes). Local `3321875` passed 55 Core and 225 UI checks plus publishing safeguards, including synthetic WAV playback; optional MP3 checks skipped. Historical evidence, CI links and earlier PR #11/#15 results are in [Testing](Testing.md).

Review remediation after `20b9e84` establishes playback state before starting output, rejects empty media, shares modal protection, retains explicit-command priority, refines real-entry-point tests and simplifies exclusion processing. Settings class names intentionally retained. New CI and Windows acceptance are pending. Do not treat prior acceptance as a rerun of this revision.

## Next steps

- Verify the remediation with the commands below and focused Windows checks. Real long-pause MP3/WAV listening, directory access denial, selected FFmpeg failure cases and tagged release assets remain deferred; see Testing.
- Preserve source audio, output files, settings, playlists and installed FFmpeg. Avoid unnecessary frameworks or broad refactoring.
- Discuss new requirements first; do not merge without explicit authorization. Use English commit messages.

```powershell
cd C:\Projects\AudioPlayer
git switch feature/configurable-seek-shortcuts
git pull --ff-only origin feature/configurable-seek-shortcuts
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```
