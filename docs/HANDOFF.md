# AudioPlayer — development handoff

**Updated:** 2026-11-25 — PR #17 merged  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Merged application baseline:** `5b5c428` (PR #17; accepted feature head `d3920b6`, application revision `a0f5540`).  
**Local checkout:** last user log showed `d3920b6` on the feature branch; synchronize main before further work.

## Current implementation

AudioPlayer is a C#/WPF/.NET 10 Windows audio player with an optional WebView2 Script companion window. The reader discovers ReadArticles-format Script JSON in the loaded audio directory, displays read-only text, progressively colors timed words and follows a continuous time-position trajectory. Manual wheel (25% displacement) and scrollbar navigation share inverse interpolation and a 150 ms debounce; the host audio engine is authoritative. The reader cannot minimize/maximize independently; minimizing the owner closes it first. Restore does not reopen it automatically.

Settings use a fixed 480 × 430 DIP dialog with 音频设置 and 字幕参数 tabs, rounded normal-weight inputs, five swatches per color group and no sample preview. Font sizes 17/18/19/20 DIP, default 17; line height default 1.6, paragraph spacing 16, indent 2 em. Retired valid settings normalize in memory on load; explicit saves retain the existing atomic workflow. See [Configuration](Configuration.md), [program overview](program-overview.md), [project contract](project-contract.md) and [Publishing](Publishing.md).

## Verification

PR #17 was squash-merged into main at `5b5c428` on 2026-11-25 after explicit user authorization. The accepted feature head `d3920b6` passed [Feature validation #251](https://github.com/chenjing2000/audioplayer/actions/runs/45517740732), including Core, workflow smoke, WPF UI, portable publish and published EXE checks. The user confirmed the local published EXE rerun passed 4/4 at d3920b6 using the unchanged application from a0f5540. Combined final-code local evidence: 59 Core + 307 UI + 5 publishing safeguards + 4 published EXE checks = 375 passed, zero outstanding failures, across two runs (not a single full rerun). The 22 reader, 14 settings and 7 final typography manual checks passed across successive revisions. Optional MP3 checks were skipped; temporary WebView2 test-cache cleanup warning and unverified mixed-DPI/device-failure cases remain recorded. The two earlier automated failures did not recur; their exact original causes were not established. No release tag or release assets were requested.

Post-merge CI is separate from the accepted feature CI; consult GitHub Actions for its current result. Detailed chronological evidence is preserved in [Testing](Testing.md) and Git history.

## Next action

Synchronize the local main branch, verify the working tree, and remove the merged feature branch only after confirming that no local changes or unpushed commits remain.

```powershell
cd C:\Projects\AudioPlayer
git status --short
git fetch origin --prune
git switch main
git pull --ff-only origin main
git log -1 --oneline
.\tools\Test.ps1 -Suite Full
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

Follow root AGENTS.md and WindowsAppPlaybook. Preserve user files, discuss new features before implementation, use English commit messages and avoid unnecessary complexity. Future feature merges require explicit authorization. No release tag authorized.
