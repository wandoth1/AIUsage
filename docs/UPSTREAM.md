# Provenance and adaptation

> Current product: AIUsage 1.1.0 is local-only. The initial upstream reference does not enable its network/authentication features. No upstream service client or credential store is used in the current application.

**AIUsage versions are independent of OpenUsage versions.** The reference below records initial provenance, not AIUsage's display version or an automatically updated dependency. See [VERSIONING.md](VERSIONING.md).

Initial reference: [robinebers/openusage v0.7.12](https://github.com/robinebers/openusage/tree/v0.7.12), published on 2026-09-17. MIT, copyright 2026 Robin Ebers. [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) reproduces the original notice in full.

| OpenUsage file | Inspected blob SHA | Adaptation in AIUsage |
|---|---|---|
| `Sources/OpenUsage/Providers/Codex/CodexLogFileParser.swift` | `323ee2afe1f91412800c74637fc1bacb19281a27` | Per-file models, counters, tiers and subagent replay gate. |
| `Sources/OpenUsage/Providers/Codex/CodexLogUsageScanner.swift` | `e042be80988c8dff75c760a7ac2aed885f820a60` | Active/archived discovery, deduplication, legacy fields, fork detection and review/reserve aliases. |
| `Sources/OpenUsage/Providers/Codex/CodexUsagePricing.swift` | `05a2f9ec00a6e7bef40c79929a9c48e0bb139060` | Per-request pricing, cache, priority applied once and long-context threshold. |
| `Sources/OpenUsage/Resources/pricing_supplement.json` | `46c546f76f84cb42c88cae6e00ff8472610fe361` | GPT-5.6 prices and Astra cross-check. |
| `docs/providers/codex.md` | `9ef01fe67f1905b480d43b7ef255e0ca82144486` | Initial quota formats and duration-based identity; the authenticated endpoint is not used in 1.1.0. |

Selected prices were checked against official OpenAI pages on 2026-10-01; sources and qualifications are recorded in `pricing.json` and the audit responses. This does not guarantee future prices or rollout formats.

## Deliberate differences

The GUI is rewritten in WPF, not SwiftUI compiled for Windows. The incremental C# cache stores only usage metadata. Upstream icons, PostHog, telemetry, updater and non-Codex providers were not copied. English/Spanish localization and independent release versioning are AIUsage features.

Missing models stay `unknown`, rather than defaulting to GPT-5. Missing totals use input plus output without adding reasoning twice. Unknown prices are not silently replaced. Repeated cumulative snapshots do not count twice. Audit fixes scope deduplication to session/account identity instead of assuming equal counts in unrelated sessions are copies.

Subagent replay seeds cumulative baselines without billing inherited history. Only a reliable live-task start clears the replay gate; ambiguous starts remain excluded with a warning. Oversized accounting/metadata can conservatively quarantine a file. Recognized large user messages/tool results are skipped without losing other usage. Stable complete EOF records are previewed and reprocessed on append. See [first remediation](AUDIT-REMEDIATION.md) and [follow-up correction](AUDIT-FOLLOWUP.md); upstream is not presumed infallible.

The optional authenticated client was removed in 1.1.0. Only quota snapshots already in local logs are interpreted. No credentials, token refresh, account calls, Codex file changes or reset claims are performed. Known local quota accounts are pseudonymized; token history still aggregates a selected folder, not account profiles. There is no pi/OpenCode, cloud or cross-device aggregation. Only resident local disks are accepted; network/WSL paths and reparse points are rejected or skipped. See [LOCAL-ONLY.md](LOCAL-ONLY.md).

`gpt-reserve` and `codex-auto-review` aliases are qualified compatibility assumptions, not new-model discovery or future-price contracts.

## Future upstream work

Adopting newer upstream changes is optional. Record any new tag/commit and affected components, retain notices, review privacy/Windows implications and pass independent tests before releasing under the next **AIUsage** version. There is no automatic synchronization or parity promise.
