# Application overview

AudioPlayer is a local Windows application built with C#, .NET 10, and WPF. It scans one folder for MP3 files, plays them through NAudio, and lets the user move between silence midpoints detected from PCM energy. TagLibSharp reads title, album, and duration. Settings stay beside the executable.

MainWindow coordinates user actions, playback, analysis, and shutdown. AudioPlayer owns the reader and output device; SilenceDetector reads its own PCM stream without changing playback position; AudioLibrary scans and sorts filenames, then reads each track's metadata. SilenceSettings defines the four parameters and their valid ranges. SettingsStore owns JSON persistence, and SilenceSettingsWindow edits values without saving files or running analysis. AudioTrack supplies playlist data and binding notifications; SilenceRange supplies interval data.

The concrete classes collaborate directly. Background analysis and playback stop confirmation justify the existing asynchronous coordination. This application has no need for a view-model framework, dependency injection container, event bus, or generic repository at its current scale.

The unified tooling wraps the existing test runners and portable publishing configuration. It does not replace the application algorithms or file formats. Test data is isolated from user data and UI/native-input checks run sequentially.

See [program-overview.md](program-overview.md) for operations and algorithm details, [Code Review](Code-Review-2026-10-13.md) for the current maintenance assessment, [Configuration](Configuration.md) for settings, [Testing](Testing.md) for verification, and [Publishing](Publishing.md) for distribution.
