# Application overview

AudioPlayer is a local Windows application built with C#, .NET 10, and WPF. It scans one folder for WAV, MP3 and supported M4A files, plays them through NAudio, and lets the user move between silence midpoints detected from PCM energy. TagLibSharp reads title, album, and duration. Settings stay beside the executable.

MainWindow coordinates user actions, playback, analysis, and shutdown. AudioPlayer owns the reader and output device; SilenceDetector reads its own PCM stream without changing playback position; AudioLibrary scans and sorts filenames, then reads each track's metadata. SilenceSettings defines the five parameters and their valid ranges. SettingsStore owns JSON persistence, and SilenceSettingsWindow edits values without saving files or running analysis. AudioTrack supplies playlist data and binding notifications; SilenceRange supplies interval data.

The concrete classes collaborate directly. Background analysis and playback stop confirmation justify the existing asynchronous coordination. This application has no need for a view-model framework, dependency injection container, event bus, or generic repository at its current scale.

The unified tooling wraps the existing test runners and portable publishing configuration. It does not replace the application algorithms or file formats. Test data is isolated from user data and UI/native-input checks run sequentially.

See [program-overview.md](program-overview.md) for operations and algorithm details, [Code Review](Code-Review-2026-10-13.md) for the current maintenance assessment, [Configuration](Configuration.md) for settings, [Testing](Testing.md) for verification, and [Publishing](Publishing.md) for distribution.


## Long-silence trimming (merged to main on 2026-11-23)

`SilenceTrimmer` uses `SilenceDetector` results, deletes central intervals on the source PCM timeline, independently fades the kept sides, and writes a validated WAV/MP3/AAC-M4A result without modifying the source. The UI keeps trimming and playback tasks separate. See [specification](specs/Long-Silence-Trimming.md) and [implementation plan](guides/Long-Silence-Implementation.md). M4A real-device trimming and listening acceptance passed; real long-pause MP3/WAV checks remain deferred. The [results/Toast refactor](specs/Results-Cache-Toast-Refactor.md) is implemented on its feature branch but is **not yet merged or real-media accepted**.
