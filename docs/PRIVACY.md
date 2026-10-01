# Privacidad y datos

## Modo local (por defecto)

AIUsage lee los archivos `.jsonl` de la carpeta de Codex seleccionada. Solo interpreta metadatos de contexto/consumo y límites; descarta mensajes de conversación y no los guarda en su caché. No lee `auth.json`, no llama a OpenAI, no usa analítica, no registra contenido en un servidor y no crea una cuenta de AIUsage.

Los archivos de configuración y caché se guardan en `%LOCALAPPDATA%\AIUsage`. La caché contiene fechas, modelos, tokens, nivel de servicio, contadores, estado del analizador y límites informados. Es información de actividad del usuario y sigue siendo privada: no debe publicarse sin revisión. Los nombres de los archivos de caché se obtienen mediante SHA-256 de la ruta; no contienen la ruta original. La app utiliza la zona horaria de Windows.

El CSV se genera únicamente al solicitarlo y elegir un destino. Contiene agregados por día/modelo; no incluye conversaciones, credenciales ni rutas. Las celdas de texto potencialmente interpretables como fórmulas se escapan.

## Consulta online opcional

Solo después de activar la casilla y confirmar, AIUsage lee el token OAuth ya existente en `auth.json`. Ese token y el identificador de cuenta, si existe, se envían al host fijo `chatgpt.com` en una petición GET al endpoint interno `/backend-api/wham/usage`. No se siguen redirecciones. No se envían archivos ni conversaciones.

No se renuevan credenciales ni se persisten copias en los ajustes/caché. El token permanece temporalmente en memoria administrada durante la consulta; no se afirma un borrado criptográfico de esa memoria. No se leen cookies del navegador ni se extraen credenciales del almacén de Windows. No se usan claves API como sustituto de OAuth.

Los errores mostrados por el cliente no incluyen el cuerpo HTTP, el token ni el contenido de `auth.json`. La app no realiza peticiones al modelo, no consume reinicios y no ofrece cambiar la suscripción.

Los botones de GitHub y datos locales solo abren el navegador o el explorador cuando se pulsan. El modo demo y la prueba de interfaz no leen los ajustes, rollouts ni credenciales reales.

## Eliminar datos y desinstalar

Pulsa **Salir**, elimina la carpeta donde extrajiste la aplicación y, si deseas borrar sus ajustes/historial cacheado, elimina `%LOCALAPPDATA%\AIUsage`. Los originales de Codex no se eliminan ni modifican. La aplicación no se registra para iniciar con Windows, no instala servicios y no requiere privilegios administrativos.
