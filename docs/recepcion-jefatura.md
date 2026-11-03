# Recepción de agendas de jefatura

El flujo conserva la descarga de adjuntos del Organizador de citaciones y conecta su carpeta de entrada con Gestión de carpetas. El navegador lee archivos; no accede directamente al correo.

## Uso

1. Abrir `/importar` en Chrome o Edge del PC que recibe los documentos.
2. Pulsar **Conectar carpeta de correo** y elegir la carpeta `entrada` del Organizador de citaciones. Autorizar lectura cuando lo solicite el navegador.
3. Mantener abierta la aplicación. Revisa la carpeta cada minuto, incluso al cambiar de módulo. **Sincronizar ahora** permite revisar inmediatamente.
4. Si el navegador pierde el permiso, pulsar **Reanudar**. **Desconectar** elimina la conexión guardada.

Los CSV y XLSX se procesan por la fecha del nombre `citas_YYYYMMDD_HHMMSS`. Se conservan citas futuras. Una huella del contenido evita repetir adjuntos idénticos y permite detectar archivos corregidos. Los errores aparecen por archivo y se vuelven a intentar; revisar también las bandejas de duplicadas y canceladas indicadas por los resultados.

**Seleccionar agendas** permite importar varios archivos directamente al seleccionarlos, con un máximo de 30 archivos y 50 MB por archivo. El importador individual conserva su revisión y confirmación y permite cargar planillas DETALLE. Las agendas reutilizan el servicio existente, que protege el trabajo ya clasificado.

## Descarga opcional desde Outlook clásico

El Organizador puede seguir descargando adjuntos en su carpeta habitual. Como alternativa, `scripts/Recibir-AgendasJefatura.ps1` extrae adjuntos `citas_*.csv` y `citas_*.xlsx` de Outlook clásico:

```powershell
./scripts/Recibir-AgendasJefatura.ps1 -Destino 'C:\Agendas\entrada' -Dias 5
```

Admite filtros `-Remitente` y `-Subcarpeta`. No envía, mueve, elimina ni marca mensajes. Sin `-Destino`, utiliza `entrada-jefatura`, excluida de Git. La sintaxis fue comprobada; la conexión con el Outlook real debe comprobarse en el equipo receptor.

## Verificación y alcance

- `node --test tests/browser/recepcion-agendas.test.mjs`: nueve pruebas de orden, huellas, reintentos, persistencia, permisos, revisión manual, límites y desconexión.
- `dotnet test Sgl.slnx --no-restore -p:NuGetAudit=false -v quiet`: 2.779 pruebas aprobadas, sin omisiones ni errores (1.838 dominio, 848 aplicación y 93 Peticiones).
- `dotnet test tests/Sgl.Application.Tests --no-build --no-restore --filter 'FullyQualifiedName~Agenda|FullyQualifiedName~ExcelImport' -v quiet`: 103 pruebas aprobadas.
- Prueba de navegador aislada con SQLite ficticia: 16 comprobaciones de conexión, CSV, XLSX, revisión periódica, recarga, navegación, clasificación y carga individual. Se simula la selección y el permiso de carpeta; no sustituye la autorización nativa del usuario.
- Auditoría de siete carpetas ficticias: sin duplicados, cita de mañana conservada y clasificación previa intacta tras recibir una actualización.
- Activación local: las 466 carpetas reales permanecen sin cambios; no se conectó una carpeta ni se importaron adjuntos reales automáticamente.

Para retirar esta función, eliminar `EntradaAgendas.razor`, `agenda-entrada.js` y su montaje en `MainLayout.razor` y `Importar.razor`, junto con las reglas `.entrada-agendas-*` de `visual.css`. El script de Outlook es independiente. No requiere revertir servicios de importación, datos ni el diseño compartido.
