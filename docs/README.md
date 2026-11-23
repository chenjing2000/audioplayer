# AudioPlayer documentation

These pages describe AudioPlayer. The documentation categories follow the shared project convention; application behavior and modules are documented here specifically for this player.

| Document | Responsibility |
| --- | --- |
| [Current development handoff](HANDOFF.md) | Latest merged baseline, verification, project boundaries, and next-session setup |
| [Results/Toast refactor specification](specs/Results-Cache-Toast-Refactor.md) | Implemented and accepted: one silence.json per audio, auto-detection, WPF Toast, settings and playlist changes |
| [Results/Toast implementation and acceptance](guides/Results-Cache-Toast-Implementation.md) | Historical implementation plan and completed acceptance checklist |
| [Long-silence trimming specification](specs/Long-Silence-Trimming.md) | Historical implemented baseline for NAudio/Media Foundation trimming |
| [FFmpeg AAC/M4A stream-copy specification](specs/FFmpeg-M4A-Stream-Copy.md) | Implemented in PR #8: optional FFmpeg, AAC stream copy, join accuracy and safety |
| [FFmpeg setup instructions](FFmpeg-Setup.txt) | Standalone installation instructions copied beside the EXE |
| [Trimming implementation plan](guides/Long-Silence-Implementation.md) | Implementation stages and acceptance requirements |
| [Project contract](project-contract.md) | Current application-specific standards and exceptions |
| [Getting Started](Getting-Started.md) | Runtime requirements, launch, and first use |
| [Application Overview](Application-Overview.md) | Purpose, modules, and application-specific architecture |
| [Configuration](Configuration.md) | Settings location, ownership, preservation, and recovery |
| [Testing](Testing.md) | Core/UI/Full entry points, media preparation, and published acceptance |
| [Publishing](Publishing.md) | Portable profile, staged updates, and GitHub Release workflow |
| [Release Notes](Release-Notes.md) | Distribution changes and runtime requirements |
| [Health Check](Health-Check-2026-10-13.md) | Dated verification evidence and limitations |
| [Code Review](Code-Review-2026-10-13.md) | Module boundaries, local simplification, retained complexity, and current regression evidence |

## Application-specific references

- [Program operations and modules](program-overview.md)

## Version and implementation status

Long-silence trimming and the results/Toast refactor were merged into `main` through PR #1 and PR #2 on 2026-11-23; focused simplification through PR #3; the approved unified v3 icons and later Play/Stop balance adjustments through PR #4 and PR #5. The latest merged feature baseline is `main@4b4afc6` (PR #8). [Windows main CI #45126656033](https://github.com/chenjing2000/audioplayer/actions/runs/45126656033) passed 45 Core and 111 WPF UI assertions plus workflow, portable publish, and published-EXE checks. User-reported Windows M4A/interaction/listening and icon visual acceptance passed; real long-pause MP3/WAV tests remain deferred. The older trimming specification is retained as historical baseline, with the results/Toast specification taking precedence where behavior changed.

[PR #7](https://github.com/chenjing2000/audioplayer/pull/7) approved the FFmpeg design and [PR #8](https://github.com/chenjing2000/audioplayer/pull/8) merged the implemented M4A stream-copy path. Real Windows M4A playback/listening and user-reported acceptance passed; selected failure-path checks remain unverified (see [Testing](Testing.md)). Only `main` remains in the remote repository after branch cleanup.

## Maintenance rules

Keep executable commands in Testing and Publishing; link them from usage guides. Keep settings rules in Configuration. Record dated results in Health Check rather than implying that an old result verifies later changes. Domain references describe file formats and behavior, not another publishing pipeline. Historical audits and plans remain historical and are not the current workflow entry point.
