# AudioPlayer — development handoff

**Updated:** 2026-11-24 — PR #16 merged; post-merge CI passed  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Latest merged application baseline:** `af27658` ([PR #16](https://github.com/chenjing2000/audioplayer/pull/16), squash merged).  
**Remote branches:** Only `main` remained when checked; local checkout synchronization and branch cleanup have not been independently confirmed.

## Current application

C# / WPF / .NET 10, NAudio 2.2.1, TagLibSharp 2.3.0. Portable framework-dependent Windows x64 executable `dist/AudioPlayer.exe`. Current behavior and data ownership are defined by [program overview](program-overview.md), [project contract](project-contract.md) and [configuration](Configuration.md); publishing is described in [Publishing](Publishing.md).

PR #16 adds:
- Configurable Ctrl+Left/Right timed seek (default 5/4 seconds, 0–99), preserving Space and plain Left/Right silence navigation.
- Configurable exact-name, case-insensitive folder exclusions (empty by default; explicit root exempt), consistent across scans, restoration and trimmed-output insertion.
- Fixed 480 × 420 DIP settings dialog and virtualized playlist row-header/border correction.
- Four persistent playback modes (单曲播放 → 单曲循环 → 顺序播放 → 列表循环), one button between Refresh and Settings, concise mode-only tooltips.
- Playback lifecycle remediation: startup state ordering, empty-media rejection, shared modal deferral, explicit-command priority and stale-completion protection; simplified exclusion processing and strengthened regression coverage.

Startup restores saved playlists without autoplay. Source audio and existing outputs remain protected. Settings and playlist JSON stay beside the actual EXE.

## Verification and remaining limits

Final feature head `c3a4c21` passed both [push CI](https://github.com/chenjing2000/audioplayer/actions/runs/45382457033) and [PR CI](https://github.com/chenjing2000/audioplayer/actions/runs/45382467366). Merged application baseline `af27658` passed [post-merge Windows CI](https://github.com/chenjing2000/audioplayer/actions/runs/45382847185). The user authorized merging on 2026-11-24.

The user confirmed 70 cumulative manual checks across successive feature revisions (16 seek, 16 exclusions, 6 scrolling, 20 modes, 12 remediation). Local `3321875` passed 55 Core and 225 UI checks plus 5 publishing safeguards, including actual synthetic WAV playback; optional MP3 checks skipped. A new local automated log for remediation or a separate manual result for the final tooltip refinement was not supplied. Historical evidence and limitations are maintained in [Testing](Testing.md).

Real long-pause MP3/WAV listening, dedicated directory-access-denial checks, selected FFmpeg fault scenarios and actual tagged Release assets remain deferred. No new release tag was created.

## Next development round

- Read this handoff, [program overview](program-overview.md), [project contract](project-contract.md) and root AGENTS.md; verify current main and source before changes.
- Discuss new requirements first, then use a new feature branch. Follow [WindowsAppPlaybook](https://github.com/chenjing2000/windowsappplaybook): first principles, minimum necessary design, remove redundancy before adding entities, no unnecessary frameworks or broad refactoring.
- Protect source audio, outputs, settings, playlists and installed FFmpeg. Use English commit messages, hosted CI and focused Windows acceptance. Do not merge without separate explicit authorization.

```powershell
cd C:\Projects\AudioPlayer
git switch main
git pull --ff-only origin main
git fetch --prune
git status
git log -1 --oneline
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```
