# Getting started

Use Windows x64 with a compatible .NET 10 Windows Desktop Runtime x64. End users do not need the SDK; the ordinary .NET Runtime does not include WPF.

Launch dist/EnglishAudioPlayer.exe or the EXE downloaded from GitHub Releases in a writable directory. Select a local folder of WAV, MP3 or compatible M4A audio. Double-click to play and use Left/Right to navigate silence midpoints. Select a row with validated silence information and compressible pauses, then click the scissors icon. The new file uses `<stem>.lesssilence.<ext>`, or ` (1)`, ` (2)` etc. if necessary; existing audio is never overwritten. The gear opens “设置” (Settings). Audio loading automatically reuses or regenerates `results/<full audio filename>/silence.json`; there is no separate detection button. This is currently the feature-branch behavior. Audio remains external user data.

Settings are stored beside the EXE as EnglishAudioPlayer.settings.json. See [Configuration](Configuration.md) for naming and recovery. Close the program before replacing the EXE and preserve existing user files.

Developers need .NET 10 SDK (honor global.json where present). Use [Testing](Testing.md) for verification and [Publishing](Publishing.md) to build the portable EXE. Start with Core checks, then run UI/Full checks serially on the desktop.

## Detailed operation

- [Program operations and modules](program-overview.md)