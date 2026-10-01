# Build and test verification

## Historical evidence

AIUsage v0.1.0-r1 (`db62c5a548452e2eb5fbb2d1ed7be95a0028d29c`) passed 48 tests and x64 WPF rendering in run 36873903102 but did not reveal every later audit defect.

[Run 36879730529](https://github.com/wandoth1/AIUsage/actions/runs/36879730529) added thirteen independent reproductions without production changes: the original 48 passed and all thirteen new cases failed. Publication was blocked. An intermediate run exposed Windows concurrent rename contention; writers were serialized without weakening the assertion. The published [0.1.1 run](https://github.com/wandoth1/AIUsage/actions/runs/36885332740) passed 48 original plus 45 audit tests and its WPF smoke checks.

The second audit subsequently found G-1. [Run 36891781462](https://github.com/wandoth1/AIUsage/actions/runs/36891781462) independently reproduced three failures around large non-accounting messages, while positive accounting/metadata quarantine controls passed. See [AUDIT-FOLLOWUP.md](AUDIT-FOLLOWUP.md). This is why a passing suite is not presented as proof of complete correctness.

## AIUsage 1.0 validation contract

The Windows workflow must pass:

1. Original regression runner: `dotnet run --project tests/AIUsage.Tests -c Release`.
2. Independent audit runner: `dotnet run --project tests/AIUsage.AuditTests -c Release`.
3. English/Spanish runner: `dotnet run --project tests/AIUsage.LocalizationTests -c Release`.
4. Second-audit runner: `dotnet run --project tests/AIUsage.FollowupTests -c Release`.
5. Expected AIU0001 failures for dotnet test on all four executable runners, avoiding false zero-test success.
6. WPF build and self-contained x64/ARM64 publication with complete runtime notices.
7. The published x64 executable's smoke checks: select both languages using Settings, preserve synthetic totals, verify tray labels and refresh scroll/focus, and render dashboard/settings in both themes.
8. Package/assembly/manifest version checks, all eight screenshots, source ZIP, binaries, build metadata and SHA-256 hashes.

Only an explicitly requested successful main run creates a normal release. Validation has read-only repository permission; publication has contents-write permission. Actions are SHA-pinned and checkout does not persist credentials. Existing releases are not silently overwritten.

Artifacts contain `regression-tests.txt`, `audit-tests.txt`, `localization-tests.txt`, `followup-tests.txt`, `test-runner-guard.txt` and `BUILD-INFO.json`. Metadata identifies the source commit, workflow run, SDK and validation scope. The source ZIP contains tracked files at that commit. Hashes and metadata are not signatures or cryptographic attestations.

Localization intentionally updates canonical quota-label assertions to English and pricing-note assertions to English translations. Numerical expectations remain unchanged; separate tests reconcile both languages. Schema 4 migrates neutral quota labels and false quarantines from the prior parser.

Two first-audit fixtures changed with justification: copied logs now carry the same session ID; oversized unknown accounting expects quarantine rather than trusting potentially inherited usage. The second-audit fix retains that protection while exempting recognized non-accounting events. Tests are not removed or weakened to hide accounting failures.

## Limits

No real credentials, conversations or account requests in tests. HTTP uses mocks. Screenshots show the actual WPF app with synthetic data. ARM64 is only cross-compiled. Mixed-DPI/multiple-monitor, concurrent RDP, Explorer restart, screen-reader use and Excel CSV import are not comprehensively validated.

The WFO0003 warning from WPF/WinForms integration may remain. Passing rendering tests does not establish every DPI scenario. Core tests do not replace authorized real-world validation.
