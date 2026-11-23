# AudioPlayer documentation

These pages describe AudioPlayer. The documentation categories follow the shared project convention; application behavior and modules are documented here specifically for this player.

| Document | Responsibility |
| --- | --- |
| [Current development handoff](HANDOFF.md) | Latest merged baseline, verification, project boundaries, and next-session setup |
| [Results/Toast refactor specification](specs/Results-Cache-Toast-Refactor.md) | Implemented and accepted: one silence.json per audio, auto-detection, WPF Toast, settings and playlist changes |
| [Results/Toast implementation and acceptance](guides/Results-Cache-Toast-Implementation.md) | Historical implementation plan and completed acceptance checklist |
| [Long-silence trimming specification](specs/Long-Silence-Trimming.md) | Confirmed UI, trimming, codec and safety behavior |
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

Long-silence trimming and the results/Toast refactor were merged into `main` through PR #1 and PR #2 on 2026-11-23; focused simplification through PR #3; the approved unified v3 icons and later Play/Stop balance adjustments through PR #4 and PR #5. The latest feature baseline is `main@8aa15b1` (PR #5). [Windows main CI #45126656033](https://github.com/chenjing2000/audioplayer/actions/runs/45126656033) passed 45 Core and 111 WPF UI assertions plus workflow, portable publish, and published-EXE checks. User-reported Windows M4A/interaction/listening and icon visual acceptance passed; real long-pause MP3/WAV tests remain deferred. The older trimming specification is retained as historical baseline, with the results/Toast specification taking precedence where behavior changed.

## Maintenance rules

Keep executable commands in Testing and Publishing; link them from usage guides. Keep settings rules in Configuration. Record dated results in Health Check rather than implying that an old result verifies later changes. Domain references describe file formats and behavior, not another publishing pipeline. Historical audits and plans remain historical and are not the current workflow entry point.
