# AIUsage 0.1.0 · Windows preview

Primera versión de AIUsage para Windows, independiente y basada parcialmente en OpenUsage v0.7.12 (MIT). Codex es el proveedor implementado en esta entrega.

## Incluye

Interfaz nativa WPF con icono de bandeja, temas oscuro/claro y ventana fijable. Consumo de hoy, ayer, 7 y 30 días, desglose por modelo, caché, gráfico de actividad y CSV. Lectura incremental de rollouts de Codex, contadores sin duplicar y tratamiento del historial heredado por subagentes. Límites de sesión, semanales y Spark cuando aparecen en los registros; consulta online opcional, consentida y de solo lectura.

Los paquetes son portables y autocontenidos: extraer el ZIP y abrir `AIUsage.exe`. `win-x64` corresponde a Intel/AMD; `win-arm64`, a Windows ARM.

## Validación y límites

La publicación depende de que se superen las pruebas sintéticas del núcleo y la prueba de arranque/renderizado de la interfaz en Windows x64. Las capturas adjuntas se generan desde esa interfaz con datos de demostración, no con una cuenta real. ARM64 se compila de forma cruzada y no se ejecuta en el runner x64.

No incluye firma de código, instalador, autoactualización ni el resto de proveedores de OpenUsage. No se ha validado con las credenciales o los archivos reales del usuario. La consulta online usa un endpoint interno sujeto a cambios y requiere que Codex tenga `auth.json`; no lee el almacén de credenciales de Windows.

**Los dólares son equivalentes teóricos a tarifas API, no cargos ni una factura de tu suscripción.** Los modelos sin tarifa muestran el consumo en tokens y un aviso de coste parcial. Los agregados se limitan a los archivos accesibles de una carpeta, no a todos los equipos ni al uso cloud.

Sin telemetría, sin envío de conversaciones y sin modificación de los archivos de Codex. Se conservan los créditos y la licencia original de OpenUsage.
