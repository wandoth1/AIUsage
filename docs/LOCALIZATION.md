# English and Spanish localization

Application-owned labels, settings, tray items, warnings and pricing explanations use L10n and embedded RESX resources. Strings.resx is English; Strings.es.resx is Spanish. SafetyStrings.resx and SafetyStrings.es.resx add audited recovery/instance warnings and override specific historical labels. The regression suite checks the merged resources, nonempty values and matching format placeholders.

Settings.Language accepts auto, en or es. Auto uses Spanish for Spanish-language Windows and English otherwise. Unsupported or null values normalize to auto. Changes apply after Save and refresh, without restarting. Presentation uses en-US/es-ES; it does not change global parsing culture, recorded identities, token arithmetic, USD currency or Windows-zone day boundaries.

Provider/model identifiers and user-supplied text are not translated. CSV columns and numerical fields are invariant; explanatory notes use the chosen language and preserve custom notes. Windows-owned controls/notifications can follow the OS language. Repository documentation and release notes are maintained in English.

The 1.1.1 safety messages explain legacy-instance conflicts, migration-write failures, paused scanning, required explicit folder confirmation and the active source. A failed migration write does not discard a saved language or folder. Corrupt settings cannot be trusted, so first-use/default display language is used while scanning remains paused.

Testing covers resource parity, culture fallback/overrides, persistence, invariance of accounting and CSV, and language switching. Published x64 demo/smoke tests exercise Settings selections, both themes, tray text and scroll/focus. Actual-entry-point tests separately exercise recovery and timers. Generated screenshots contain synthetic data; they are not real account evidence.
