# Seguridad

Esta versión es preliminar y no tiene firma de código. Las releases incluyen SHA-256 de los paquetes; verifica tanto la procedencia como la integridad. No desactives globalmente SmartScreen o el antivirus para usar la aplicación.

No publiques `auth.json`, claves API, tokens OAuth, cookies, archivos completos de sesiones ni capturas con datos personales. Para reportar un fallo indica versión de AIUsage, arquitectura y versión de Windows, uso nativo o WSL y un ejemplo mínimo **sintético** del formato que falla.

Los endpoints de límites y formatos de rollouts no son contratos estables. La app advierte cuando un dato está ausente, incompleto o no puede valorarse. No se deben usar sus costes estimados como control de facturación ni como medida exacta del cupo de la cuenta.

La primera versión es de solo lectura respecto a Codex. No reclama créditos, no refresca credenciales y no envía conversaciones. Revisa `docs/PRIVACY.md` y el código antes de habilitar el acceso online.

Para una vulnerabilidad que requiera material sensible utiliza el canal de reporte privado de GitHub si está habilitado. Si no lo está, abre una incidencia sin secretos para acordar un canal; no publiques una prueba de concepto con credenciales reales.
