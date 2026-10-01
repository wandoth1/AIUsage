# AIUsage 1.3.0 for Windows — optional Claude Code usage

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged: no account client, credential reader, provider requests, model calls, updater or subprocess integration.

## New

- **Claude Code usage (optional, off by default).** Enable it in Settings → Claude Code. AIUsage then reads the token counters from the transcripts Claude Code already keeps on this PC (`%USERPROFILE%\.claude\projects`, or `CLAUDE_CONFIG_DIR`), including subagents. A new selector shows **All**, **Codex** or **Claude Code**; the tray total covers both sources.
- **Official Claude prices.** Every current Claude model at Anthropic's published API list prices (checked 2026-10-01), including 5-minute and 1-hour cache writes, cache reads, fast mode on Opus 5.5 / 5 / 4.8 and the 1.1x US-only inference multiplier.
- **Accurate counting.** Requests that Claude Code logs more than once (resumed sessions, subagent sidechains) are counted once, using the same rules as ccusage/OpenUsage. Advisor iterations are priced under their own model.

## Privacy and Anthropic rules

AIUsage never opens Claude credentials (`.credentials.json`), `history.jsonl` or anything outside `projects\`, never signs in to Claude, never contacts Anthropic and never automates Claude Code. For that reason Claude plan limits are not shown: they would require your Claude account credentials, which Anthropic does not allow third-party apps to use. Check them inside Claude Code with `/usage`. Prompts and responses are discarded while reading; the cache keeps only timestamps, models, token counters and hashed ids. Costs are list-price estimates, not what a Pro/Max subscription costs. AIUsage is not affiliated with or endorsed by Anthropic.

## Download and upgrade

Use `AIUsage-1.3.0-win-x64.zip` on Intel/AMD or `AIUsage-1.3.0-win-arm64.zip` on Windows ARM. Exit the older app from its tray menu, extract **every file** into a new local folder and run `AIUsage.exe`. Keep the DLLs and the `es` subfolder beside the executable. .NET is included. Existing settings are preserved; Claude Code stays off until you enable it. Never delete `.codex` or `.claude`.

## Evidence and limits

Test logs, package checks, build information and hashes accompany this release. New regression tests cover Claude usage mapping and list prices, cache-write durations, fast mode and US-only inference, deduplication, advisor iterations, rejection of foreign records, reading only `projects\` transcripts, incremental reading and a cache without content or raw ids. x64 execution uses synthetic data; ARM64 is compiled and inspected, not executed. Binaries remain unsigned. This is not legal certification, provider approval or a ban guarantee.

## Previous release

1.2.0 (automatic Codex folder detection) remains available under its tag; its notes are in the repository history.
