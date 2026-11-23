# AudioPlayer documentation

These pages describe AudioPlayer. The documentation categories follow the shared project convention; application behavior and modules are documented here specifically for this player.

| Document | Responsibility |
| --- | --- |
| [Next-iteration refactor specification](specs/Results-Cache-Toast-Refactor.md) | **Implemented on feature branch; Windows acceptance pending:** single silence.json per audio, auto-detection, WPF Toast, settings and playlist changes |
| [Next-iteration implementation and acceptance](guides/Results-Cache-Toast-Implementation.md) | Staged development tasks, tests, branch and acceptance checklist |
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

The long-silence trimming feature was merged to `main` through PR #1 on 2026-11-23. The new results/Toast refactor has implementation and synthetic Core/UI regression coverage in `feature/results-cache-toast-refactor`; it is not merged to `main` and real-media/interaction acceptance is outstanding. The previous trimming specification is preserved as the completed baseline, with new requirements superseding only the relevant behaviors after implementation.

## Maintenance rules

Keep executable commands in Testing and Publishing; link them from usage guides. Keep settings rules in Configuration. Record dated results in Health Check rather than implying that an old result verifies later changes. Domain references describe file formats and behavior, not another publishing pipeline. Historical audits and plans remain historical and are not the current workflow entry point.
