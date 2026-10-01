# Local-only architecture and compliance boundaries

AIUsage 1.1.0 replaces the optional authenticated integration of 1.0.0 and earlier. This is a technical risk reduction, not a legal certification, OpenAI approval or guarantee about account enforcement.

## Removed, rather than disabled

- `CodexUsageClient`, its endpoint, HTTP client, credential parser, retry logic and authentication headers.
- The OnlineQuota runtime property, consent dialog, account refresh and live/local quota merging.
- Browser links, shell file pickers, file-manager/editor launchers and all application subprocess creation.

There is no fallback via the Codex CLI, app server, OAuth, API keys, browser automation or any other provider. Older settings cannot turn the feature back on. AIUsage's previous releases remain identifiable historical versions, not silently replaced binaries.

## Local features retained

Read-only parsing, the two accounting-audit fixes, deduplication, local log quota snapshots, totals, pricing, diagrams, English/Spanish, themes, tray refresh and local CSV remain. The app never repairs or rewrites a Codex log. Missing/ambiguous data remains visibly incomplete.

The former HTTP-related tests are retired **with the deleted capability** rather than counted as passing tests for a new product. Eleven online-client/online-merge tests are removed from the 45-case audit suite; the 34 local audit cases remain. The 48 accounting tests and 17 follow-up regressions remain. The 22 localization cases now validate local-only help and migration instead of the old consent strings. New offline tests cover removal, migration, locked credentials, source immutability, filesystem boundaries, export, inline prices and application metadata. Exact execution counts are in release artifacts.

## Local filesystem policy

`LocalPaths` rejects remote/device/relative paths before content access, checks Windows drive type, and checks ancestors for reparse/offline/recall flags. `LogScanner` guards the home, individual rollouts and cache paths. `AtomicJson`, price loading and export guard their own destinations. WSL UNC access is intentionally removed despite some WSL distributions running on the same machine. Make a separate local rollout copy yourself where necessary; the app does not transfer credentials.

This is best-effort application enforcement against unintended network-backed files, not protection against a compromised OS, malicious same-user path races or every storage driver. Do not use synchronized/virtualized storage for a strict offline workflow. The executable does not install firewall rules, services or certificates, or change security controls.

## Policies, licenses and remaining legal uncertainty

Reviewed on 2026-10-01:

- OpenAI's [Europe Terms of Use](https://openai.com/policies/eu-terms-of-use/) include restrictions on automated extraction and circumventing restrictions/protective measures. This application does not connect to those services, scrape a website or bypass service limits. The terms do not explicitly approve AIUsage; absence of a service connection is not an official compliance opinion.
- OpenAI's [Codex authentication guidance](https://developers.openai.com/codex/auth/) says to treat auth.json like a password. The application no longer accesses it at all.
- The AEPD's [privacy by design/default guidance](https://www.aepd.es/preguntas-frecuentes/2-tus-obligaciones-como-responsable-del-tratamiento/9-analisis-de-riesgos/FAQ-0224-que-es-la-proteccion-de-datos-desde-el-diseno-y-por-defecto) informs data minimization and a local-only default. It is not a certification and does not eliminate obligations for an employer or user processing someone else's records.
- OpenUsage's MIT copyright/license notice remains in LICENSE and THIRD-PARTY-NOTICES.md. Independently versioned does not mean unattributed. Full runtime/WPF/Windows Forms notices still accompany each package. Product names identify compatibility; no endorsement is claimed.

Technical review cannot certify compliance with every law, contractual interpretation or future policy. Process only logs you are authorized to use. Obtain jurisdiction-specific professional advice before commercialization or organizational deployments where a legal assurance is required. Do not advertise this release as "ban-proof", "OpenAI-approved" or "legally certified".

## Evidence and boundaries

`scripts/verify-local-runtime.ps1` creates only synthetic data, sets legacy OnlineQuota=true, locks fake authentication/configuration against access, and launches the published x64 executable. Its normal-mode fixture must retain 1,100 tokens without errors and migrate settings. Source hashes must remain unchanged. Bilingual demo tests then check both themes, Settings and tray labels. No real account or conversation is used.

`AIUsage.OfflineTests` inspects application-owned Core/x64/ARM64 assemblies for network/credential/process/dynamic-load APIs and unexpected native imports, rejects runtime credential/endpoint strings, validates local operations and records whether .NET network start events occurred in its process. These guardrails are not a full proof against all possible egress mechanisms or a packet capture. No claim is made about independent operating-system or third-party network activity. ARM64 is not executed by the x64 runner.

Build, restore and GitHub publication download dependencies/licenses and upload artifacts. They are development/distribution activities, separate from the local-only installed app.
