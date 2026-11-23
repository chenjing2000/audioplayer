# AGENTS.md — EnglishAudioPlayer

本项目遵循 [WindowsAppPlaybook](https://github.com/chenjing2000/windowsappplaybook) 的第一性原理与最小必要设计，项目专属行为以 [docs/project-contract.md](docs/project-contract.md) 为准。

- 现行权威规格：[docs/specs/Results-Cache-Toast-Refactor.md](docs/specs/Results-Cache-Toast-Refactor.md)；分阶段实施与验收：[docs/guides/Results-Cache-Toast-Implementation.md](docs/guides/Results-Cache-Toast-Implementation.md)。原有 [长静音规格](docs/specs/Long-Silence-Trimming.md) 中的 PCM/媒体安全规则继续有效。
- 构建：`dotnet build EnglishAudioPlayer/AudioPausePlayer.csproj -c Release`。
- 测试：`tools/Test.ps1 -Suite Core|UI|Full`；发布：`tools/Publish.ps1`。
- 保护源音频、已有输出、EXE 同目录用户配置与原有播放器功能；未知格式不能静默转码或伪造成功。
- 先删除无用逻辑、合并和修改既有实现，仅在必要时新增功能类。没有授权不合并 `main`。
