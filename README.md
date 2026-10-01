# AIUsage · Windows

**Tu consumo de Codex, desde la bandeja de Windows.** Tokens medidos en los registros locales, costes API estimados por modelo y límites de la cuenta cuando están disponibles.

Aplicación nativa **C# / WPF / .NET 10**, sin Electron, sin telemetría y sin necesidad de instalar Python o Node. Implementación independiente y port funcional **parcial** de [OpenUsage v0.7.12](https://github.com/robinebers/openusage/releases/tag/v0.7.12), con atribución MIT. La primera versión se centra en Codex; no replica todos los proveedores de OpenUsage.

[Descargas y versiones](https://github.com/wandoth1/AIUsage/releases) · [Compilaciones y pruebas](https://github.com/wandoth1/AIUsage/actions/workflows/windows.yml) · [Privacidad](docs/PRIVACY.md) · [Origen del código](docs/UPSTREAM.md)

## Descargar y abrir

1. Abre **Releases** y descarga `AIUsage-0.1.0-win-x64.zip` para un PC Intel/AMD, o `AIUsage-0.1.0-win-arm64.zip` para Windows en ARM.
2. Extrae **todo** el ZIP en una carpeta de tu usuario y ejecuta `AIUsage.exe`. El runtime de .NET va incluido. No necesita permisos de administrador ni un instalador.
3. La ventana se abre junto al reloj. Su icono permanece en la bandeja; puede estar dentro de los iconos ocultos. `×` y `Esc` ocultan la ventana; **Salir** termina la aplicación.

**Versión preliminar, sin firma de código.** Windows puede mostrar una advertencia de editor desconocido. No desactives el antivirus ni protecciones globales. Comprueba que el archivo procede de este repositorio y verifica su SHA-256 contra `SHA256SUMS.txt`; también puedes compilarlo desde el código. El hash comprueba integridad, no sustituye una firma de editor.

```powershell
Get-FileHash .\AIUsage-0.1.0-win-x64.zip -Algorithm SHA256
```

Objetivo: Windows 10/11 de 64 bits, x64 o ARM64, en una versión compatible con .NET 10. No hay paquete de 32 bits. El pipeline ejecuta pruebas de lógica y una prueba de arranque/renderizado WPF en Windows x64; ARM64 se compila de forma cruzada y necesita validación en hardware ARM. Los paquetes no se presentan como probados con una cuenta real de Codex.

## Qué muestra

- **Hoy, ayer, últimos 7 y 30 días**: coste estimado, tokens y registros de uso; los días siguen la zona horaria de Windows.
- **Desglose por modelo** con barras, porcentaje del coste —o de tokens si falta alguna tarifa— y detalle de entrada, caché y salida al pasar el ratón.
- **Límites de sesión, semanales y Spark** si Codex los informa. Cada dato conserva su origen y hora; un reinicio vencido no se convierte artificialmente en un límite nuevo.
- **Tema oscuro/claro**, ventana fijable, actualización periódica, historial visual de 7 días y exportación CSV agregada.
- **Lectura incremental** de archivos abiertos por Codex, con caché local de metadatos. Evita volver a sumar contadores repetidos, copias y el historial heredado de subagentes.

No hay datos inventados cuando una carpeta está vacía: aparece **Sin datos**. Los modelos desconocidos conservan sus tokens y muestran **Sin tarifa**. Un coste incompleto lleva `≥` y un aviso. El programa no añade una entrada al inicio de Windows ni instala servicios.

## Conectar tus registros

Por defecto se usa `CODEX_HOME` si está definido; en caso contrario, `%USERPROFILE%\.codex`. En **Ajustes → Carpeta de Codex** puedes seleccionar otra carpeta. Se leen `sessions/**/*.jsonl` y `archived_sessions/**/*.jsonl`; también se admite seleccionar directamente una carpeta de rollouts.

Para Codex dentro de WSL, indica una ruta a `.codex` accesible desde Windows, por ejemplo `\\wsl.localhost\Ubuntu\home\TU_USUARIO\.codex`. La distribución debe estar disponible. La primera versión no descubre automáticamente distribuciones WSL ni combina varias carpetas. Por seguridad, omite enlaces/junctions anidados y avisa de una lectura incompleta.

Los datos representan los **registros accesibles del equipo/carpeta seleccionada**, no todo tu consumo de ChatGPT, el uso cloud ni otros ordenadores. Los rollouts de cuentas diferentes presentes en esa misma carpeta pueden mezclarse en el historial: v0.1 no hace atribución por cuenta. Los límites online pertenecen a la sesión actualmente autenticada.

### Límites online: opcionales y de solo lectura

El modo local funciona sin conexión y sin leer `auth.json`. Para obtener límites recientes, activa **Ajustes → Consultar los límites de mi cuenta online** y confirma el aviso.

Se lee el `access_token` existente en `auth.json` y se envía **solo a `https://chatgpt.com/backend-api/wham/usage`**, con el identificador de cuenta cuando existe. No se envían conversaciones. La consulta se limita a una por minuto, tiene timeout y no sigue redirecciones. Es el endpoint interno usado por OpenUsage, no una API pública estable.

AIUsage **no renueva ni sobrescribe credenciales**, no inicia conversaciones y no consume reinicios de límites. Si la sesión caduca, vuelve a autenticarte desde Codex. Una clave API no equivale a una sesión de ChatGPT. El almacén de credenciales de Windows no se lee en esta versión: si Codex solo guarda allí su sesión, la lectura local sigue funcionando, pero la consulta online no estará disponible. No copies credenciales de otra persona ni las adjuntes a una incidencia.

## Cómo interpretar los dólares

**No son cargos de tu suscripción, créditos gastados ni una factura de OpenAI.** Se calcula el equivalente teórico a tarifas API a partir de los tokens que registró Codex:

```text
(entrada no cacheada × precio entrada
 + entrada cacheada × precio caché
 + escritura de caché × precio correspondiente
 + salida × precio salida) / 1.000.000
```

La entrada cacheada ya forma parte de la entrada. Los tokens de razonamiento ya están incluidos en la salida: no se cobran dos veces. El nivel de servicio se toma de cada registro, no de tu configuración actual. Los modelos incluidos aplican sus reglas de contexto largo por encima de 272.000 tokens de entrada y el multiplicador de prioridad cuando está registrado.

Catálogo incluido, revisado el **1 de octubre de 2026**: `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`. Fuentes en [pricing.json](src/AIUsage.Core/pricing.json). No se supone que un modelo desconocido cueste lo mismo que otro.

El catálogo recalcula el historial accesible con esas tarifas; **no reconstruye precios históricos**, promociones, descuentos particulares, cargos de herramientas ni recargos regionales. Las asignaciones de `gpt-reserve` y `codex-auto-review` posteriores al 9 de julio de 2026 a Luna 5.6 son compatibilidad heredada de OpenUsage v0.7.12, no una afirmación de que todos los modelos de revisión futuros sean Luna.

### Tarifas personalizadas

Abre **Ajustes → Editar precios personalizados**. Guarda un objeto JSON en `%LOCALAPPDATA%\AIUsage\price-overrides.json` y pulsa **Actualizar**:

```json
{
  "mi-modelo": {
    "Input": 2.0,
    "Cached": 0.1,
    "Output": 10.0,
    "CacheWriteMultiplier": 1.25,
    "LongThreshold": 272000,
    "FastMultiplier": 2,
    "Source": "Fuente y fecha de la tarifa que has comprobado"
  }
}
```

Tarifas en USD por millón. `LongThreshold: 0` desactiva el recargo de contexto largo; un umbral positivo usa 2× en entrada/caché y 1,5× en salida. La interfaz indica cuando hay tarifas personalizadas. Este archivo no modifica el modelo que ejecuta Codex.

## Compilar y probar

Necesitas Windows y el SDK de .NET 10. El núcleo y sus pruebas también funcionan sin WPF en otros sistemas.

```powershell
dotnet run --project tests/AIUsage.Tests -c Release
dotnet build src/AIUsage.Windows -c Release -p:PublishSingleFile=false
.\scripts\build.ps1 -Runtime win-x64
.\artifacts\publish\win-x64\AIUsage.exe --demo
```

`--demo` usa únicamente datos sintéticos y no lee tu carpeta ni tus credenciales. `--tray` abre directamente en la bandeja. `--smoke-test CARPETA` ejecuta la vista demo, genera capturas de los dos temas y termina; está pensado para CI, no para consultar una cuenta.

Las capturas generadas en las compilaciones y releases se denominan `AIUsage-dark-demo.png` y `AIUsage-light-demo.png`: son capturas del renderizado de la aplicación real con **datos sintéticos**, no pruebas de una cuenta real.

## Alcance de v0.1

Codex es el único proveedor implementado. No se incluyen Claude, Cursor, Copilot, pi, OpenCode, multiperfil, autoactualizaciones, firma de código ni un instalador. La autenticación online puede dejar de funcionar si cambia el endpoint. La interpretación de rollouts se basa en el formato inspeccionado de OpenUsage v0.7.12; formatos futuros pueden requerir cambios.

Revisa [SECURITY.md](SECURITY.md) antes de publicar una incidencia. Nunca adjuntes `auth.json`, tokens ni conversaciones privadas.
