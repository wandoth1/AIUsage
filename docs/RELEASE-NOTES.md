# AIUsage 1.2.0 for Windows — automatic Codex folder detection

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged: no account client, credential reader, provider requests, model calls, updater or subprocess integration.

## New

- **Automatic Codex folder detection.** Leaving Settings → Codex folder empty uses `CODEX_HOME`, otherwise `%USERPROFILE%\.codex`. Settings now shows the detected folder and how many sessions it holds from the last 32 days, with **Use detected folder** to apply it in one click.
- **Install folder misconfiguration fixed.** A Codex folder pointing at the folder where the Codex *application* is installed (for example `C:\Program Files\WindowsApps\OpenAI.Codex_…\app`) never contains logs and showed "No data". Such a setting is now cleared at startup so automatic detection applies, with a visible notice, and it is rejected when saved.
- **Folders without logs.** Saving a folder that contains no Codex logs offers the detected folder instead. When an explicitly selected folder yields no files, the dashboard shows the detected folder with a one-click button. Folders are never switched silently.

## Privacy

Detection uses the scanner's own discovery rules and lists only rollout file names and modification dates; it opens no log contents. After a settings error, scanning stays paused and detection runs only when you choose **Use detected folder**, which saves that folder explicitly.

## Download and upgrade

Use `AIUsage-1.2.0-win-x64.zip` on Intel/AMD or `AIUsage-1.2.0-win-arm64.zip` on Windows ARM. Exit the older app from its tray menu, extract **every file** into a new local folder and run `AIUsage.exe`. Keep the DLLs and the `es` subfolder beside the executable. .NET is included. Existing settings are preserved. Never delete `.codex`.

## Evidence and limits

Test logs, package checks, build information and hashes accompany this release. New regression tests cover install-folder recognition and correction, metadata-only folder inspection, suggestions and CODEX_HOME resolution. x64 execution uses synthetic data; ARM64 is compiled and inspected, not executed. Binaries remain unsigned. This is not legal certification, provider approval or a ban guarantee.

## Previous release

1.1.1 (local-security maintenance) remains available under its tag; its notes are in the repository history.
