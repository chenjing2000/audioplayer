# Configuration

The current configuration file is EnglishAudioPlayer.settings.json beside the actual executable. It follows the EXE directory rather than the working directory. For AudioPlayer, ReciteWords, and ReadArticles, renaming the EXE changes the .settings.json stem; MdictViewer always uses dictionaries.json.

SettingsStore uses the actual EXE name. Invalid settings are reported and retained; failed writes preserve the previous file.

## Ownership and updates

The application owns its configuration format. Publishing updates only the EXE and preserves configuration and other user files. Git and Release assets exclude personal settings and media. Do not delete dist to update a program. Use a writable application folder.

Acceptance tests launch a renamed EXE in a temporary directory, check configuration placement, and use an isolated runtime root to verify the missing-.NET instructions. Application-specific configuration recovery remains covered by the existing service/UI checks.

## Application-specific details

See [program-overview.md](program-overview.md) and [Testing](Testing.md).

## Long-silence trimming

The settings dialog adds `AllowedSilenceSeconds` (default 2.000, at most three decimal places). It must be at least `MinimumSilenceMilliseconds / 1000`. The last confirmed values, not incomplete edits, drive each trim task. Legacy JSON without the new field is initialized to `max(2.000, MinimumSilenceMilliseconds / 1000)`; invalid explicit values are reported, not repaired silently.

The existing EXE-adjacent settings location is retained for backward compatibility and documented as a project-specific exception to WindowsAppPlaybook's default userdata path. See [trimming specification](specs/Long-Silence-Trimming.md).
