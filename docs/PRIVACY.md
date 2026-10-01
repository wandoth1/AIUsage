# Privacy and data

## Local mode (default)

AIUsage reads JSONL files in the selected Codex folder. It interprets context/usage metadata and limits, discards conversation messages, and does not store them in its cache. It does not read `auth.json`, contact OpenAI, use analytics, store content on a server or create an AIUsage account.

Settings and cache live in `%LOCALAPPDATA%\AIUsage`. Settings include the chosen language. Cached activity contains timestamps, models, tokens, service tiers, counters, parser state, session identifiers and reported limits. Known local account identifiers are hashed with SHA-256 to separate quotas; raw account IDs are not stored. The hash is a correlatable pseudonym, **not guaranteed anonymity**. Cached activity remains private and should not be published without review. Cache filenames hash the original path instead of including it. Daily grouping uses the Windows time zone.

CSV is created only after an explicit export and destination choice. It contains day/model aggregates and pricing qualifications, not conversations, credentials or original paths. Potential spreadsheet formulas in text cells are escaped. Column names/numbers remain invariant; explanatory notes follow the selected language. Custom notes are preserved verbatim.

Large conversation records are classified from a bounded JSON header and skipped; image contents and pasted text are not cached. Accounting/session metadata that cannot be safely interpreted may exclude the file's accounting with a warning. This exclusion never edits or deletes the original rollout.

## Optional online queries

Only after enabling the checkbox and confirming consent does AIUsage read the existing OAuth token from `auth.json`. The token and account identifier, if available, go to the fixed `chatgpt.com` host in a GET request to `/backend-api/wham/usage`. Redirects are disabled. Credential format is validated before headers are assigned, and credentials are reread after the response; a detected change invalidates it. Frequency is limited and throttling/transient failures are backed off. Files and conversations are not uploaded.

Credentials are not refreshed or copied into settings/cache. The token temporarily resides in managed memory during a query; no cryptographic memory-erasure guarantee is made. Browser cookies and Windows Credential Manager are not read. API keys do not replace OAuth.

Error messages from the quota client do not include response bodies, tokens or `auth.json` content. The app does not call models, spend resets or change subscriptions. Changing language does not broaden online consent or enable it automatically.

GitHub/local-data buttons open a browser/file manager only when clicked. Demo and UI smoke modes do not read real settings, rollouts or credentials. HTTP tests use fake credentials in their own temporary directories and do not query real accounts. Windows-owned dialogs may use the system language rather than the app's selection.

## Remove data or uninstall

Choose **Exit**, delete the extracted app folder and, to remove its settings/cached history, delete `%LOCALAPPDATA%\AIUsage`. Original Codex files are not removed or modified. The app does not register for Windows startup, install services or require administrator privileges.

Manual cache rebuilding removes only JSON files in AIUsage's cache directory. Schema migrations rebuild metadata from original logs; they do not alter conversations or credentials.
