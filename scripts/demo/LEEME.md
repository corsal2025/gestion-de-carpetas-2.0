# Demo local con datos ficticios

Requisitos: Windows, PowerShell 7, SDK .NET 10 y copia de la rama `feat/gc2-historia-7-pasos`. No requiere Excel, base ni credenciales municipales. Los comandos se ejecutan desde la raíz del repositorio. La carpeta elegida debe ser **nueva**; el generador se niega a reemplazar datos existentes.

```powershell
dotnet run --project scripts/demo/Demo.csproj -- temp/demo-local
dotnet publish src/Sgl.Web/Sgl.Web.csproj -c Release -o temp/demo-local/publicacion
pwsh -File scripts/demo/Abrir-Demo.ps1
```

Abra `http://127.0.0.1:56552`. Mantenga la terminal abierta y detenga con Ctrl+C. Si el puerto está ocupado elija otro: `pwsh -File scripts/demo/Abrir-Demo.ps1 -Puerto 56553`. Para otra carpeta nueva pase la misma ubicación al generador, publicación y parámetro `-Carpeta`.

El arranque es manual y explícito, en primer plano. Esta guía es para una estación de revisión autorizada; no debe usarse para eludir un rechazo de las herramientas del agente. El agente no inició este servidor: el arranque del entorno anterior fue bloqueado por su control de ejecución.

## Aislamiento

La demo crea SQLite nueva y aplica migraciones únicamente ahí. Produce configuración y CSV ficticio bajo `temp/`, ignorado por Git. Usa ese content root, rechaza configuración local, redirige APPDATA a una carpeta vacía propia para no cargar user-secrets y rechaza variables de entorno que sobrescriban secciones sensibles. No cambie esa configuración ni copie credenciales. EWS apunta a loopback puerto 1, el envío real queda desactivado, las direcciones ficticias usan `example.invalid`. No puede enviar correo ni consultar Conaset realmente.

La publicación no es un despliegue del servicio municipal. No use esta base para producción ni copie la base municipal dentro de la demo. No versionar bases, CSV generados ni capturas con personas reales.

## Recorrido disponible

Los nombres comienzan por DEMO. Las fechas se calculan al generar para mantener casos recientes y vencidos; la fecha de última carpeta conserva los cortes de Archivo, Oficina 43 y Digital. Son escenarios ficticios, no verificaciones legales de plazos.

- Entrada: agenda pendiente con clase de origen B; importe `temp/demo-local/agenda-demo.csv` para simular una cita más.
- Búsqueda: Archivo y Oficina 43 pendientes; otro caso ya subido.
- F8: solicitud pendiente y respaldo mensual opcional; otro F8 no generado con certificado por firmar.
- Certificados: por firmar y firmado pendiente de confirmar subida. Configure firmantes ficticios en la demo si necesita generar documentos; no use firmas o correos reales.
- Pedimos: pendiente de revisión, en plazo, vencido y subido por otra comuna.
- Nos piden: solicitudes precargadas de Archivo, Oficina 43, Digital, vencida y F8. Representan correos simulados, no una lectura EWS.
- Cancelaciones: una cita cancelada conservada para revisar y una nueva cita de la misma persona.
- Otorgamiento: carpetas con subida registrada, para seguimiento de decisiones ficticias.

El generador comprueba 18 carpetas, cuatro F8 y dos certificados. Esos conteos dejan de ser iguales cuando se realizan acciones o se importa el CSV; para repetir desde cero genere otra carpeta nueva, sin borrar ni sobrescribir una existente.

## Validación pendiente

La generación y las pruebas automatizadas no certifican la revisión visual del servidor. Continúan pendientes la pantalla de cancelaciones en ejecución, tema oscuro e impresión; además de correo/Conaset reales en una prueba operativa controlada futura.
