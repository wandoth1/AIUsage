# First-audit remediation — historical record

Reviewed on 2026-10-01 against AIUsage `v0.1.0-r1`, commit `db62c5a548452e2eb5fbb2d1ed7be95a0028d29c`. Corrections were implemented in [PR #1](https://github.com/wandoth1/AIUsage/pull/1) and released in **0.1.1**. **AIUsage 1.0.0 retains those corrections**, with the large-message fix described in [AUDIT-FOLLOWUP.md](AUDIT-FOLLOWUP.md), localization and independent versioning.

The private report was checked against source, Codex's format and official pricing references. The original report, private paths and credentials are not published. This English document preserves the historical disposition; it does not claim a new independent full audit of 1.0.

## Tests-first evidence

Thirteen independent synthetic reproductions were added with production unchanged. [Run 36879730529](https://github.com/wandoth1/AIUsage/actions/runs/36879730529) failed all thirteen while passing the original 48 tests. The independent audit runner was then expanded to 45 cases with independently calculated expectations.

An additional test exposed Windows rename contention even with unique temporary filenames. In-process writers were serialized without weakening that assertion. The published [0.1.1 run](https://github.com/wandoth1/AIUsage/actions/runs/36885332740) passed all 93 tests and its x64 WPF checks. Subsequent review nevertheless found G-1, illustrating that a passing suite is not proof of complete correctness.

## Disposition

| Finding | Correction/decision | Verification or remaining limit |
|---|---|---|
| H-01 · Codex/Spark quotas | Separate providers by limit ID/name and duration-based window identity. | Tests distinguish Codex 20% from Spark 90%, equal labels/different IDs, and weekly windows changing slots. |
| H-02 · Persistent priority | Complete snapshots without a tier reset to standard; partial omissions do not. Ignore copied snapshots owned by another thread. | Official schema and fixtures; no real-account fast-mode toggle. |
| H-03 · Sol 5.6 price | Updated to 4 / 0.4 / 20 USD per million as observed on 2026-10-01, with a promotional note. | 200K input plus 100K output estimates $2.80 rather than $4.00. Review note from 2026-11-22; no invented expiry/new price or historical reconstruction. |
| H-04 · Context occupancy | Total-only context-fill records without input/output/reasoning usage are not billed. | Independent context-window-fill reproduction. |
| H-05 · Oversized metadata | Conservative quarantine for unreadable accounting/session metadata. | Ambiguous child history is not treated as root usage. G-1 later narrowed classification so ordinary large conversations do not trigger this mitigation. |
| H-06 · Missing started_at | Keep replay gate closed and warn until a reliable live-task start is known. | Do not substitute potentially rewritten log timestamps. Some new usage may remain excluded. |
| H-07 · Global deduplication | Scope by session/account identity and cumulative counters; use sequence when totals are absent. | Independent sessions with equal timestamps/counts both survive. Files without IDs are not assumed copies. |
| H-08 · Custom prices | Normalize keys, require Input/Cached/Output, reject duplicate/unknown/invalid fields and bound file size. | Case, typo, omission and duplicate tests. |
| H-09 · Unverified aliases/rules | Show qualifications per model and in CSV; explicit custom prices take precedence over aliases. | Future reserve/auto-review models are not certified as Luna. Unconfirmed multipliers retain notes. |
| H-10 · Incomplete catalog | Optional expansion not implemented. | Unknown models retain tokens and visibly incomplete cost, not invented fallback prices. |
| H-11 · Cache version | Reject missing/old schemas and rebuild from originals. | Schema 2 accompanied the first corrections; 1.0 uses schema 4 for neutral labels and G-1 recovery. |
| H-12 · Large conversation warnings | Recognizably irrelevant records do not imply missing accounting. | Initial handling covered response_item; G-1 extends it to known non-accounting event_msg subtypes. |
| H-13 · EOF without newline | Preview complete stable JSON without advancing the committed checkpoint. | Append retracts/reprocesses it; restart/newline tests. |
| H-14 · Metadata-preserving rewrites | Manual Rebuild reading cache action. | No full-file hash on every refresh; old rewrites preserving size/time/prefix may need rebuilding. |
| H-15 · Invalid auth headers | Validate before constructing headers; sanitize errors and bound authentication reads. | Invalid characters never reach HTTP, and tokens do not enter error messages. |
| H-16 · Quota identity | Discard responses if credentials change; do not mix online/local windows; pseudonymize known local accounts. | Token history still aggregates by folder. Missing identities cannot be reliably separated; observation time remains visible. |
| H-17 · Retry behavior | One-minute minimum, serialized requests, progressive backoff and Retry-After. | Offline controlled-clock tests for 429/5xx, changed credentials and concurrent calls. |
| H-18 · WPF refresh | Calculate/reuse aggregates off the UI thread and preserve scroll/focus. | Visual tree is still rebuilt; not a full MVVM migration or performance guarantee. |
| H-19 · Multiple monitors/DPI | Still pending manual validation and monitor-aware placement improvements. | Not claimed fixed or hardware-tested. |
| H-20 · Culture/CSV | Coherent UI formatting and invariant comma-delimited CSV. | In 1.0 presentation explicitly follows en-US/es-ES; Windows determines the time zone. Excel double-click import is not guaranteed. |
| H-21 · Empty dotnet test success | AIU0001 directs contributors to executable runners with dotnet run. | CI verifies the guard on all runners. |
| H-22 · Build chain | Pin Actions by SHA; no persisted checkout credentials; do not cancel a main release on a new push. | No administrative permissions or extra external services. |
| H-23 · Names/provenance | Derive ZIP names from RELEASE_TAG; validate versions; provide BUILD-INFO and SHA-256. | Not signing or a cryptographic attestation. 1.0 adds manifest checks and tracked-source ZIP. |

Additional fixes recognize legacy `resets_in_seconds`, preserve settings-load errors, give demo mode a separate instance and serialize JSON writes using unique temporaries. This is not a distributed transaction across RDP sessions; external contention may still produce recoverable cache errors.

## References inspected

- Codex: `openai/codex@ecc78e4cf5607ecf5f080d682eae0ecb650868ae`, `codex-rs/protocol/src/protocol.rs`, including ThreadSettingsSnapshot, ThreadSettingsAppliedEvent and TurnStartedEvent. started_at is optional; copied snapshots may retain their original owner.
- Initial adaptation: OpenUsage v0.7.12 (`3b84fec518d5b3775adb93456fa8af7330c852d5`), not presumed infallible.
- Prices: https://developers.openai.com/api/docs/pricing and model pages in `pricing.json`, consulted on 2026-10-01. Unconfirmed model-specific rules remain qualified.
- MSBuild runner guard: https://learn.microsoft.com/en-us/visualstudio/msbuild/target-build-order . Tests use dotnet run, not implicit VSTest discovery.

## Still outside verified scope

Real-account use, full reconstruction of ambiguous subagents, token attribution by account, every mixed-DPI/multiple-monitor/Explorer/RDP/accessibility scenario, ARM64 hardware execution, signing, automatic updates and additional providers. Earlier tags and the privacy of the supplied reports are preserved.
