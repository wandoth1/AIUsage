# Privacy and local data — 1.1.0

AIUsage is local-only. The authenticated client from older versions was removed from source; it is not hidden behind a default-off checkbox. There is no API, OAuth, app-server, cookie or credential-store integration, no analytics, no uploads and no automatic update check. It does not read `auth.json`, launch Codex or call another AI. It never changes the original Codex files or configuration.

## Inputs and storage

Only user-accessible, resident local JSONL logs are read. Content is transiently processed to identify accounting/session metadata; conversation, image and tool-result content is not persisted. Keep logs from other people out of the selected folder unless you have the necessary authority to process them.

AIUsage stores its own settings, optional price overrides and metadata cache in `%LOCALAPPDATA%\AIUsage`. Cache data includes timestamps, model names, tokens, service tiers, counters, parser/session state and recorded limits. Recognized account identifiers from log metadata are hashed to separate local quotas; these are correlatable pseudonyms, **not anonymity**. Activity history is private even without conversation text. Users control local files and should protect them with appropriate OS access controls.

There is no application account or collection server. This does not make every use of local data exempt from privacy law. Workplace deployment, shared logs, redistribution or commercialization may require additional analysis of the actual processing and jurisdiction.

The working history is bounded by the scanner's current time window; cache maintenance does not promise an exact legal retention deadline. A user can clear the reading cache in Settings. To remove all AIUsage activity/settings/exports, exit the app and delete `%LOCALAPPDATA%\AIUsage`. To uninstall, also delete the extracted app folder. No original Codex file needs to be deleted.

## User actions

CSV exports are created only on command, under AIUsage's local `exports` subfolder with a unique filename. They contain day/model aggregates and pricing qualifications, not conversations or credentials. Text cells are escaped against spreadsheet formulas. Notes supplied by the user are preserved and may themselves contain personal data: review before sharing an export. No file is opened or uploaded automatically.

The built-in price editor validates JSON before replacing the local overrides file. There are no browser links, external editor/file-manager launchers or shell file pickers in the application. URLs in bundled pricing, license and documentation files are source references only; opening them separately is the user's action.

Migration discards the former OnlineQuota setting, including true values, and preserves supported preferences. If settings cannot be read or rewritten, safe local-only defaults are used. The removed client cannot be restored by any old setting. Exit the old executable before starting the new one; an already-running older version is not changed by downloading this release.

## Filesystem and operating-system boundary

Network/UNC/WSL paths, mapped network drives, device paths, reparse points (including links/junctions) and nonresident remote-storage files are rejected or skipped. Source files are opened with FileAccess.Read; AIUsage writes only its own settings/cache/prices/exports and explicit synthetic-test output. Do not place these locations inside cloud-sync or network-backed storage for an offline workflow.

These checks are not an OS firewall or hostile-filesystem sandbox. Storage drivers, Windows, antivirus, SmartScreen, backups or cloud-sync software may communicate independently. Attribute checks cannot defeat all filesystem races or detect every third-party virtualization scheme. No zero-network guarantee is made for the entire computer.

## Verification limits

Source and application-owned assembly guards reject network APIs, credential readers, subprocesses, dynamic assembly loading and non-allowlisted native imports. Tests use only synthetic fixtures, including inaccessible credentials/configuration. The offline test runner observes .NET HTTP/socket/DNS start events during its own synthetic operations; it is not an external packet capture. Bundled .NET/WPF libraries contain framework functionality that the app does not invoke. Build/restore/license/release scripts use the internet but are not run by the installed application.
