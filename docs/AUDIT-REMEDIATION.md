# Respuesta a la auditoría de AIUsage · 0.1.1

Fecha de revisión: 1 de octubre de 2026. Base: `v0.1.0-r1`, commit `db62c5a548452e2eb5fbb2d1ed7be95a0028d29c`. Cambios en [PR #1](https://github.com/wandoth1/AIUsage/pull/1).

Se contrastó el informe recibido con el código, el formato de Codex y las fuentes oficiales de precios. No se publican el archivo original de la auditoría, sus rutas privadas ni credenciales. Este documento registra decisiones propias y evidencia reproducible; no atribuye a la auditoría comprobaciones nuevas.

## Evidencia y método

Primero se añadieron 13 reproducciones sintéticas sobre producción intacta. En el [run 36879730529](https://github.com/wandoth1/AIUsage/actions/runs/36879730529) fallaban las 13, pese a pasar las 48 pruebas originales. Se ampliaron a 45 pruebas de auditoría, manteniendo un ejecutor independiente y resultados esperados calculados sin reutilizar la implementación del cálculo.

Las correcciones del núcleo pasaron los 13 casos iniciales. Una prueba adicional detectó una colisión de escrituras concurrentes en Windows incluso con temporales distintos: se añadió serialización dentro del proceso. Las pruebas completas, WPF y empaquetado deben pasar antes de publicar; los resultados de cada artefacto acompañan a la release. No se presentan las pruebas sintéticas como contraste con una cuenta real.

## Disposición de los hallazgos

| Hallazgo | Decisión en 0.1.1 | Comprobación o limitación |
|---|---|---|
| H-01 · Cuotas Codex/Spark | Corregido: `limit_id`/`limit_name`, identidad por proveedor y duración, conservación de proveedores separados. | Pruebas de 20 % Codex frente a 90 % Spark, etiquetas iguales con IDs distintos y ventana semanal que cambia de posición. |
| H-02 · Priority persistente | Corregido: un snapshot completo sin tier vuelve a estándar; una actualización parcial no borra el tier por omisión. Se ignoran snapshots copiados de otro `thread_id`. | Esquema oficial y fixtures; no se ha conmutado `/fast` en una cuenta real. |
| H-03 · Precio Sol 5.6 | Actualizado a 4 / 0,4 / 20 USD por millón, observado el 2026-10-01. Promoción anunciada al menos hasta 2026-11-21. | Caso 200K entrada + 100K salida: 2,80 USD, no 4,00. Nota de revisión desde 22-nov; sin presumir caducidad exacta ni reconstruir tarifas históricas. |
| H-04 · Tokens de ocupación | Corregido: un evento solo con `total_tokens`, sin uso de entrada/salida/razonamiento, no suma consumo. | Reproducción del contador de ventana de contexto llena. |
| H-05 · Metadatos sobredimensionados | Mitigación conservadora: un registro desconocido o de contabilidad/metadatos superior al límite bloquea la contabilización del archivo, con aviso persistente. | No se convierte un hijo ambiguo en una sesión raíz. No se afirma recuperación completa de esos archivos. |
| H-06 · `started_at` ausente | Se mantiene la barrera contra replay y se avisa de ambigüedad; solo una hora de inicio fiable la abre. | No se usa el timestamp de la línea como sustituto: el replay puede reescribirlo. Parte del consumo puede quedar excluida hasta disponer de información suficiente. |
| H-07 · Deduplicación global | Corregido: identidad de sesión/cuenta y contadores acumulados; secuencia para registros sin acumulado. | Dos sesiones independientes con iguales cantidades y hora cuentan ambas. Sin ID de sesión, archivos diferentes no se suponen copias. |
| H-08 · Precios personalizados | Corregido: claves normalizadas, Input/Cached/Output obligatorios, rechazo de campos duplicados/desconocidos/invalidos y tamaño acotado. | Pruebas de mayúsculas, campos mal escritos, omisiones y duplicados. |
| H-09 · Alias y reglas no verificadas | Visibles como estimaciones condicionadas en desglose y CSV. Un precio explícito personalizado tiene prioridad sobre el alias heredado. | No se certifica que todos los futuros reserve/auto-review sean Luna. Multiplicadores no confirmados conservan nota. |
| H-10 · Catálogo incompleto | Ampliación opcional no implementada. | Los tokens siguen visibles y el coste se marca incompleto; no se inventan tarifas. |
| H-11 · Versión de caché | Corregido: esquema de parser 2, rechaza versión ausente/antigua y vuelve a leer los originales. | Pruebas de reconstrucción en nuevo proceso. |
| H-12 · Avisos por conversación grande | Corregido para tipos irrelevantes identificables con seguridad en el prefijo acotado. | `response_item` grande no implica contabilidad incompleta. Tipos ambiguos siguen siendo conservadores. |
| H-13 · EOF sin salto final | Corregido: JSON completo estable se muestra en la segunda lectura, sin confirmar el desplazamiento del archivo. | El append retrae/reprocesa la vista provisional; pruebas entre procesos y al añadir el salto. |
| H-14 · Modificaciones que conservan metadatos | Mitigación manual: botón Reconstruir caché de lectura, documentado. | No se hace hash completo de todos los logs en cada refresco. Una modificación histórica que conserve tamaño/fecha/prefijo puede requerir reconstrucción. |
| H-15 · Cabeceras inválidas | Corregido: validación previa, errores sanitizados y lectura acotada de autenticación. | CRLF, NUL y caracteres inválidos no llegan al cliente HTTP; sin tokens en mensajes. |
| H-16 · Identidad de cuotas | Respuestas invalidadas si cambian credenciales durante la consulta; no mezclar ventanas online con locales. Cuotas locales conocidas separadas por seudónimo. | El historial de tokens sigue agregado por carpeta. Cuotas sin identidad no se pueden separar con certeza. La hora del snapshot sigue visible. |
| H-17 · Reintentos | Corregido: mínimo un minuto, serialización, espera progresiva y Retry-After. | Pruebas offline con reloj controlado, 429/5xx, credenciales cambiadas y llamadas simultáneas. |
| H-18 · Refresco WPF | Mitigado: agregaciones calculadas fuera del hilo de interfaz y reutilizadas por periodo; conservación de scroll/foco. | Sigue existiendo reconstrucción visual; no es una migración a MVVM ni una garantía de rendimiento. Smoke sintético para scroll/foco. |
| H-19 · Varios monitores/DPI | Pendiente de validación manual y mejora de posición por monitor. | No se afirma corregido ni probado en varios monitores/ARM64. |
| H-20 · Cultura y CSV | Importes UI según Windows; CSV mantiene coma delimitadora y punto decimal documentados. | Prueba es-ES y CSV invariante; no se ha abierto Excel ni se garantiza doble clic correcto con cualquier cultura. |
| H-21 · `dotnet test` vacío | Corregido: guardia AIU0001 explica que hay que usar `dotnet run`. | El workflow comprueba el error previsto sobre ambos ejecutores. |
| H-22 · Cadena de compilación | Acciones fijadas por SHA, checkout sin credenciales persistentes; no cancelar publicaciones en main por un nuevo push. | No se añaden permisos de administración ni servicios externos. |
| H-23 · Nombres y procedencia | Nombres de ZIP derivados de RELEASE_TAG, comprobación de versión y BUILD-INFO con commit/run/SDK, junto con SHA-256. | Sin firma de editor ni attestación criptográfica; no se presentan como equivalentes. |

Además se reconoce el campo histórico `resets_in_seconds`, se conserva el error de carga de ajustes en pantalla, el modo demo usa una instancia distinta de la aplicación real y las escrituras JSON del proceso se serializan usando archivos temporales únicos. Dos sesiones RDP distintas no obtienen por ello una transacción distribuida: una contención externa todavía puede resultar en un error recuperable de caché.

## Fuentes verificadas

- Formato de Codex: `openai/codex@ecc78e4cf5607ecf5f080d682eae0ecb650868ae`, `codex-rs/protocol/src/protocol.rs`, tipos `ThreadSettingsSnapshot`, `ThreadSettingsAppliedEvent` y `TurnStartedEvent`. `started_at` es opcional; los snapshots copiados pueden conservar el `thread_id` original.
- Referencia adaptada: OpenUsage `v0.7.12` (`3b84fec518d5b3775adb93456fa8af7330c852d5`), no asumida infalible.
- Tarifas: https://developers.openai.com/api/docs/pricing y páginas oficiales de cada modelo enlazadas en `src/AIUsage.Core/pricing.json`, consultadas el 2026-10-01. En particular https://developers.openai.com/api/docs/models/gpt-5.6-sol para la promoción. Donde no se confirmó una regla específica, se conserva un aviso en lugar de declarar verificación.
- Extensión de MSBuild para impedir éxito vacío: https://learn.microsoft.com/en-us/visualstudio/msbuild/target-build-order . Las pruebas usan `dotnet run`; no se presupone descubrimiento automático por VSTest.

## No cubierto por esta entrega

Uso con credenciales reales; reconstrucción completa de subagentes ambiguos; atribución del historial de tokens por cuenta; todas las condiciones multimonitor/DPI, Explorer/RDP/accesibilidad; ejecución ARM64 real; firma, autoactualización y proveedores adicionales. Se preservan los tags anteriores para poder comparar y no se publica el informe privado recibido.
