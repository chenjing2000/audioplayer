# Configuration

The current configuration file is EnglishAudioPlayer.settings.json beside the actual executable. It follows the EXE directory rather than the working directory. For AudioPlayer, ReciteWords, and ReadArticles, renaming the EXE changes the .settings.json stem; MdictViewer always uses dictionaries.json.

SettingsStore uses the actual EXE name. Invalid settings are reported and retained; failed writes preserve the previous file.

## Ownership and updates

The application owns its configuration format. Publishing updates only the EXE and preserves configuration and other user files. Git and Release assets exclude personal settings and media. Do not delete dist to update a program. Use a writable application folder.

Acceptance tests launch a renamed EXE in a temporary directory, check configuration placement, and use an isolated runtime root to verify the missing-.NET instructions. Application-specific configuration recovery remains covered by the existing service/UI checks.

## Application-specific details

See [program-overview.md](program-overview.md) and [Testing](Testing.md).

## Long-silence trimming (historical `main` baseline)

The settings dialog adds `AllowedSilenceSeconds` (default 2.000, at most three decimal places). It must be at least `MinimumSilenceMilliseconds / 1000`. The last confirmed values, not incomplete edits, drive each trim task. Legacy JSON without the new field is initialized to `max(2.000, MinimumSilenceMilliseconds / 1000)`; invalid explicit values are reported, not repaired silently.

The existing EXE-adjacent settings location is retained for backward compatibility and documented as a project-specific exception to WindowsAppPlaybook's default userdata path. See [trimming specification](specs/Long-Silence-Trimming.md).

## Results/Toast refactor (implemented on feature branch; acceptance pending)

The [results/Toast refactor](specs/Results-Cache-Toast-Refactor.md) changes `AllowedSilenceSeconds` to default **2.0 s** with at most **one decimal place**; `2.` is normalized to `2.0` when confirmed, while an empty value keeps the settings dialog open with a red input border. The displayed minimum for a 350 ms minimum silence is 0.4 s. The input boxes share a rounded style and the window title becomes “设置”.

This is a **breaking change**: no old-JSON migration or repair logic, and no silent deletion/rewrite of incompatible configuration. Numerically valid existing settings may still load if they satisfy new rules. EXE-adjacent settings are unchanged. This behavior is in `feature/results-cache-toast-refactor`, not yet merged to `main`.
