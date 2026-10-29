# Pendientes

## Ronda de auditoría (2026-10-29)

Commits desde el 20 de octubre: `1008b45` (20-10, restringir acciones de barra y pulir tablas), `346e3cb` (27-10, interacciones entre módulos, diseño y cálculo de plazos), `4c5250e` (27-10, solicitudes especiales en la ficha de carpeta y exportaciones seguras) y `c556bd0` (29-10, cabecera compacta y contraste). Los cambios de esta ronda siguen sin commit en el árbol de trabajo.

Resuelto en esta ronda (la verificación con `dotnet build` y `dotnet test` la hace quien integra):
- **Concurrencia**: un solo `UnidadDeTrabajoGate` por circuito compartido por Carpetas, F8, Certificados, Duplicados, Panel y la sincronización de correo (antes cada servicio tenía su semáforo sobre el mismo DbContext).
- **Datos**: editar desde el drawer del listado ya no borra la idoneidad moral; la sincronización de correo no cierra carpetas por correos anteriores a su registro; Deshacer cierra la solicitud F8 automática; F8 subido / no generado y firmar certificado no mueven una carpeta que ya salió del estado de origen; `ForzarEstado` valida el estado; el punto de restauración incluye la fecha de petición; fechas de reagendar/registrar validadas; fechas ambiguas mes-primero eliminadas.
- **Peticiones**: el modo prueba no marca Enviada ni cambia el estado; estado intermedio «Enviando» con actualización atómica (sin doble envío), un correo por comuna, sin cancelación a mitad de envío; la importación no borra obsoletas si se omitió una hoja; el reenvío manual se restringe a `Peticion:DominiosPermitidos`; EWS solo https y sin caracteres de control; escritura atómica del directorio de comunas; ZIP validado antes de abrir el Excel.
- **Importación**: filas sin citación válida o con DV incorrecto se reportan y se saltan; rango de fechas compartido; `scripts/import_excel.py` exige `--force`, respalda y ya no inventa la fecha 2026-01-02.
- **Base de datos**: WAL + `busy_timeout`, índices de `FechaSubida` e `HistorialCambios` (migración `AgregarIndicesFechaSubidaYHistorial`), `EtapaBackfill` sin trabajo si no hay pendientes.
- **Seguridad y despliegue**: cabeceras, `no-store` en descargas, validación de origen y de `dias`, autenticación Windows opcional (`Seguridad:RequerirAutenticacion`), instalador en `localhost` por defecto (`-BindTodas` para la red), sin Google Fonts, sin nombre real en `appsettings.json`, descarga del certificado y firmantes con auditoría (ver README, «Seguridad y despliegue»).
- **Interfaz**: `ErrorBoundary`, sin prerender, mensajes de error genéricos, accesibilidad básica, código muerto retirado (`CertificadoSinCorreo`, `UndoToast`, JS y logo duplicados, `update_css.py`).

Pendientes que requieren decisión o verificación (no se tocaron):

| ID | Qué pasa | Qué hace falta |
|---|---|---|
| F3-01 | Causa raíz encontrada: el propio Excel ya trae unas 3.754 filas con subida anterior a la citación (pico a 14 días); es un tema de negocio, no de lectura del archivo. Además hay 63 carpetas con fecha de subida 2026-01-02 puesta por defecto por la versión antigua del script. | Decidir con Gilda si se corrigen o se aceptan; las 63 carpetas con 2026-01-02 son datos inventados y conviene vaciarlas. |
| A-04 / F3-02 | R9 sigue en la opción A (la carpeta se queda en su pestaña «Subida…»; no se mueve a Cerradas). | Confirmar con el usuario si es abierta (A) o cerrada (B). |
| M-03 | Transiciones sin definir en `EstadoTransitions`: NoExisteCarpeta, CanjeLicExtranjera y Citada. | Definir las aristas con el departamento. |
| M-16 | Los feriados no se descuentan del plazo legal. | Decidir si se incorporan y desde qué calendario. |
| M-04 / M-17 | Reglas de las fechas de cierre y de subida (cuándo se estampan y cuándo se limpian). | Decisión de negocio. |
| Auth | La autenticación con Windows no se ha probado en producción. | Activar `Seguridad:RequerirAutenticacion` en el servidor y verificar usuarios, cuenta de servicio y autor del historial. |
| Navegadores | Solo se probó el navegador integrado/Edge. | Revisar en Chrome y Firefox reales, bandejas de más de 200 filas e impresión a PDF. |

## Cierre de fase 3

Resuelto en esta fase: ARB6-01 (orden cacheado entre sesiones), ARB6-02 (reparación de comunas en el registro de importaciones), ARB6-04 (campo Funcionario fluido), ARB6-05 (títulos «<Módulo> · Gestión de Carpetas 2.0») y R6–R12 de `spec-fase3-interacciones.md`.

| ID | Sev. | Qué pasa | Acción sugerida |
|---|---|---|---|
| F3-01 | ALTA (datos) | El vigilante de integridad detecta 3.673 carpetas importadas con «fecha de subida anterior a la fecha de citación» (todas con citación en 2026, la mayoría en Subida a Conaset / con F8). Es un dato heredado: el aviso rojo de Inicio seguirá visible hasta corregirlas. | Revisar si la citación del Excel viene con día/mes invertidos o si la subida es correcta; decidir corrección masiva con Gilda. |
| ARB6-03 | BAJA | 247 carpetas de cambio de domicilio quedaron sin fecha de última carpeta tras la reparación, así el PDF por sector no las clasifica. 103 valores heredados no se reconocen. | Revisar a mano o con CAS Chile. |
| F3-02 | BAJA | R9 queda en la opción A (la carpeta se queda en su pestaña «Subida…»; sin mover a Cerradas). | Confirmar con el usuario si se prefiere B. |
| F3-03 | BAJA | «Fecha de etapa» de carpetas anteriores a esta fase es la del último cambio de estado auditado o, si no hubo, la de ingreso al sistema (no la fecha real del Excel). | Informativo. |
| F3-04 | BAJA | La verificación visual se hizo con Edge (CDP). Falta mirar en Chrome real de la oficina y con la impresión a PDF física. | Revisar el martes. |
| Pendiente | RESUELTO | «Clase pendiente» transita a EsperaExamen, Otorgado y ParaDenegar. | Resuelto en EstadoTransitions y tests unitarios. |

## Para retomar (2026-10-15)
- **Mejoras UX resueltas**
  - Columna «Fecha últ. carpeta» de «Por clasificar»: ajustado para que cuando el trámite de agenda es Primera Licencia, se bloquee el calendario (no lleva fecha anterior) y se preseleccione «Primera Licencia» en el destino automáticamente.
  - Lupa de duplicado en «Por clasificar»: se mantiene visible en filas con alerta para navegación directa con un click a las demás carpetas del RUT.
- **Pendientes de datos heredados**
  - Cierre por subida (R9): se mantiene la opción A (la carpeta se queda en su pestaña «Subida…» con fecha de cierre automática).
  - 3.673 carpetas heredadas con fecha de subida anterior a la citación (vigilante de integridad; el aviso rojo se retiró de Inicio, la vista «Integridad» sigue en el menú).
- **Datos de CAS Chile** (el usuario los extrae por años): importar como tabla de consulta por RUT (última carpeta, lugar, clases).
- **Pendientes de revisión visual**: probar en Chrome real las bandejas grandes (> 200 filas); el navegador de pruebas no cargaba sus filas.
- **App**: Soporte de Servicio de Windows integrado (`builder.Host.UseWindowsService()`, `scripts/install_windows_service.ps1`, `scripts/uninstall_windows_service.ps1`).

## Duplicados en Por clasificar (2026-10-14)

Decision: el modulo "Posibles duplicados" se elimino; los duplicados se muestran y resuelven en Por clasificar.

Cambios
- Revertido 1b85d19: los ciudadanos con duplicado pendiente vuelven a aparecer en bandejas y contadores (como antes).
- Eliminados: pagina `/duplicados`, entrada de menu lateral, tarjeta/contador del panel asociado, CSS `nav-duplicados`. El enlace de Estadisticas ahora lleva a Por clasificar.
- Por clasificar: en cada fila roja, boton "Revisar" (junto a la lupa) abre un panel con motivo en lenguaje claro, tabla de las otras carpetas abiertas del RUT (sede, citacion, estado, subida) y acciones Reagendar, Anular duplicada / Anular la otra, Mantener ambas y enlace a todas las carpetas del RUT.
- "Mantener ambas" ahora oculta la alerta en la bandeja hasta que se registre otra carpeta abierta posterior a la revision (misma regla que usaba el modulo).
- El mensaje de Lico del panel ahora dice "...por revisar en Por clasificar".

- Vista "Por arreglar (N)" en Por clasificar (chip, `?bandeja=PorClasificar&porArreglar=1`): lista los casos pendientes aunque todas sus carpetas esten en otras bandejas (RUT, nombre + lupa, carpetas abiertas, motivo), con comparacion y acciones (Conservar, Reagendar, Anular, Mantener ambas). No altera el contador de Por clasificar.

Por revisar / decidir
- `PanelDto.DuplicadosPendientes` y `DuplicadosService.ContarPendientesAsync` siguen existiendo (alimentan el mensaje de Lico); decidir si se retiran.
- Duplicados cuya otra carpeta esta en otra bandeja (p. ej. Archivos): la fila roja solo aparece mientras haya una fila en Por clasificar; si ambas estan fuera, el caso queda visible solo en Integridad.
- RUT invalido que el parser acepta (p. ej. 586.466.165-5): el motivo dice "RUT invalido"; no hay accion de corregir RUT en el panel.
- Filas con cita futura: se tratan igual que las demas (no hay regla especial); decidir si Reagendar debe permitirse hacia fecha futura.
- "Mantener ambas" y nueva importacion: si la agenda trae otra carpeta abierta del mismo RUT despues de la revision, la alerta vuelve a aparecer (esperado).
- Contadores de bandejas y panel incluyen duplicados otra vez (antes de 1b85d19); no se descuentan.
- "Reagendar" desde el panel conserva la otra carpeta y anula la fila actual; no hay variante "conservar esta y anular la otra con su fecha" (usar "Anular la otra").
- La UI no fue verificada en navegador por el agente.

- **Duplicados en Por clasificar (2026-10-14)**: los casos "por arreglar" (RUT con más de una carpeta abierta sin revisar) ya van dentro de la misma tabla de Por clasificar, ARRIBA de las carpetas normales: fila separadora "POR ARREGLAR · N casos", una fila roja por caso (orden apellido/nombre, de a 100 con "Mostrar más"), luego separador "POR CLASIFICAR · M carpetas" y las filas normales. Botón "Resolver" abre la comparación (Conservar / mantener ambas / Reagendar / Anular). Se omiten los ciudadanos que ya tienen fila propia en la bandeja. El chip "Por arreglar (N)" ahora salta a la sección; el contador del menú lateral no cambia. Pendiente: si Por clasificar está vacía (o el filtro no deja carpetas) se muestra el estado vacío y no los casos.

## Módulos F8 y Certificado / Oficio — Fase A (2026-10-16)
Revisión adversarial: sin hallazgos bloqueantes ni altos. Pendientes para decidir:
- **MEDIA — escrituras simultáneas:** cada servicio (`CarpetaService`, `SolicitudesF8Service`, `CertificadosService`) tiene su propio semáforo sobre el mismo `DbContext` del circuito. Dos escrituras a la vez podrían dar un error de EF. Es el patrón que ya existía; arreglo posible: un solo semáforo por circuito.
- **MEDIA — F8 que queda abierto:** si la carpeta se mueve por otro camino (mover estado a mano, importación de Excel, Deshacer) la solicitud de F8 sigue en "Solicitado". Arreglo posible: que "F8 subido" / "No se pudo generar" se nieguen si la carpeta ya no está en un estado desde el que se pide F8.
- **BAJA — firmar certificado fuerza `SubidaConOficio`** aunque la carpeta ya esté en Otorgado/Denegado. Arreglo: proteger los estados finales.
- **BAJA — mensaje de error del envío de correo** se devuelve tal cual al usuario; con un adaptador real podría filtrar datos del servidor.
- **BAJA — BOM** agregado al inicio de `EstadoTransitions.cs` (cosmético).
- El correo al Director arranca en **modo prueba** (no envía nada): falta el adaptador real.

## Módulos F8 y Certificado / Oficio — Fases B y C (2026-10-16)
Revisión adversarial: 1 alto (corregido: F8 no puede pisar una carpeta ya Otorgada/Denegada) y pendientes menores:
- **MEDIA — descarga del certificado** (`GET /certificados/{id}/documento`): sin autenticación, cualquiera en la red con el id obtiene el .docx con datos personales. Agregar `Cache-Control: no-store`; resolver de fondo cuando exista inicio de sesión.
- **MEDIA — Firmantes** (`/configuracion/firmantes`): se pueden editar sin indicar el funcionario ni dejar registro. Si se activa `Certificados:EnvioCorreoActivo`, alguien podría cambiar el correo del Director. Exigir nombre de funcionario y auditar.
- **BAJA — caracteres de control** en nombre o motivo podrían hacer fallar el armado del correo EWS (el certificado igual se crea; el Director no recibe aviso).
- **Pendiente de producto:** probar la generación real en Word (no se abrió un .docx generado) y el envío EWS contra Exchange.

## Rediseño de interfaz — Etapa 6 (2026-10-17)
Cerrado: filas Excel en tema claro, Peticiones con el estilo nuevo, Anuladas/Integridad/Firmantes, controles de tabla a 36 px, limpieza de CSS sin uso, spinner calmo con movimiento reducido y barra de Peticiones en dos columnas en teléfono. Queda abierto:
- **BAJA — no verificado:** Firefox/Safari (solo se probó el navegador integrado), impresión, lector de pantalla y teclado completo en Peticiones.
