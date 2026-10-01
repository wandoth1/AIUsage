# AIUsage 0.1.1 · Correcciones de auditoría

Actualización de la aplicación, no solo del empaquetado. Port funcional parcial de OpenUsage v0.7.12 para Windows; Codex sigue siendo el único proveedor.

## Actualizar

Cierra la versión anterior desde **Salir**, extrae todo el ZIP nuevo en otra carpeta y abre `AIUsage.exe`. `win-x64` es para Intel/AMD; `win-arm64` es para Windows ARM. El runtime está incluido y no se necesitan privilegios de administrador. Los ajustes se conservan y la caché antigua se reconstruye automáticamente. No borres `.codex`.

## Correcciones principales

- Cuotas de Codex y Spark separadas por identificador y duración; sin mezclar ventanas online y locales de distintas cuentas.
- Desactivar priority deja de mantener el multiplicador anterior; los contadores de ocupación de contexto ya no son consumo.
- Deduplicación con identidad de sesión y contadores, sin eliminar uso legítimo de sesiones independientes.
- Tarifas personalizadas estrictas, notas de procedencia y promoción de GPT-5.6 Sol observada el 1 de octubre de 2026.
- Última línea estable sin salto final, esquema de caché versionado y reconstrucción manual.
- Validación de autenticación antes de formar cabeceras; detección de cambios de sesión durante una consulta; espera progresiva y Retry-After.
- Agregaciones fuera del hilo WPF, conservación de desplazamiento y foco al refrescar, errores de ajustes persistentes y modo demo independiente.
- Escrituras locales serializadas dentro del proceso, temporales únicos y acciones CI fijadas por SHA.

## Evidencias de la publicación

El pipeline ejecuta los 48 casos del ejecutor original y 45 casos de auditoría. Los 13 casos iniciales de auditoría se reprodujeron primero sobre código de producción sin modificar: fallaban los 13, aunque pasaban las 48 pruebas anteriores.

La publicación requiere que ambas suites pasen, que la protección contra `dotnet test` sin pruebas funcione y que el ejecutable x64 supere la prueba WPF con ambos temas y conservación de desplazamiento/foco. Los informes de cada ejecución y `BUILD-INFO.json` se adjuntan a la release. Las capturas usan datos sintéticos, no una cuenta real.

## Límites que se mantienen

Un archivo con metadatos de contabilidad demasiado grandes o un subagente sin información suficiente puede quedar excluido con aviso: se evita duplicar consumo, no se afirma haber recuperado datos ambiguos. Las reglas de precio no confirmadas se señalan. No se reconstruyen precios históricos ni se separa el historial de tokens de varias cuentas mezcladas en una carpeta.

ARM64 está compilado de forma cruzada, no ejecutado en hardware ARM. La autenticación se prueba con respuestas HTTP simuladas, no con credenciales reales. Siguen pendientes escenarios de varios monitores/DPI, lector de pantalla y reinicio de Explorer. No hay firma de código, instalador ni autoactualización. SHA-256 y BUILD-INFO verifican integridad/describen procedencia; no son una firma de editor ni una attestación criptográfica.

Detalle: `docs/AUDIT-REMEDIATION.md` y `docs/BUILD-VERIFICATION.md`.
