# Configuration

The current configuration file is EnglishAudioPlayer.settings.json beside the actual executable. It follows the EXE directory rather than the working directory. For AudioPlayer, ReciteWords, and ReadArticles, renaming the EXE changes the .settings.json stem; MdictViewer always uses dictionaries.json.

SettingsStore uses the actual EXE name. Invalid settings are reported and retained; failed writes preserve the previous file.

## Ownership and updates

The application owns its configuration format. Publishing updates only the EXE and preserves configuration and other user files. Git and Release assets exclude personal settings and media. Do not delete dist to update a program. Use a writable application folder.

Acceptance tests launch a renamed EXE in a temporary directory, check configuration placement, and use an isolated runtime root to verify the missing-.NET instructions. Application-specific configuration recovery remains covered by the existing service/UI checks.

## Application-specific details

See [program-overview.md](program-overview.md) and [Testing](Testing.md).

## Long-silence trimming (current `main` behavior)

The settings dialog adds `AllowedSilenceSeconds` (default 2.000, at most three decimal places). It must be at least `MinimumSilenceMilliseconds / 1000`. The last confirmed values, not incomplete edits, drive each trim task. Legacy JSON without the new field is initialized to `max(2.000, MinimumSilenceMilliseconds / 1000)`; invalid explicit values are reported, not repaired silently.

The existing EXE-adjacent settings location is retained for backward compatibility and documented as a project-specific exception to WindowsAppPlaybook's default userdata path. See [trimming specification](specs/Long-Silence-Trimming.md).

## Approved next iteration (not yet implemented)

The [results/Toast refactor](specs/Results-Cache-Toast-Refactor.md) changes `AllowedSilenceSeconds` to default **2.0 s** with at most **one decimal place**; `2.` is normalized to `2.0` when confirmed, while an empty value keeps the settings dialog open with a red input border. The displayed minimum for a 350 ms minimum silence is 0.4 s. The input boxes share a rounded style and the window title becomes “设置”.

This is a **breaking change**: do not add old-JSON migration or repair logic, and do not silently delete/rewrite incompatible configuration. A numerically valid existing setting can still load if it satisfies the new schema and constraints. No change to the executable-adjacent settings location is proposed. Implemented behavior remains as described in the preceding section until this refactor is merged.
