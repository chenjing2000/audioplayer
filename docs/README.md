# AudioPlayer documentation

当前行为以 [程序说明](program-overview.md)、[项目契约](project-contract.md) 和实际源码为准。历史规格用于追溯设计，不应覆盖后来合并的规则。

| 文档 | 用途 |
| --- | --- |
| [HANDOFF](HANDOFF.md) | 最新已确认的 `main` 基线、未完成验证与下一轮开发 |
| [Program overview](program-overview.md) | 当前功能、模块和运行逻辑 |
| [Project contract](project-contract.md) | 技术、数据安全与项目约束 |
| [Getting Started](Getting-Started.md) | 最简运行说明 |
| [Configuration](Configuration.md) | 当前五项配置及安全保存 |
| [Testing](Testing.md) | 测试命令、历史结果和未完成验收 |
| [Publishing](Publishing.md) | 便携发布、EXE/安装说明交付 |
| [FFmpeg setup](FFmpeg-Setup.txt) | 运行时可选依赖的安装说明 |
| [Release Notes](Release-Notes.md) | 下次发行包的发布说明 |

## 历史规格与审查证据

- [长静音裁剪原始规格](specs/Long-Silence-Trimming.md)：PR #1 阶段的 PCM 规则；旧 M4A 编码与输出命名已被后续 PR 取代
- [结果与 Toast 重构规格](specs/Results-Cache-Toast-Refactor.md)：PR #2 的原始规格；其旧 `silence.json` 路径已被 PR #10 取代
- [FFmpeg M4A stream-copy 规格](specs/FFmpeg-M4A-Stream-Copy.md)：PR #8 引入的 AAC 处理规则
- [2026-10-13 代码审查](Code-Review-2026-10-13.md) 与 [体检记录](Health-Check-2026-10-13.md)：仅作为当时的事实记录，不代表当前提交重新验收

已完成的实施计划不再作为日常维护入口；其历史内容可通过 Git 提交记录查看。最新可核实的已合并基线是 PR #10 的 `main@2be037f`，对应 [main CI #45182410792](https://github.com/chenjing2000/audioplayer/actions/runs/45182410792) 通过。后续修复分支的检查结果应另行记录，不能借用上述旧 CI。
