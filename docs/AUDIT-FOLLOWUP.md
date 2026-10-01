# Second-audit follow-up — included in AIUsage 1.0.0

Date: 2026-10-01. The supplied follow-up reviewed **0.1.1**, commit `ae72f4c3abeb3f1c946683a2e8e766992243e7e2`, and observed that the in-progress bilingual branch shared G-1. It was **not a complete audit of the new bilingual interface**. The original private report/workspace is not published.

## G-1: large conversations incorrectly excluded the whole session

The 0.1.1 oversized-record mitigation checked only the top-level JSON type. It treated every large event_msg as ambiguous accounting, set AccountingBlocked and cleared that file's cached usage. A pasted image/text or a large paginated tool result could therefore suppress earlier usage and later appends, despite being unrelated to accounting.

We independently reproduced this before changing the parser. [Commit b366961](https://github.com/wandoth1/AIUsage/commit/b366961442dd53d06da096bf3cc4b30deb8083ab) added five synthetic tests. [Run 36891781462](https://github.com/wandoth1/AIUsage/actions/runs/36891781462) failed the three conversation/append cases (0 rather than 3,300 tokens) and passed both positive controls for unreadable accounting/child metadata.

### Correction

A bounded Utf8JsonReader inspects both top-level `type` and direct `payload.type`. It does not search pasted text for discriminator strings. Recognized conversation/tool events, including user_message and item_completed, are skipped without clearing session accounting. Accounting types, unknown subtypes and unreadable headers remain conservative. The change does not increase record memory limits or persist conversation content.

The allowlist is based on Codex's persisted event variants. It is intentionally not a blanket exemption for every future event_msg. A malformed or unusually ordered record whose discriminator cannot be read inside the bounded prefix can still be excluded with a warning.

Cache schema **4** rejects older cached state, including prior false quarantines, and rebuilds accessible history from original logs. Codex files and credentials are not modified.

### Regression coverage

`tests/AIUsage.FollowupTests` includes the original reproductions plus realistic inline-image/paginated-tool fixtures, append and restart recovery, EOF without newline, old-cache recovery, known conversational variants, unknown/accounting positive controls, nested discriminator spoofing, BOM/escaped strings/partial UTF-8, and a check that large private messages do not enter the cache. Its separate executable result is included in release artifacts and gates publication.

## Other observations

| Observation | Decision |
|---|---|
| Sol 5.6 long-context cached-input surcharge was marked verified | Set LongCacheVerified to false, consistently with Terra/Luna. The official page specifies 2× input and 1.5× output but does not explicitly resolve the cache surcharge. Retain the inherited estimate with an English/Spanish warning, not an unsupported certainty. Numerical prices are unchanged; a $1.71 synthetic case verifies the qualification in both languages. |
| A normal OAuth token renewal discards an in-flight response | Keep this conservative identity check. It may postpone fresh limits until the next request, but does not change or refresh credentials. Documented in README. |
| Online quotas hide local windows absent from the response, including Spark | Retain separation: do not silently mix potentially different accounts. Documented in README. |
| Dark scrollbar style and WFO0003 warning | Cosmetic/manual-validation items, not addressed as part of the accounting fix. |
| Missing subagent started_at and metadata-preserving historical rewrites | Keep the documented conservative exclusion/manual cache rebuild. No invented consumption. |

## Primary references

- https://github.com/openai/codex/blob/ecc78e4cf5607ecf5f080d682eae0ecb650868ae/codex-rs/rollout/src/policy.rs : persisted legacy user/agent events and paginated ItemCompleted events.
- https://github.com/openai/codex/blob/ecc78e4cf5607ecf5f080d682eae0ecb650868ae/codex-rs/protocol/src/protocol.rs : EventMsg and message/item payloads.
- https://developers.openai.com/api/docs/models/gpt-5.6-sol : pricing wording rechecked on 2026-10-01. Cache interpretation remains explicitly qualified.

All execution evidence is synthetic. No real credentials or private conversations were used or published. Passing these regressions does not establish that every future Codex format is supported.
