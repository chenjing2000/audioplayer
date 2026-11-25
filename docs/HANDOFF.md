# AudioPlayer — development handoff

**Updated:** 2026-11-25 — PR #17 merged, post-merge Windows verification passed  
**Repository:** https://github.com/chenjing2000/audioplayer  
**Local checkout:** `C:\Projects\AudioPlayer`  
**Merged application baseline:** `5b5c428` (PR #17; accepted feature head `d3920b6`, application revision `a0f5540`).  
**Verified development baseline:** `8a165d1` on main (application unchanged from `5b5c428`); latest user log confirms local and remote synchronization at that revision. Only main remains; the feature branch was deleted locally and remotely.

## Current implementation

AudioPlayer is a C#/WPF/.NET 10 Windows audio player with an optional WebView2 Script companion window. The reader discovers ReadArticles-format Script JSON in the loaded audio directory, displays read-only text, progressively colors timed words and follows a continuous time-position trajectory. Manual wheel (25% displacement) and scrollbar navigation share inverse interpolation and a 150 ms debounce; the host audio engine is authoritative. The reader cannot minimize/maximize independently; minimizing the owner closes it first. Restore does not reopen it automatically.

Settings use a fixed 480 × 430 DIP dialog with 音频设置 and 字幕参数 tabs, rounded normal-weight inputs, five swatches per color group and no sample preview. Font sizes 17/18/19/20 DIP, default 17; line height default 1.6, paragraph spacing 16, indent 2 em. Retired valid settings normalize in memory on load; explicit saves retain the existing atomic workflow. See [Configuration](Configuration.md), [program overview](program-overview.md), [project contract](project-contract.md) and [Publishing](Publishing.md).

## Verification

PR #17 was squash-merged at `5b5c428` with explicit user authorization. Accepted feature head `d3920b6` passed [Feature validation #251](https://github.com/chenjing2000/audioplayer/actions/runs/45517740732). The 22 reader, 14 settings and 7 final typography manual checks passed across successive revisions (43 total).

The user-provided Windows PowerShell 5.1 log confirms that local main fast-forwarded to `8a165d1` (HEAD, origin/main and origin/HEAD matched), and that both local and remote `feature/script-companion-window` were deleted. The remote branch listing was independently verified: only main remains. At `8a165d1`, the complete local post-merge sequence passed: `Test.ps1 -Suite Full` (59 Core, 307 UI and 5 publishing safeguards, zero failures), `Publish.ps1` (successful win-x64 .NET 10 portable EXE at dist/AudioPlayer.exe), and `Test-PublishedApp.ps1` (4/4, zero failures). Total: 375 executed checks passed in this single verification sequence. Both earlier failing checks (WebView2 ready handshake and renamed EXE playlist restoration) passed. The WebView2 success message misleadingly includes the timeout fallback text; the assertion passed and subsequent reader tests passed. Optional real MP3 playback, MP3 metadata and MP3-dependent UI checks were skipped; temporary test WebView2 cache remained in use during cleanup (nonfatal). No post-merge workflow run was returned for either `5b5c428` or `8a165d1` at the latest check; pre-merge CI #45517740732 remains verified successful. No new release tag or assets were requested.

Detailed historical evidence and remaining unverified edge cases (including mixed DPI and actual device failure) are preserved in [Testing](Testing.md).

## Next action

This development milestone is complete. Keep the accepted application unchanged unless a new requirement or reproducible defect warrants changes. Synchronize main before starting another feature; the following commands are the standard local verification procedure, not an outstanding acceptance requirement.

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
