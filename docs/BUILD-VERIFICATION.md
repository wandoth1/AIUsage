# Verificación de compilaciones y pruebas

## Evidencia previa a las correcciones

La entrega `v0.1.0-r1` corresponde a `db62c5a548452e2eb5fbb2d1ed7be95a0028d29c`. Su workflow `36873903102` pasó 48 pruebas sintéticas y la prueba de renderizado WPF x64. Esto no detectaba los fallos de exactitud de la auditoría posterior.

En el commit `6d8689091da0c275ee9dc98e2ba5027bffe24c8f` se añadieron únicamente pruebas independientes y su paso de CI, sin modificar producción. El [run 36879730529](https://github.com/wandoth1/AIUsage/actions/runs/36879730529) conservó 48/48 pruebas originales correctas y falló 13/13 casos nuevos. Su publicación quedó bloqueada.

Una ejecución intermedia de las correcciones, [36882826391](https://github.com/wandoth1/AIUsage/actions/runs/36882826391), pasó 48 pruebas originales y 44 de 45 nuevas. La prueba adicional de escrituras simultáneas descubrió una colisión de renombrados en Windows; la corrección posterior serializa los escritores del proceso sin relajar el assert.

## Contrato de validación de v0.1.1

El workflow debe completar, por este orden:

1. `dotnet run --project tests/AIUsage.Tests -c Release`: 48 casos.
2. `dotnet run --project tests/AIUsage.AuditTests -c Release`: 45 casos, con HTTP simulado y fixtures independientes.
3. Comprobar que `dotnet test` sobre ambos ejecutores devuelve el error explicativo `AIU0001` y no un éxito sin pruebas.
4. Compilar WPF y publicar paquetes autocontenidos x64 y ARM64.
5. Ejecutar **el binario x64 publicado** con `--smoke-test`: renderizar dos temas y comprobar conservación de desplazamiento/foco.
6. Verificar licencias incluidas, concordancia de versiones, empaquetar y generar SHA-256.

Solo una ejecución correcta en `main`, solicitada para publicar, puede crear la release. Los resultados concretos del artefacto descargado están en `regression-tests.txt`, `audit-tests.txt`, `test-runner-guard.txt` y `BUILD-INFO.json`. Este último identifica commit, run, SDK y alcance de validación; no es una firma ni una attestación.

Dos fixtures originales cambiaron con justificación: la prueba de una copia ahora incluye el mismo identificador de sesión; un registro sobredimensionado de tipo desconocido espera exclusión conservadora con aviso en vez de contabilizar uso posterior potencialmente heredado. No se cambiaron expectativas de precio para ocultar discrepancias con las fuentes oficiales.

## Límites

No se ejecutan credenciales, rollouts ni conversaciones reales. Las pruebas HTTP no realizan conexiones externas. Las capturas son renderizados reales con datos demo. ARM64 solo se compila; no se afirma ejecución en hardware ARM. Tampoco se afirma validación integral de DPI por monitor, varios monitores, RDP concurrente, reinicio de Explorer, lector de pantalla o importación de CSV en Excel.

El aviso WFO0003 de la integración WinForms/WPF puede seguir apareciendo. No equivale a un fallo de la prueba de renderizado ni demuestra compatibilidad en todos los escenarios DPI. La verificación del núcleo no reemplaza pruebas de uso real autorizadas.
