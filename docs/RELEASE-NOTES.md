# AIUsage 1.4.0 for Windows — Claude plan limits through Claude Code's status line

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged: no account client, credential reader, provider requests, model calls, updater or subprocess integration.

## New

- **Claude plan limits (optional).** Claude Code's documented status line feature passes your Pro/Max plan usage (5-hour and weekly windows, with reset times) to a command you configure. Make `AIUsage.exe --claude-statusline` that command and AIUsage shows those limits in a **Claude plan limits** card. Settings → Claude Code shows the exact entry to add to `%USERPROFILE%\.claude\settings.json`. Claude Code also displays the model and your limits in its status line.
- **Separate limit cards.** In the **All** view, Codex limits and Claude plan limits are now two clearly separated cards.

## Privacy and Anthropic rules

AIUsage still never reads Claude credentials, never signs in, never contacts Anthropic and never automates Claude Code. The limits come only from what Claude Code itself passes to the status line command on this PC. The status line mode opens no window, keeps only the limit percentages and reset times (`%LOCALAPPDATA%\AIUsage\claude-limits.json`) and discards everything else Claude Code sends (working directory, transcript path, session id, costs). AIUsage never edits Claude Code's settings; you add the entry yourself. AIUsage is not affiliated with or endorsed by Anthropic.

## Download and upgrade

Use `AIUsage-1.4.0-win-x64.zip` on Intel/AMD or `AIUsage-1.4.0-win-arm64.zip` on Windows ARM. Exit the older app from its tray menu, extract **every file** into a new local folder and run `AIUsage.exe`. Keep the DLLs and the `es` subfolder beside the executable. Existing settings are preserved. If you configured the status line with an older folder, update its path. Never delete `.codex` or `.claude`.

## Evidence and limits

Test logs, package checks, build information and hashes accompany this release. New regression tests cover parsing the documented status line input, keeping only rate limits, preserving the last snapshot when limits are absent, rejecting malformed, oversized or hostile input (including terminal control characters), ignoring tampered snapshots and generating the settings entry. x64 execution uses synthetic data; ARM64 is compiled and inspected, not executed. Binaries remain unsigned. This is not legal certification, provider approval or a ban guarantee.

## Previous release

1.3.0 (optional Claude Code usage) remains available under its tag; its notes are in the repository history.
