# AIUsage 0.1.0 · Windows preview · packaging revision r1

Primera versión de AIUsage para Windows, independiente y basada parcialmente en OpenUsage v0.7.12 (MIT). Codex es el proveedor implementado en esta entrega.

**r1 es una revisión del empaquetado:** añade los avisos completos de terceros de .NET, WPF y Windows Forms, con sus fuentes y SHA-256. La aplicación sigue siendo la versión 0.1.0. No se ha cambiado su lógica.

## Descarga

Extrae el ZIP y abre `AIUsage.exe`. No requiere instalar .NET ni permisos de administrador.

- `AIUsage-0.1.0-win-x64.zip`: Windows en Intel/AMD.
- `AIUsage-0.1.0-win-arm64.zip`: Windows en ARM.

## Incluye

Interfaz nativa WPF con icono de bandeja, temas oscuro/claro y ventana fijable. Consumo de hoy, ayer, 7 y 30 días, desglose por modelo, caché, gráfico de actividad y CSV. Lectura incremental de rollouts de Codex, contadores sin duplicar y tratamiento del historial heredado por subagentes. Límites de sesión, semanales y Spark cuando aparecen en los registros; consulta online opcional, consentida y de solo lectura.

## Validación y límites

La publicación depende de las pruebas sintéticas del núcleo y del arranque/renderizado WPF en Windows x64. La primera compilación superó 48 pruebas; el informe de esta compilación se adjunta como `regression-tests.txt`. Las capturas se generan desde la aplicación real con datos sintéticos, no con una cuenta real. ARM64 se compila de forma cruzada y no se ejecuta en el runner x64.

Es una versión preliminar **sin firma de código**. No incluye instalador, autoactualización ni el resto de proveedores de OpenUsage. No se ha validado con credenciales o archivos reales del usuario. La consulta online usa un endpoint interno sujeto a cambios y requiere `auth.json`; no lee el almacén de credenciales de Windows.

**Los dólares son equivalentes teóricos a tarifas API, no cargos ni una factura de tu suscripción.** Los modelos sin tarifa muestran tokens y un aviso de coste parcial. Los agregados se limitan a los archivos accesibles de una carpeta, no a todos los equipos ni al uso cloud.

Sin telemetría, sin envío de conversaciones y sin modificación de los archivos de Codex. Se conservan los créditos y la licencia original de OpenUsage.
