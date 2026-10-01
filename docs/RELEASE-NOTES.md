# AIUsage 1.4.1 for Windows — clearer Claude plan limits instructions

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged.

## Changed

- **Step-by-step instructions in the Claude plan limits card.** When no limits have arrived yet, the card explains exactly how to enable them: copy the entry from Settings → Claude Code, paste it into the Claude Code settings file named there, and use Claude Code in a terminal.
- **Terminal only.** Claude Code runs its status line only in a terminal (the `claude` command); the Claude desktop app does not run it. The card, Settings and README now say so: limits are updated only while you use Claude Code in a terminal.

No accounting, privacy or security behaviour changed. Use `AIUsage-1.4.1-win-x64.zip` or `AIUsage-1.4.1-win-arm64.zip`. If you configured the status line with the 1.4.0 folder, update the path in Claude Code's settings to the new folder.

## Included from 1.4.0: Claude plan limits through Claude Code's status line

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged: no account client, credential reader, provider requests, model calls, updater or subprocess integration.

### New

- **Claude plan limits (optional).** Claude Code's documented status line feature passes your Pro/Max plan usage (5-hour and weekly windows, with reset times) to a command you configure. Make `AIUsage.exe --claude-statusline` that command and AIUsage shows those limits in a **Claude plan limits** card. Settings → Claude Code shows the exact entry to add to `%USERPROFILE%\.claude\settings.json`. Claude Code also displays the model and your limits in its status line.
- **Separate limit cards.** In the **All** view, Codex limits and Claude plan limits are now two clearly separated cards.

### Independent audit fixes

An independent audit of 1.3.0 and this release found no credential access, network calls, scraping or automation of Claude Code. Its findings are fixed:

- The status line entry is offered only for paths neither Git Bash nor PowerShell can interpret (letters, digits, `.`, `_`, `-`); otherwise Settings asks you to move AIUsage, for example to `C:\AIUsage`. Previously a path with spaces failed in PowerShell and shell symbols in a folder name could be interpreted.
- A transcript rewritten and extended after a previous read is now detected (sampled ranges and the last 64 KiB before the checkpoint) and read again.
- Cache entries and the limits snapshot are fully validated; anything incoherent is rebuilt or ignored, and window names and durations come from known ids only.
- Deduplication no longer merges different sessions that reuse a message id, matching current ccusage.
- A Codex folder error no longer stops Claude Code totals from updating, and vice versa.
- Settings names Claude Code's active settings file (including `CLAUDE_CONFIG_DIR`), and the status line output drops bidirectional and invisible Unicode format characters.

### Privacy and Anthropic rules

AIUsage still never reads Claude credentials, never signs in, never contacts Anthropic and never automates Claude Code. The limits come only from what Claude Code itself passes to the status line command on this PC. The status line mode opens no window, keeps only the limit percentages and reset times (`%LOCALAPPDATA%\AIUsage\claude-limits.json`) and discards everything else Claude Code sends (working directory, transcript path, session id, costs). AIUsage never edits Claude Code's settings; you add the entry yourself. AIUsage is not affiliated with or endorsed by Anthropic.

### Evidence and limits

Test logs, package checks, build information and hashes accompany this release. New regression tests cover parsing the documented status line input, keeping only rate limits, preserving the last snapshot when limits are absent, rejecting malformed, oversized or hostile input (including terminal control characters), ignoring tampered snapshots and generating the settings entry. x64 execution uses synthetic data; ARM64 is compiled and inspected, not executed. Binaries remain unsigned. This is not legal certification, provider approval or a ban guarantee.

### Previous release

1.4.0 and 1.3.0 remain available under their tags; their notes are in the repository history.
