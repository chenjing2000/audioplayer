# 统一发布流程

在仓库根目录执行，需要 Windows 和 .NET 10 SDK；先关闭正在运行的程序：

```powershell
.\tools\Publish.ps1
```

发布脚本调用 AudioPlayer/Properties/PublishProfiles/Portable.pubxml 中的 Portable 配置。本项目采用 Release、win-x64、依赖本机运行时的单文件发布，嵌入原生加载器，不裁剪、不压缩、不分发 PDB 或 XML 文档。正式输出为 dist/AudioPlayer.exe。用户需要 .NET 10 Windows Desktop Runtime x64，普通 .NET Runtime 不足以运行 WPF；不需要 SDK。

脚本先在 dist 下的唯一临时目录构建，排除调试符号和 XML 文档，确认剩余产物只有一个 EXE，再在同一卷中原子替换已有 EXE。构建失败、意外的额外依赖或 EXE 被占用时停止；保留旧程序、已有配置和其他用户文件。最终删除临时目录，不清理 bin/obj 构建缓存。同一项目不要同时执行发布、构建或测试。

若 Windows 执行策略阻止脚本，可仅对这次进程使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
```

验证发布保护逻辑，不需要实际编译程序：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Publish.ps1
```

测试只替代外部 dotnet 构建过程，实际运行发布脚本并操作临时文件，覆盖失败构建、意外 DLL、EXE 占用、用户配置及播放列表保留。正式 EXE 需单独验证，结果见[本次体检](Health-Check-2026-10-13.md)。

GitHub 分发统一通过唯一的 portable-* 标签：先审查、验证并提交源码，再推送标签。.github/workflows/release.yml 在 Windows 上执行同样的保护测试和发布脚本，使用 docs/Release-Notes.md 创建 Release 并上传 EXE。普通提交不会触发 Release；后续发新标签前更新发布说明。EXE 作为 Release 附件，不将 dist、用户配置、词库或媒体提交进 Git。

## Unified release verification

Run `tools/Test.ps1 -Suite Core` before publishing and `tools/Test-PublishedApp.ps1` afterward. The GitHub tag workflow now performs both steps before creating the Release. Local real-content Full checks are documented in [Testing](Testing.md).

## Optional FFmpeg setup TXT (implemented in PR #8)

The [M4A FFmpeg stream-copy feature](specs/FFmpeg-M4A-Stream-Copy.md) copies **only** [`FFmpeg-Setup.txt`](FFmpeg-Setup.txt) beside the real executable for local build output and `dist/` publishing. The repository's full `README.md` is **not** copied. The main app remains a framework-dependent single-file `AudioPlayer.exe`; the TXT is separate documentation, not a runtime dependency. Publish validation still rejects unexpected build outputs and preserves the existing EXE replacement and user-data safeguards.

FFmpeg GPL Shared executables/DLLs are **not bundled**, and ordinary publish/CI must not require downloading them. The user installs the optional dependency relative to the EXE at `runtimes/ffmpeg/win-x64/bin/`. This optional dependency was integrated in PR #8 and the publish workflow passed feature-branch Windows checks.

MSBuild copies `FFmpeg-Setup.txt` to development output; `Publish.ps1` copies it to `dist/`. The EXE remains framework-dependent and single-file; FFmpeg binaries are not bundled. Windows feature-branch acceptance verified the setup TXT and prior publish safeguards. The GitHub Release workflow now attaches both `AudioPlayer.exe` and `FFmpeg-Setup.txt` as separate assets. Release users should download both into the same directory; no FFmpeg binary is bundled. Existing older tags are not retroactively updated.


## Rename to AudioPlayer (PR #10)

The merged `main` publishes `dist/AudioPlayer.exe` from `AudioPlayer/AudioPlayer.csproj`. Older EXE files in `dist/` are not automatically removed or migrated. After acceptance, close any old instance, optionally copy previous settings to `AudioPlayer.settings.json` if the new file is absent, and manually remove obsolete executables if desired. The release workflow builds the EXE from a clean checkout.
