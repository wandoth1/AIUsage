# Procedencia y adaptación

Referencia fijada: [robinebers/openusage, v0.7.12](https://github.com/robinebers/openusage/tree/v0.7.12), release publicada el 17 de septiembre de 2026. Licencia MIT, copyright 2026 Robin Ebers. El archivo `THIRD-PARTY-NOTICES.md` conserva íntegro el aviso original.

| Archivo de OpenUsage | Blob SHA inspeccionado | Uso en AIUsage |
|---|---|---|
| `Sources/OpenUsage/Providers/Codex/CodexLogFileParser.swift` | `323ee2afe1f91412800c74637fc1bacb19281a27` | Estado por archivo; modelos; contadores; nivel de servicio; barrera de reproducción de subagentes. |
| `Sources/OpenUsage/Providers/Codex/CodexLogUsageScanner.swift` | `e042be80988c8dff75c760a7ac2aed885f820a60` | Descubrimiento activo/archivado, deduplicación, campos antiguos, detección de forks y alias de revisión/reserva. |
| `Sources/OpenUsage/Providers/Codex/CodexUsagePricing.swift` | `05a2f9ec00a6e7bef40c79929a9c48e0bb139060` | Precios por petición, caché, prioridad sin doble multiplicación y umbral de contexto largo. |
| `Sources/OpenUsage/Resources/pricing_supplement.json` | `46c546f76f84cb42c88cae6e00ff8472610fe361` | Tarifas de GPT-5.6 y contraste de Astra. |
| `docs/providers/codex.md` | `9ef01fe67f1905b480d43b7ef255e0ca82144486` | Formatos de límites, identificación de ventanas por duración, endpoint y alcance de las métricas. |

Los importes de GPT-6.1 Sol, GPT-6 Sol, GPT-6 Luna y GPT-6 Astra se contrastaron con las respectivas páginas oficiales de OpenAI el 1 de octubre de 2026. Sus URL están asociadas a cada entrada de `pricing.json`.

## Decisiones deliberadas respecto a upstream

La interfaz se reescribe en WPF: no se intenta compilar SwiftUI en Windows. La caché incremental también se reimplementa en C# y guarda únicamente metadatos de consumo. No se copian iconos, PostHog, telemetría, actualizador ni conectores ajenos a Codex.

Un modelo ausente se etiqueta `unknown`, en lugar de suponer GPT-5. Si falta `total_tokens`, se usa entrada + salida; no se vuelve a añadir razonamiento, que es parte de la salida. Los modelos sin tarifa conocida no reciben una tarifa sustituta silenciosa.

Los estados repetidos de totales no cuentan otra vez. En archivos de subagentes se siembran los totales de referencia durante la reproducción de historia heredada, pero no se emite consumo hasta un `task_started` vivo. Si falta el inicio de sesión se usa el inicio de tarea frente a la fecha de su propia línea, como en upstream. Los registros demasiado grandes se omiten con aviso y una línea parcial se vuelve a leer en la siguiente actualización.

El cliente de límites es de solo lectura. No renueva tokens, no toca el almacén de credenciales de Windows, no modifica archivos de Codex y no canjea reinicios. Evita competir con la renovación de credenciales del cliente original. Una credencial caducada debe renovarse desde Codex.

La primera versión soporta una carpeta seleccionada y no interpreta la propiedad de cada sesión por cuenta. No agrega tráfico de pi/OpenCode, uso cloud ni otros dispositivos. Omite junctions anidados para evitar ciclos; el usuario puede seleccionar directamente la carpeta de destino.

Los alias `gpt-reserve` y `codex-auto-review` son reglas de compatibilidad fijadas a esta versión. No constituyen un mecanismo de detección de modelos nuevos ni un contrato de precios futuros.

## Pruebas

`tests/AIUsage.Tests` utiliza exclusivamente datos sintéticos: contadores, caché, razonamiento, replay de hijos, archivos truncados/parciales, duplicados, tamaños acotados, tarifas, ventanas semanales, CSV seguro y cambios de cultura/zona horaria. No llama a OpenAI. La prueba WPF del pipeline usa `--smoke-test`; ARM64 se compila pero no se ejecuta en el runner x64.
