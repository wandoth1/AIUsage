# AIUsage 1.0.0 for Windows

First release of the **independently versioned AIUsage 1.x line**, following our own 0.1.x development previews. AIUsage remains an independent Windows port of OpenUsage under MIT. Upstream versions record provenance, not AIUsage version numbers; future upstream improvements may be selectively ported or omitted.

## What's new

- English and Spanish application text: Settings, dashboard, tray menus, tooltips, consent dialogs, errors and pricing explanations.
- Persistent language selection: **Settings → Language / Idioma → English / Español / System default → Save and refresh**. No restart. Automatic selection uses Spanish for Spanish-language Windows, English otherwise.
- English README, privacy/security/build documentation, audit responses and release notes; explicit independent versioning/upstream policy.
- Consistent version 1.0.0 in the executable manifest, About, user agent and Windows package names.
- Localization tests, both-language/both-theme WPF checks and a tracked-source archive with build metadata and SHA-256 hashes.

## Second-audit corrections included

Fixed a regression in 0.1.1: a conversation event larger than 2 MB, such as an inline image or a paginated tool result, could incorrectly exclude the entire session. The parser now recognizes safe message types from the bounded JSON header without caching their content. Unknown or unreadable accounting/session metadata still triggers conservative exclusion with a warning.

Cache schema 4 automatically rebuilds older caches, including previously quarantined sessions. Added independent reproductions, migration/append/EOF/privacy checks and positive controls that retain protection against unreadable accounting. Sol 5.6's inherited long-context cache surcharge is now visibly qualified as unverified, consistently with Terra and Luna; numerical tariffs are unchanged.

All other accounting/security corrections from 0.1.1 remain. See `docs/AUDIT-FOLLOWUP.md` for evidence and decisions. The external follow-up audited 0.1.1 and identified this shared parser issue; it was not a full audit of the new bilingual GUI.

## Downloads and upgrade

Extract the **whole ZIP**, then run `AIUsage.exe`. The .NET runtime is included; no administrator rights or installer are needed.

- `AIUsage-1.0.0-win-x64.zip`: Windows on Intel / AMD.
- `AIUsage-1.0.0-win-arm64.zip`: Windows on ARM (cross-compiled, not hardware-tested).
- `AIUsage-1.0.0-source.zip`: tracked source from the build commit.

Exit the old app, extract into a new folder and run the new executable. Settings are retained; missing language preferences default to system selection. The reading cache rebuilds automatically. **Do not delete `.codex`.**

## Validation and limits

Publication is gated on the original regression, audit, localization and follow-up suites; executable-runner guards; Windows compilation; x64/ARM64 publishing; and the published x64 WPF smoke checks. Result files and `BUILD-INFO.json` accompany this release. Screenshots contain synthetic data, not a real account.

Codex is the only provider. Real-account and ARM64 hardware validation, comprehensive multiple-monitor/DPI, RDP and accessibility checks remain outstanding. Ambiguous subagents can still be excluded with a warning; token history aggregates accounts within a folder.

**Binaries are unsigned.** Hashes/build metadata are not publisher signatures. Do not disable Windows protections. There is no automatic updater or installer. Dollars are estimated API equivalents, not subscription charges or invoices. Original MIT attribution is preserved.
