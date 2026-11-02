# Continuidad de implementación — 1 de noviembre de 2026

## Implementado

- Agenda acumulativa: contacto y trámite faltantes se completan sin sobrescribir correcciones; diferencias y coincidencias ambiguas se informan. Clase de origen opcional separada de la clasificación confirmada.
- Cancelaciones antes de altas: se anula la carpeta de agenda sin trabajo conservando historial; con trabajo queda en `/agenda/cancelaciones` para conservar/anular explícitamente. Reimportar o cargar un archivo antiguo no reactiva reservas canceladas.
- Estadísticas de cancelaciones con identidad pseudónima para distinguir reservas del mismo horario; no es anonimización irreversible.
- Clasificación conserva destino manual y no inventa fecha de última carpeta para primera licencia; informa período digital.
- Certificados: firma separada de confirmación de subida; control optimista de cambios de estado, fecha de etapa, decisión y anulación entre sesiones.
- Plazos: Pedimos 15 días hábiles desde citación; Nos piden 12 estimados desde recepción. Calendario nacional 2026–2027 y feriados adicionales configurables; fuera de cobertura se advierte.
- F8 diario y respaldo de penúltima opcional en cola mensual, con agregar, retirar y marcar subida auditados.
- Navegación agrupada conservando rutas y funciones; resumen de seguimiento en otorgamiento. Idoneidad moral corresponde a otra sección.

## Verificación registrada

- Suite final: 2.763 pruebas aprobadas (835 aplicación, 1.836 dominio, 92 Peticiones).
- Auditoría independiente de cancelaciones: 64 pruebas aprobadas; detectó y se corrigió un fallo de resolución por anulación.
- CSV reales del 30 de octubre, probados solo en SQLite nueva aislada: mañana 502 activas; tarde 532 activas y 53 cancelaciones estadísticas, sin falsas alertas. Repetición: cero altas y cero historial adicional. Originales intactos.
- Gitleaks de cambios y archivos nuevos sin hallazgos; compilación aislada porque existe una aplicación abierta.
- QA sintética de escritorio/móvil: agenda/clase, F8, certificados, respaldo y otorgamiento. Firma no mueve carpeta; confirmar subida sí. Script ui-audit bloqueado por incompatibilidad CDP, con comprobaciones DOM equivalentes. Tema oscuro e impresión física pendientes.

## Antes de producción

- QA estática de cancelaciones: se retiró la clase de solicitudes que transformaba su tabla en tarjetas con rótulos de certificados incorrectos. Conserva sus encabezados semánticos y estilos generales de tabla en ambos temas. Prueba de regresión falló antes y pasa después; suite aislada: 2.764 aprobadas (836 aplicación, 1.836 dominio, 92 Peticiones).
- QA runtime de esta corrección, tema oscuro e impresión sigue pendiente: el arranque demo fue rechazado por el control de ejecución; los servidores antiguos detectados no acreditan aislamiento ni versión actual. No se reutilizaron ni se rodeó el bloqueo.

1. Revisar visualmente la nueva pantalla de cancelaciones y resolver casos sintéticos.
2. Comprobar correo y Conaset reales con destinatarios controlados. No se enviaron correos ni se verificó integración real con Conaset.
3. Respaldar la base real antes del despliegue. Las migraciones nuevas solo se aplicaron a bases aisladas; no a producción.
4. Revisar migraciones de respaldos, clase de agenda e identidad de cancelaciones; la reversión automática de identidad de cancelaciones está impedida para evitar pérdida de registros.
5. Mantener calendario de feriados locales/extraordinarios y años posteriores a 2027.

## Simulación y datos de prueba

La simulación integral contiene 18 carpetas DEMO, cuatro F8, dos certificados y cinco solicitudes entrantes, en scratch ignorado separado de los datos reales. El lanzamiento del servidor demo fue rechazado por el control de ejecución; no hay URL activa. No rodear ese bloqueo.

CSV reales, bases, temporales, capturas y configuraciones locales no deben incluirse en Git. El código está preparado para revisión y despliegue posterior; este guardado no actualiza la aplicación en uso.

## Demo portable para otra estación — 2 de noviembre de 2026

- Fuentes versionadas en `scripts/demo`, instrucciones en `scripts/demo/LEEME.md`: generan una base nueva ficticia (18 carpetas, cuatro F8, dos certificados y cinco solicitudes entrantes), un CSV DEMO y configuración local de prueba.
- El generador se niega a reemplazar una carpeta existente. Las bases y archivos generados quedan bajo `temp/`, ignorados por Git; no se publica una base ni archivos de ciudadanos.
- Arranque manual explícito en PowerShell 7/Windows: content root de demo, APPDATA aislado, sin configuración local ni variables de entorno de secciones sensibles, correo real desactivado y EWS loopback. No modifica la configuración de producción.
- La generación y publicación se comprobaron sin levantar un servidor. La prueba de seguridad de paquete falló antes y pasó con el launcher; la validación visual runtime sigue pendiente y el rechazo previo de ejecución no se ha rodeado.
- Esta demo permite revisar desde otra estación autorizada tras clonar la rama; no constituye despliegue municipal ni comprobación de correo/Conaset real.
- Verificación de esta entrega: generador produce 18/4/2/5 registros esperados y publicación Release completa; prueba de paquete pasa. Suite ejecutada: dominio 1.836 y aplicación 837 aprobadas, Peticiones 91/92. Fallo reproducido por separado en `LosRespaldosAutomaticosNoDesplazanALosManuales`: esperaba 30 y obtuvo 32. No se afirma suite completa aprobada ni se atribuye su causa a esta entrega sin diagnóstico.

## Corrección de retención de respaldos — 2 de noviembre de 2026

- El fallo anterior se diagnosticó con una prueba nueva: intentar abrir exclusivamente una copia recién cerrada fallaba en Windows porque la conexión SQLite destino seguía retenida por el pool. No se cambiaron las expectativas de retención.
- Se desactivó pooling también en la conexión destino de `Backup`, igual que ya se hacía en la fuente. Pasan la prueba de archivo libre y la retención separada de 30 respaldos automáticos, conservando los manuales.
- Suite completa recompilada en salida aislada: **2.766 aprobadas** (837 aplicación, 1.836 dominio y 93 Peticiones). No se tocó ninguna base real ni se arrancó un servidor.
