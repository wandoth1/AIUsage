# AIUsage 1.6.1 for Windows — prices for every model Devin offers

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged.

## Fixed

- **`gpt-6-1-sol` showed "No price".** Devin writes OpenAI versions with a dash; AIUsage now maps Devin's ids to the catalog name (`gpt-6.1-sol`, `gpt-5.6-luna`…), so a model used from Codex and from Devin is one row with one price.
- **Priority and fast variants.** `…-priority` (OpenAI) and `…-fast` (Claude) ids are recognised as service tiers and cost twice the standard rate, as in Devin's table. `-thinking` and `-1m` variants use the base model's price.

## Prices

- **25 models added** from the per-token table in Devin's model documentation (checked 2026-10-11): SWE-1.6, SWE-1.7, SWE-1.7 Lightning, SWE-check, Adaptive, Gemini 3.1 Pro and 3.5–3.8 Flash, GLM-5.2, GLM-5.3 and GLM-5.3 Flash, Kimi K2.6, K2.7 and K3, DeepSeek V4 Flash/Pro and V4.1 Flash, Grok 4.5–4.7, Inkling, Nemotron 3 Ultra and Penguin. Each uses its regular rate; plan promotions (free SWE-1.7 or GLM-5.2, introductory Gemini Flash prices) are not applied.
- Devin's internal `compactor` model has no published rate and stays "No price".
- Custom prices may now set `CacheWriteMultiplier` to 0 for models that do not charge cache writes.

The Devin cache is rebuilt once (about a second). Use `AIUsage-1.6.1-win-x64.zip` or `AIUsage-1.6.1-win-arm64.zip`; update the Claude Code status line path if it points to an older folder.

## Included from 1.6.0: optional Devin usage

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged: no account client, credential reader, provider requests, model calls, updater or subprocess integration.

### New

- **Devin usage (optional, off by default).** Enable it in Settings → Devin. AIUsage reads the token counters of the Devin CLI and the Devin Desktop agent from Devin's local session database (`%APPDATA%\Devin\cli\sessions.db`) and adds **Devin** to the source selector next to Codex and Claude Code. All models Devin used are included (SWE-2, GPT, Claude), with cost by reasoning effort taken from the model name.
- **Exact counting.** Devin stores a response again when a conversation is compacted or branched; AIUsage counts each response once (in a real history the naive sum was about twice the actual usage).
- **SWE-2 list price** from Devin's model documentation (checked 2026-10-11): $3 input, $0.30 cache read and $15 output per million tokens. Devin plan promotions (free or discounted SWE-2) are not applied; figures are list-price equivalents, not your Devin bill.

### How the database is read

- Through `winsqlite3.dll`, the SQLite build that ships with Windows, loaded from System32 only. No SQLite binary and no third-party package is added to the AIUsage folder.
- Opened with SQLite's read-only flag; only `SELECT` statements are prepared and `sqlite3_exec` is not imported, so AIUsage cannot modify Devin's data. When Devin is not running the file is read as immutable and no side files are created; while Devin is running SQLite's normal shared read path is used.
- Only `message_nodes.row_id`, `created_at` and `chat_message` are queried, and from each model response only its role, model, token counters and message id (kept as a SHA-256 digest). Prompts, responses, titles, folders, tool calls, credentials and all other Devin files are never read.

### Limits

- Only local Devin CLI / Desktop agent sessions. **Devin Cloud sessions are not on this PC and are not included.**
- Devin's plan quota is not in the local database and is not shown.
- Cognition does not document this database; if a Devin update changes it, AIUsage reports that Devin usage could not be read instead of guessing. Nothing in Cognition's terms or acceptable-use policy addresses reading this local file; AIUsage is not affiliated with or endorsed by Cognition.

### Upgrade

Use `AIUsage-1.6.0-win-x64.zip` or `AIUsage-1.6.0-win-arm64.zip`. Existing settings are preserved and Devin stays off until you enable it. If Claude Code's status line points to an older AIUsage folder, update its path.

### Evidence

New regression tests build synthetic SQLite databases with Windows' own SQLite and cover token mapping and list prices for SWE-2, GPT and Claude models, counting stored copies once, reading an idle database without changing it or creating side files, reading a database in use through its write-ahead log, incremental reads, a cache without content or raw ids, replaced databases, incoherent caches, and non-database or malformed input. The offline checks pin the exact native entry points and require read-only SQL. x64 execution uses synthetic data; ARM64 is compiled and inspected, not executed. Binaries remain unsigned.

## Included from 1.5.3: Claude Haiku 5.5 prices

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged.

### Prices

- **Claude Haiku 5.5 added** (`claude-haiku-5-5`), from Anthropic's published API price list checked on 2026-10-08. It is priced by prompt length: up to 100,000 prompt tokens $0.10 input, $0.01 cache reads and $0.50 output per million tokens; above 100,000 prompt tokens every category costs 5x ($0.50, $0.05, $2.50). Cache writes keep the 1.25x (5 minutes) and 2x (1 hour) multipliers. AIUsage chooses the price per request from its total input (uncached input, cache reads and cache writes).
- **Claude Sonnet 5.5 cache reads** corrected from $0.20 to the current $0.10 per million tokens (0.05x input). Earlier estimates for Sonnet 5.5 were slightly high; history is re-estimated with the current catalog.
- Long-context multipliers are now per model (`LongInputMultiplier`, default 2; `LongOutputMultiplier`, default 1.5) and can be set in custom prices. Existing models and overrides behave as before.

History is always re-estimated with the current catalog, not historical prices. Use `AIUsage-1.5.3-win-x64.zip` or `AIUsage-1.5.3-win-arm64.zip`; update the Claude Code status line path if it points to an older folder.

## Included from 1.5.2: application icon

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged.

### Changed

- **`AIUsage.exe` has its own icon** (16 to 256 px): three rising bars on a dark rounded tile, the motif the tray icon already used. It is original artwork generated by `scripts/make-icon.py` (not part of the build) and contains no third-party logos. Windows may keep showing the old icon for a while because of its icon cache.

No accounting, privacy or security behaviour changed. Use `AIUsage-1.5.2-win-x64.zip` or `AIUsage-1.5.2-win-arm64.zip`; update the Claude Code status line path if it points to an older folder.

## Included from 1.5.1: readable tooltips in the dark theme

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged.

### Fixed

- **Tooltips were unreadable in the dark theme** (light text on the light system tooltip background), most visibly the new cost-by-effort tooltip. Tooltips now use the theme's colours in both themes. The reviewed theme stylesheet gains one style; its pinned hash is updated accordingly.

No accounting, privacy or security behaviour changed. Use `AIUsage-1.5.1-win-x64.zip` or `AIUsage-1.5.1-win-arm64.zip`; update the Claude Code status line path if it points to an older folder.

## Included from 1.5.0: cost by reasoning effort

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged: no account client, credential reader, provider requests, model calls, updater or subprocess integration.

### New

- **Cost by reasoning effort.** Hovering over a model in the "By model" card shows its tokens, estimated cost and records per effort level, highest first (`max`, `ultra`, `xhigh`, `high`, `medium`, `low`), with "not recorded" for older logs. Works for Codex (effort from `turn_context` and thread settings, exactly like the model) and Claude Code (the `effort` recent versions write on each response).
- Effort only changes how many tokens a request uses; prices per token are unchanged, and per-effort rows always add up to the model row.

### Privacy

Effort is a setting already present in the same local records AIUsage reads; no new file, field of conversation content or network access is involved. Effort values are kept only if they are short lowercase identifiers.

### Upgrade

Use `AIUsage-1.5.0-win-x64.zip` or `AIUsage-1.5.0-win-arm64.zip`. The first start rebuilds the reading caches once so older events get their effort; this can take a little longer with large histories. If Claude Code's status line points to an older AIUsage folder, update its path.

### Evidence and limits

New regression tests cover Codex effort from turn_context, thread settings and collaboration mode, Claude effort with valid, invalid and missing values, cache validation, deduplication of identical copies and per-effort totals that add up to the model totals across a period. x64 execution uses synthetic data; ARM64 is compiled and inspected, not executed. Binaries remain unsigned.

## Included from 1.4.1: clearer Claude plan limits instructions

Independent AIUsage versioning; MIT attribution to OpenUsage is preserved. The exclusively local design is unchanged.

### Changed

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

1.4.1, 1.4.0 and 1.3.0 remain available under their tags; their notes are in the repository history.
