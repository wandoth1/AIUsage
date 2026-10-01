# Local-only architecture and boundaries — 1.4.0

The optional authenticated integration was removed in 1.1.0. Version 1.1.1 preserved that boundary and addressed the subsequent local-security audit; 1.2.0 kept it unchanged while adding metadata-only detection of the Codex data folder; 1.3.0 kept it unchanged while adding an optional, read-only Claude Code source; 1.4.0 keeps it unchanged while accepting Claude plan limits from Claude Code's documented status line, which Claude Code runs locally as `AIUsage.exe --claude-statusline` when the user configures it (see PRIVACY.md). AIUsage itself still creates no subprocess and has no network client. This is technical risk reduction, not legal certification, OpenAI approval or an account-enforcement guarantee.

## Absent capabilities

The application has no authenticated quota client, endpoint, credential parser, retry logic, account headers, OnlineQuota property or consent flow. It does not use Codex CLI, App Server, OAuth, API keys, browser automation or another provider as a substitute. Browser/file-shell buttons, shell pickers, external editors and subprocess creation remain removed. Old settings cannot restore deleted code.

Local features remain: read-only rollout accounting, deduplication, conservative subagent replay, historical limits, costs, charts, English/Spanish, themes, tray and local CSV. Missing or ambiguous data is visibly incomplete. Original Codex files are never repaired, rewritten or removed.

## Recovery and local paths

Legacy instance names are checked without signalling. A conflict produces a visible warning and exit code 2; the app neither activates nor kills that older process. Its new local-only identity is stable across subsequent maintenance releases.

Settings are decoded separately from migration writes. An unsuccessful rewrite preserves valid selected preferences. An unreadable/corrupt file pauses scanning until explicit folder confirmation; a genuinely absent first-use file retains the documented default. The active path is displayed.

LocalPaths validates resident local drives and path ancestors. UNC/WSL shares, mapped network drives, device/relative paths, reparse points and remote-recall placeholders are rejected or skipped. Source, settings, cache, price and export paths are checked. Direct rollout folders accept only rollout-*.jsonl, and history.jsonl is excluded. These are safeguards against unintended access, not a sandbox against malicious same-user races, arbitrary storage drivers or OS compromise.

## Runtime and caches

Since 1.1.1 the package is a self-contained folder deployment; native libraries do not use the former single-file extraction mechanism. Keep the entire package together. Existing TEMP extraction remnants from old versions are documented separately. Standard .NET local diagnostics IPC remains subject to OS permissions and is NOT disabled. StartupHookSupport is disabled in the shipped configuration, without changing machine-wide settings. A user able to replace that configuration or executable is outside this protection.

Schema 5 caches are streamed compact JSON. Large histories use immutable event pages and a manifest committed last, sharing a 32 MiB per-file read/write limit. An incomplete generation is discarded, never presented as partial successful accounting. Total history still uses proportional memory/storage; this is not a global resource quota. Cache rebuilding changes only AIUsage files.

## Evidence

Source and actual-published own assembly policies reject network/credential/process APIs, unexpected references, dynamic activation and unapproved native imports. The theme loader is an explicitly pinned, fixed embedded stylesheet with a Parse-only exception, not external XAML. Broader framework capabilities are not automatically capabilities exercised by our code.

Synthetic tests cover migration, locked files, source hashes, paths, exports and prices. Independent Windows UI Automation starts the published x64 EXE through its ordinary entry point, checks conflicts/settings/timers and a harmless hook with positive/negative controls. Separate demo/smoke checks cover language/theme rendering.

A test runner's .NET network events are not an external trace of WPF. Successful operation with locked files is not proof of zero failed file-open attempts. This maintenance release does not claim a new ETW/packet-capture audit. Historical 1.1.0 observations are not relabelled as new executions. ARM64 is compiled and inspected, not executed.

## License and legal scope

MIT copyright and permission notices for OpenUsage remain, with runtime/WPF/Windows Forms notices in every package. Independent versioning does not remove attribution. Compatibility names do not imply endorsement. Only process records you are authorized to use; local storage does not waive obligations concerning other people's data or organizational deployments.

Relevant sources include OpenAI's Europe Terms of Use, Codex authentication guidance, and the AEPD's privacy-by-design guidance. They are references, not approval of AIUsage:

- https://openai.com/policies/eu-terms-of-use/
- https://developers.openai.com/codex/auth/
- https://www.aepd.es/preguntas-frecuentes/2-tus-obligaciones-como-responsable-del-tratamiento/9-analisis-de-riesgos/FAQ-0224-que-es-la-proteccion-de-datos-desde-el-diseno-y-por-defecto

No blanket legal-compliance, ban-proof, vendor-approved or anonymity claim is made. Obtain appropriate jurisdiction-specific advice when a legal assurance is needed. Build/restore/publication use the internet separately from installed runtime behavior. Windows, security and synchronization software can communicate independently; avoid network-backed/synchronized locations for an offline workflow.
