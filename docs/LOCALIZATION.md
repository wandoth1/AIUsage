# English and Spanish localization

AIUsage embeds `Strings.resx` (English neutral resources) and `Strings.es.resx` (Spanish satellite resources) in `AIUsage.Core/Resources`. `L10n` resolves explicit `en`/`es` choices or `auto`; automatic selection uses Spanish for Spanish system UI cultures and English otherwise. Old settings without a Language property migrate to auto without changing other options.

Select a language in Settings, then choose **Save and refresh**. This saves the choice, rebuilds the dashboard and replaces the tray menu without restarting. The selector does not discard other unsaved settings immediately. Save is blocked during an active refresh, preventing a language change from racing an earlier dashboard calculation. Demo/smoke selection does not write real settings.

`T(key)` retrieves text and `F(key, args)` formats a complete template. The latter captures one immutable locale before resource lookup and formatting. Presentation uses en-US/es-ES explicitly; global parsing culture, JSON and invariant CSV numbers are not changed. Windows still determines the time zone and day boundaries.

Add keys to both resource files and keep numeric placeholder indexes and formats identical. Translate complete messages, not sentence fragments. Use `L10n.Culture` explicitly for presentation dates/numbers. Do not translate model IDs, account/window identities, paths, JSON fields, CSV column names or user notes. Quota names/sources are stored neutrally and translated only for display. Cache schema 4 includes the language-neutral metadata migration and the second-audit accounting correction.

Built-in pricing explanations are translated; custom notes remain verbatim. Language does not affect costs or qualification flags. CSV explanations follow the selected language while its schema/numbers remain invariant. System-owned Windows dialogs may retain Windows' own language.

## Verification

Run `dotnet run --project tests/AIUsage.LocalizationTests -c Release`. Tests inspect the actual embedded English/Spanish resources, placeholder parity, fallback rules, settings migration/persistence, formatted values, neutral quota serialization, pricing/CSV invariants, user notes, independent version and concurrent reads.

The published x64 executable runs `--smoke-test DIRECTORY`: it selects both languages through Settings radio buttons and Save, checks unchanged synthetic totals, verifies tray labels, checks refresh scroll/focus, and captures dashboards/settings in both themes. These are synthetic checks, not real-account or comprehensive accessibility validation.

Microsoft documentation: https://learn.microsoft.com/en-us/dotnet/core/extensions/retrieve-resources and https://learn.microsoft.com/en-us/dotnet/core/extensions/create-satellite-assemblies .
