# Privacy and local data — 1.4.0

AIUsage is local-only. The authenticated client was deleted in 1.1.0; there is no OAuth, API-key, app-server, cookie or credential-store integration. No analytics, upload, automatic update check or model call is implemented. It does not read auth.json, launch Codex or modify original Codex logs/configuration.

## Inputs and storage

Resident local JSONL rollouts are read to identify usage/session metadata. Conversation, image and tool-result content is transient, not cached. Direct-folder fallback accepts only rollout-*.jsonl; history.jsonl is excluded even inside sessions directories. Use only records you are authorized to process.

Settings, optional price overrides and metadata cache live under `%LOCALAPPDATA%\AIUsage`. The cache includes timestamps, models, tokens, tiers, counters, parser/session state, plain session IDs and recorded limits. Recognized account identifiers are hashed into correlatable pseudonyms, **not guaranteed anonymity**. Protect activity data with suitable OS controls. No collection server or AIUsage account exists.

Schema 5 checkpoints use compact streamed JSON manifests and immutable event pages. Every file shares a 32 MiB read/write limit; a missing/corrupt page invalidates the entire checkpoint and triggers reconstruction from accessible originals. Events and total cache storage remain proportional to the history. Cache housekeeping does not promise a precise legal retention deadline.

CSV exports occur only on command in the local exports subfolder. They contain day/model aggregates and pricing qualifications, not conversations or credentials. Text cells are formula-escaped. User notes are preserved and may contain personal data; inspect before sharing. No export is automatically opened or uploaded. Prices are edited and validated inside AIUsage; no external editor or shell dialog is launched.

## Claude Code (optional, off by default)

When **Settings → Claude Code → Include Claude Code usage** is enabled, AIUsage reads the transcripts Claude Code already writes on this PC: `*.jsonl` files under `projects\` of `CLAUDE_CONFIG_DIR`, otherwise `%USERPROFILE%\.claude`. Nothing else in that folder is opened: not `.credentials.json`, `history.jsonl`, settings, todos or any other file. Lines without a usage block (prompts, tool output) are skipped before parsing; from assistant records only the timestamp, model, token counters, `speed` and `inference_geo` are used, and message/request/session ids are kept only as SHA-256 digests for deduplication. The cache in `%LOCALAPPDATA%\AIUsage\cache-claude` holds those values and nothing else.

AIUsage never uses Claude credentials, never signs in to Claude, never calls Anthropic services and never automates Claude Code. Claude plan limits are never fetched from Anthropic. They appear only if the user makes AIUsage Claude Code's status line (`statusLine` in Claude Code's settings, a documented Claude Code feature): Claude Code then runs `AIUsage.exe --claude-statusline` locally and passes session JSON on stdin. AIUsage keeps only `rate_limits` (5-hour, weekly and, behind a gateway, spend-limit percentages and reset times) in `%LOCALAPPDATA%\AIUsage\claude-limits.json`, prints the model name and those percentages for Claude Code to display, and discards everything else (working directory, transcript path, session id, costs). AIUsage never edits Claude Code's settings itself; Settings shows the entry for the user to add. Costs are estimates at Anthropic's published API list prices (platform.claude.com pricing, checked 2026-10-01); with a Pro or Max subscription they are not what you pay. Server-tool charges such as web search are not included. Claude Code deletes its own transcripts after its retention period (30 days by default), so older usage disappears from AIUsage too.

## Settings and upgrade

Supported settings are read before attempting to remove the legacy OnlineQuota field. That migration can write AIUsage's settings automatically. A write failure preserves the selected folder and supported in-memory preferences with a warning. Unreadable/corrupt settings pause scanning until explicit folder confirmation; no default source is silently substituted. A missing file on first use still permits the documented default source. Automatic Codex-folder detection resolves the same documented default and lists only rollout file names and modification dates to report how many recent sessions it holds; it opens no log contents, and while scanning is paused after a settings error it runs only when you choose **Use detected folder**. A setting that points to the Codex application install folder (which never holds logs) is cleared at startup with a visible notice.

The new local-only mutex namespace never silently activates a program holding the legacy namespace. It shows a warning and exits without signalling or killing that program. A previously running older executable is not changed by downloading a new release. Exit it and update shortcuts.

## Runtime-owned and OS behavior

The self-contained folder loads native libraries beside the app rather than self-extracting a bundle to `%TEMP%\.net\AIUsage`. Older releases can have left that AIUsage-specific cache. After exiting all versions, it may be deleted separately; do not remove other applications' `.net` directories.

The standard .NET local diagnostics IPC channel can exist subject to OS permissions. It is not disabled by this release and is not an Internet client. `System.StartupHookProvider.IsSupported` is false in the shipped runtimeconfig. These settings do not stop an attacker who can replace binaries/configuration or control the OS. No system-wide security/environment setting is modified.

Network/UNC/WSL paths, mapped drives, device paths, links/junctions and nonresident remote-storage files are rejected or skipped. This is best-effort application enforcement, not a firewall or hostile-filesystem sandbox. Windows, antivirus, storage drivers, backup and cloud-sync programs may create files or communicate independently. Avoid cloud-sync/network-backed locations for strict offline use. Not every filesystem race or virtualization scheme is detected.

## Verification and removal

Tests inspect our actual published assemblies and source, exercise synthetic locked credentials, original-file hashes, settings recovery, export and pricing, and operate the x64 executable through its ordinary entry point and timers. The offline runner observes .NET network-start events in its own process; it is not external tracing of WPF or proof that no failed file open was attempted. The fixed, source-pinned theme stylesheet is parsed from an embedded constant, never external XAML. Build/restore/license/publication tools use the internet, separately from the installed app.

Choose Exit, remove the extracted app folder and optionally `%LOCALAPPDATA%\AIUsage` to uninstall and remove its activity metadata. Settings → Rebuild reading cache removes only AIUsage cache JSON. Original Codex data must not be deleted. OS/security caches and previous runtime extraction remnants require separate consideration.

Local processing does not remove every legal or contractual obligation, particularly for workplace/shared logs or commercialization. No provider approval, anonymity guarantee, legal certification or account-enforcement guarantee is made.
