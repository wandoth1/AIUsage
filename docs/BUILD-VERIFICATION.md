# Verificación de la entrega inicial

El workflow `36872557145`, commit `91ccb40cc2d14f108728ac19b5126a70b5bafcda`, completó el 1 de octubre de 2026:

- 48 pruebas sintéticas, 0 fallos.
- Compilación WPF en Windows Server 2025 x64, SDK .NET 10.0.401.
- Publicación autocontenida para win-x64 y win-arm64.
- Ejecución de la prueba WPF y renderizado de los temas oscuro y claro.

Se descargaron los artefactos y se comprobaron los hashes SHA-256 de ambos ZIP, capturas e informe. Las cabeceras PE de los ejecutables identificaban AMD64 (`0x8664`) y ARM64 (`0xaa64`) respectivamente. Se inspeccionaron visualmente ambas capturas: son datos demo, no consumo real del usuario.

En la inspección final del contenido de los ZIP se detectó que el selector de licencias no recogía todos los nombres de avisos de terceros del runtime. La revisión `v0.1.0-r1` corrige el empaquetado y hace obligatoria la incorporación de las seis licencias/avisos de .NET, WPF y Windows Forms desde sus tags oficiales. Se conserva la versión de aplicación 0.1.0 y se repiten las pruebas durante la nueva publicación.

La compilación inicial contiene el aviso WFO0003 de WinForms por la declaración DPI del manifiesto. La aplicación principal es WPF; WinForms se utiliza para la bandeja y el selector de carpetas. El aviso no impidió compilar o ejecutar la prueba de renderizado. No se afirma validación de todos los escenarios de DPI/monitores, hardware ARM o sesiones reales.

No se probaron peticiones de cuota con credenciales reales. No se ejecutó el binario ARM64 en hardware ARM. Estas comprobaciones no equivalen a una auditoría externa de seguridad ni garantizan compatibilidad con futuros formatos de Codex.
