# Sistema de Gestión de Licencias de Conducir (SGL)

Módulo independiente de **gestión de carpetas** del Depto. de Licencias de Conducir (sedes Av. Argentina, Placilla, Merc. Puerto). Reemplaza el seguimiento de carpetas en planilla Excel.

**Creado por Raúl Salazar** — ver [AUTHORS.md](AUTHORS.md).

> ⚠️ Este repositorio contiene SOLO código. Los datos personales (RUT, nombres), bases de datos (`*.db`) y planillas (`*.xlsx`) nunca se versionan.

## Alcance (v0.3.0)
Módulo de carpetas, módulo de Peticiones de cambio de domicilio y módulo de Solicitudes (F8 y Certificado / Oficio). Sin autenticación por defecto; el login con Windows es opcional (ver «Seguridad y despliegue»):
- Registrar carpeta (ciudadano con RUT validado por dígito verificador, sede, fecha de citación), también en línea desde el listado.
- Listado con búsqueda por RUT o nombre, filtros por sede, estado, trámite y caja, paginado, columnas congeladas, edición en línea y panel lateral de detalle.
- Detalle: cambio de estado con lista cerrada de **solo** los estados siguientes válidos, edición de datos e historial de cambios.
- Cada cambio queda registrado en el historial (fecha, autor, campo, valor anterior y nuevo). El autor se ingresa como texto en el campo «Funcionario».
- **Cajas de archivo**: asignar carpetas a cajas (individual o por lote), cola de caja, acta imprimible y reapertura de cajas.
- **Importador Excel** masivo (planillas multihoja por sede) que normaliza estados y valores. Ya no está en la barra superior: se abre desde el botón «Cargar Excel Masivo» del listado o desde Estadísticas (`/importar`). Las filas sin citación válida o con RUT de dígito verificador incorrecto se reportan y se saltan; las fechas fuera de rango (2015 a hoy + 1 año) se avisan.
- **Peticiones** (`/peticiones`): peticiones de cambio de domicilio a otras comunas (importación de planilla, directorio de comunas y correos, envío por EWS, estadísticas, configuración de correo). Con `Peticion:TestModeEmail` el envío va solo a la casilla de prueba y ninguna petición cambia de estado. El reenvío manual se limita a los dominios de `Peticion:DominiosPermitidos` (si está vacío no se restringe y se avisa).
- **Solicitudes**: «Solicitudes de F8» (`/f8`) y «Certificado / Oficio» (`/certificados`, firmantes en `/configuracion/firmantes`). El correo al Director arranca en modo prueba (`Certificados:EnvioCorreoActivo` = false).
- Listados PDF por sector (Archivo y Oficina 43) y guía interactiva «Cómo usar el sistema».
- **Interfaz**: tema claro/oscuro y dos estilos de fila (suaves, o los colores originales del Excel), configurables desde la barra superior y recordados por navegador. Los estilos viven en `src/Sgl.Web/wwwroot/theme.css`, que se carga después de `app.css` (ver «Diseño y paletas»).

Fuera de alcance (ya existen en otros sistemas): usuarios y roles propios, filtrado por sede según usuario y dashboard.

## Stack
- .NET 10 · Blazor Server (render interactivo en servidor)
- Entity Framework Core · **SQLite por ahora** (proveedor aislado en Infraestructura; SQL Server soportado por configuración)
- xUnit (TDD)

## Estructura
```
src/Sgl.Domain          Entidades y reglas puras (Carpeta, Ciudadano, Rut, máquina de estados, historial)
src/Sgl.Application     Casos de uso (CarpetaService, Solicitudes, Integridad) y puerto ICarpetaRepository
src/Sgl.Infrastructure  EF Core: SglDbContext, repositorio, migraciones, selección de proveedor
src/Sgl.Peticiones      Razor Pages del módulo Peticiones (/peticiones)
src/Sgl.Web             UI Blazor Server
tests/                  Sgl.Domain.Tests, Sgl.Application.Tests, Sgl.Peticiones.Tests (SQLite en memoria)
```

## Ejecutar
```bash
dotnet run --project src/Sgl.Web
```
Abrir http://localhost:5030 (puerto de `launchSettings.json`). La base SQLite `sgl.db` se crea y migra automáticamente al iniciar.

## Pruebas
```bash
dotnet test
```

## Base de datos
Configuración en `appsettings.json` (o `appsettings.Local.json`, no versionado):
```json
{ "Database": { "Provider": "Sqlite" }, "ConnectionStrings": { "Sgl": "Data Source=sgl.db" } }
```
Para SQL Server: `"Provider": "SqlServer"` y la cadena de conexión correspondiente. Las migraciones incluidas son de SQLite; antes de usar SQL Server hay que generar un set de migraciones para ese proveedor y aplicarlo (`dotnet ef database update`). La migración automática al inicio solo se ejecuta con SQLite.

## Seguridad y despliegue

- **Autenticación opcional**: `Seguridad:RequerirAutenticacion` (por defecto `false`). Con `true` se usa autenticación Windows (Negotiate), todo el sitio exige usuario y el autor del historial pasa a ser el usuario de Windows. Falta verificarlo en el servidor de producción.
- **Cabeceras y descargas**: todas las respuestas llevan `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` y `Referrer-Policy: same-origin`; `/certificados/{id}/documento` y `/estadisticas/exportar` van con `Cache-Control: no-store`, comparan el origen de la petición con el host y la exportación valida `dias` (1 a 3650) y `desde <= hasta`. `AllowedHosts` se configura en `appsettings.json`.
- **Errores y render**: sin prerender (el circuito arma cada página una vez) y con `ErrorBoundary` en el layout con botón de reintento; los errores inesperados se muestran con mensaje genérico y el detalle va al log. Sin fuentes externas (Google Fonts retirado).
- **SQLite**: cada conexión activa WAL, `busy_timeout=5000` y `synchronous=NORMAL`; las escrituras de un circuito pasan por un único `UnidadDeTrabajoGate`.
- **Servicio de Windows**: `scripts/install_windows_service.ps1` (como administrador). Por defecto escucha solo en `localhost`; `-BindTodas` lo publica en la red (la aplicación no tiene login salvo que se active `Seguridad:RequerirAutenticacion`: restrinja con firewall). Parámetros: `-Port` (por defecto 5000), `-Credential` (cuenta de servicio dedicada; sin ella corre como LocalSystem y se advierte) y `-Force` (continuar sin `sgl.db` en la carpeta publicada). Al correr como servicio el directorio de trabajo es el de la aplicación publicada.
- **Carga inicial por script**: `python scripts/import_excel.py <excel.xlsx> <sgl.db> --force` BORRA Carpetas, Ciudadanos e HistorialCambios, hace respaldo previo y deja la fecha de subida vacía si no se puede leer (ya no usa 2026-01-02 por defecto).
- **Respaldo automático de Peticiones**: `Peticion:RespaldoAutomaticoActivo` (por defecto `true`). Activo, el servicio copia la base completa una vez al día a `Peticion:BackupDirectory` (por defecto `Documentos\RespaldoPeticionCambioDomicilio`). **Debe ponerse en `false` en entornos de prueba** (variable de entorno `Peticion__RespaldoAutomaticoActivo=false`): apagado no copia nada ni escribe en Documentos. No afecta a los respaldos manuales que pide el usuario desde la pantalla.
- **Configuración nueva**: `Seguridad:RequerirAutenticacion`, `Peticion:DominiosPermitidos`, `Peticion:TestModeEmail`, `Certificados:EnvioCorreoActivo`, y `ListadoBusqueda:Solicitante` (vacío en `appsettings.json`; el nombre real va en `appsettings.Local.json`, no versionado). La URL de EWS debe ser https.

## Fase 2: módulos, clasificación sin duplicados e inicio

Contrato: `docs/spec-fase2-reorganizacion.md`. Decisiones de diseño:

- **Clase obligatoria (R1)**: `Carpeta.Clasificar` exige al menos una clase; el servicio bloquea si el RUT ya tiene una carpeta abierta con una clase en común (también entre filas del mismo lote). Las carpetas antiguas no se tocan.
- **Alerta de duplicado (R2)**: se calcula en vivo al listar "Por clasificar" (carpeta abierta = sin decisión final, no anulada y sin caja real), así desaparece sola al resolverse. La importación informa cuántas filas entran con alerta. Reagendar = la carpeta conservada toma fecha y sede de la otra, que se anula con motivo "Reagendamiento".
- **Anulación (R3)**: flag `Anulada` + motivo/fecha/funcionario e historial; filtro global de EF (`HasQueryFilter`) la saca de módulos, contadores, estadísticas y duplicados. Solo la vista *Anuladas*, restaurar y la idempotencia de importadores usan `IgnoreQueryFilters()`. Nunca hay borrado físico.
- **Navegación (R4)**: Inicio (`/`) con tarjetas por módulo; dentro de un módulo, menú lateral (`ModuloLayout`) plegable a 56 px con estado en `localStorage`. La barra superior queda para herramientas globales: Inicio, Cajas y Estadísticas (la carga de Excel se abre desde el listado); *Anuladas* vive en el menú lateral (grupo Cierre). *Posibles duplicados* ya no existe: se resuelve en Por clasificar.
- **Semáforo por módulo**: rojo si alguna carpeta alcanzó el umbral rojo (días desde citación, `Bandejas:Umbrales`), amarillo si alguna alcanzó el amarillo. Cerradas/Archivadas no llevan semáforo. Por clasificar: 1/3 días.
- **Serie de 7 días**: entradas diarias al módulo según el historial (agenda, registro, cambio de estado, caja real).
- **Alerta con varias abiertas**: si el RUT tiene 2+ carpetas abiertas se muestra la más reciente, el total y (al pasar el mouse) la lista completa. Dos filas del mismo RUT dentro de Por clasificar se alertan entre sí a propósito.
- **Reimportación**: una fila ya importada y luego anulada (p. ej. por reagendamiento) cuenta como "ya existía" y además se informa aparte como "ya existían (anuladas)"; nunca se vuelve a crear.
- **Restaurar**: bloqueado si el RUT tiene otra carpeta abierta (no archivada) con una clase en común; si solo coincide el RUT, se pide confirmación. Reagendar solo acepta como destino una carpeta abierta.
- **Unidad de trabajo**: el DbContext vive por circuito Blazor; toda mutación de `CarpetaService`, `DuplicadosService` e importadores descarta los cambios rastreados si falla (`ChangeTracker.Clear()`), y la clasificación en lote valida todas las filas antes de modificar alguna. Las carpetas anuladas solo admiten Restaurar (servicio y dominio lo exigen).
- **Escrituras serializadas por circuito**: un único `UnidadDeTrabajoGate` por circuito (compartido por `CarpetaService`, F8, Certificados, Duplicados y la sincronización de correo) serializa las mutaciones (un `Clear()` nunca corre durante otro `SaveChanges`); el formulario de detalle se guarda con un solo comando y un solo `SaveChanges`.
- **PDF por sector**: sigue en el panel «Mostrar sedes y acciones» arriba de la tabla de cada módulo (se activa al marcar carpetas de Archivo u Of. 43).
- **Contraste AA**: auditado en claro/oscuro × filas suaves/Excel en todos los módulos (≥ 4,5:1). Cuatro colores Excel con texto blanco se oscurecieron un tono (Cambio de domicilio, En Archivos, No existe carpeta, Of. 43).
- **Meta del día**: clasificadas hoy (hora de Chile) frente a las que siguen en Por clasificar.

## Fase 3: interacciones entre estados, listados para búsqueda e integridad

Contrato: `docs/spec-fase3-interacciones.md` (R6–R12). Decisiones de diseño:

- **Clasificación por fecha (R6)**: en Por clasificar, si solo se escribe la fecha de última carpeta, la acción por defecto es «Buscar carpeta física»: el destino (Archivos antes del 01-07-2023, Of. 43 desde entonces) se calcula en el dominio (`Carpeta.ResolverDestinoClasificacion`) y la fila lo muestra antes de aplicar. Un estado elegido a mano siempre se respeta; sin fecha ni estado no se puede aplicar. El lote admite «Buscar carpeta física» usando la fecha ya guardada de cada carpeta (todo o nada). Las fechas se leen día-primero (`02-07-2023` es 2 de julio) y se guardan en ISO.
- **Fecha de etapa y cierre (R7)**: `FechaEstadoActual` se re-estampa en cada cambio de estado (instante UTC, se muestra en hora de Chile); `FechaSubida` solo se estampa si está vacía y nunca antes de la citación; `FechaCierre` se anota al entrar a un estado de cierre (subidas, certificado/oficio) o con decisión final, y se limpia si la carpeta se reabre. Las carpetas anteriores se rellenan una vez al iniciar desde el historial (`EtapaBackfill`), sin inventar fechas: sin dato, queda vacío. Antes de migrar se hace una copia `*.backup-…-premigracion` de la base.
- **Transiciones (R8)**: la máquina de estados agrega Archivos/Of. 43 → {Subida a Conaset, No existe carpeta} y No existe carpeta → {Subida con F8, Crear certificado/oficio, Subida con oficio, Archivos, Of. 43}. La edición rápida del listado sigue siendo una corrección manual (`ForzarEstado`, auditada).
- **Cierre por subida (R9, opción A)**: la carpeta se queda en su pestaña «Subida…»; no existe «mover a Cerradas».
- **Listado para búsqueda (R10)**: botón en Archivos y Of. 43 (todo el módulo bajo los filtros, o solo las marcadas), orden por fecha de última carpeta, folio y emisión en hora de Chile, casillas «encontrada / no existe». «Marcar como impreso» deja `FechaImpresion` auditada (se limpia si la carpeta sale de Archivos/Of. 43). Tras clasificar a Archivos/Of. 43 se ofrece el listado de lo recién clasificado.
- **Deshacer (R11)**: toast «Pasó a <módulo>» de 10 s. Cada cambio devuelve un punto de restauración (estado, decisión, fechas, marca de búsqueda, impresión); deshacer se rechaza si la carpeta cambió de nuevo (no pisa decisiones de otra persona) y queda auditado.
- **Vigilante de integridad (R12)**: `IntegridadService` evalúa cinco reglas (una sola bandeja, suma de bandejas = total y = panel, mismo RUT/misma clase, anuladas invisibles, fechas imposibles) leyendo los mismos filtros y contadores que usan las pantallas. Corre al iniciar, tras cada importación (`IntegridadSolicitudes`) y cada 24 h (`IntegridadHostedService`); guarda solo conteos en `IntegridadCorridas`. Inicio muestra un aviso rojo con la cantidad y enlaza a `/integridad`, donde se ven las carpetas afectadas (el aviso no lleva datos personales).
- **Caché del orden de bandejas (ARB6-01)**: `OrdenCacheado` guarda los ids ordenados junto con la versión de datos (`MAX(HistorialCambios.Id)`); se revalida cada 3 s y se descarta en cuanto una ventana vuelve incompleta.

## Fase 4: cambio de domicilio, asistencia del día y Reclasificación

- **Cambio de domicilio con sentido**: cada carpeta es *saliente* (la pedimos nosotros; plazo de 15 días hábiles desde la citación, sin reiniciarse al reenviar) o *entrante* (nos la piden por correo; plazo de ~12 días hábiles desde la llegada del correo, `CambioDomicilio:PlazoEntranteDias`). La saliente nunca puede ir a F8, oficio ni certificado. `CambioDomicilio:EnvioReal` es `false` por defecto (modo prueba: los correos van solo a la casilla de prueba).
- **Calendario de plazos**: los cálculos incluyen feriados nacionales de 2026 y 2027. Es necesario verificar feriados locales, extraordinarios y años posteriores; se agregan en `appsettings.Local.json` mediante `CambioDomicilio:FeriadosAdicionales`, por ejemplo `["2026-12-09"]` (fechas ISO). Los contadores de días restantes en las bandejas son días corridos hasta el vencimiento calculado en días hábiles. El cálculo histórico de Peticiones usa la fecha de envío, distinta del seguimiento de carpetas.
- **Reclasificación** (`/cambio-domicilio/reclasificacion`, primera etapa de Cambios de domicilio): toda carpeta que entra a Cambio de domicilio (clasificada a mano o llegada por correo) queda con la marca `PendienteReclasificacion` y no aparece en Por pedir ni en Nos piden hasta que una persona la confirme (una, varias o todas) o la corrija a otro destino, con «Deshacer». No es un estado nuevo: la máquina de estados no cambia y las carpetas existentes quedan sin pendiente (migración `ReclasificacionCambioDom`, default `false`). La puerta también se aplica en `CambiarEstado`/`ForzarEstado` y en los botones del Listado y del Detalle.
- **Asistencia del día, Otorgamiento y Cajas**: agenda diaria con asistencia, resolución de otorgamiento (Otorgada/Denegada) y rótulos de caja por serie.
- **Pruebas**: Domain 1833, Application 797, Peticiones 92 (todas en verde al cierre de la fase).

### Diseño del panel

- **Una familia visual**: la barra superior usa un único acento (amarillo = activo); «Cómo usar el sistema» es acción secundaria. Lico vive en una tarjeta contenida de Inicio y la mascota de la barra no lleva globo flotante.
- **Inicio**: grupos de etapa como paneles con su total, tarjetas compactas en rejilla fluida (`auto-fit`), leyenda del semáforo y de las barras de 7 días (con valores diarios en el tooltip; sin entradas se dice «sin movimientos»). La meta del día tiene etiqueta, mensaje motivador y estado vacío propio.
- **Movimiento**: solo transiciones cortas; con `prefers-reduced-motion` desaparecen (toast, pulso, elevación de tarjetas).
- **Contraste**: verificado ≥ 4,5:1 en claro y oscuro para los pares nuevos (avisos, toast, grupos, chips). Estilos nuevos en `wwwroot/polish.css` (se carga al final).

### Diseño y paletas

- **Paletas (6)**: Institucional (`inst`, la original y la de por defecto), Océano (`ocea`), Bosque (`bosq`), Atardecer (`atar`), Violeta (`viol`) y Grafito (`graf`). Se eligen con el botón de color de la barra superior (en el estilo `institucional` se ven los 6 puntos en línea). Solo cambian colores de acento (botones, encabezados, marca, anillo de foco, barra superior); el semáforo, los colores de estado/Excel y las superficies neutras no cambian.
- **Dónde se guardan**: atributo `data-palette` en `<html>` y `localStorage` clave `sgl-palette` (junto a `sgl-theme`, `sgl-rows` y `sgl-design`). El script de `Components/App.razor` los aplica antes del primer pintado; `window.sglUi.setPalette(clave)` (en `app.js`) los cambia.
- **Estilos**: `claro` (por defecto) es el diseño con vidrio y fondo aurora; `institucional` conserva el aspecto anterior (barra azul, tablas clásicas) y las pantallas nuevas no lo tocan (`html:not([data-design="institucional"])`).
- **Orden de carga de CSS** (el último gana): `app.css` → `theme.css` (tokens, temas, paletas) → `modulos.css` → `polish.css` → `shell.css` (barra flotante, menú lateral, fondo) → `listado.css` (Listado y barra superior de una fila) → `paginas.css` (Detalle, Estadísticas, Cajas) → `solicitudes.css` (Certificados, F8, Importar). Las hojas antiguas usan `!important` y `:not(#sgl)`; las reglas nuevas que deban ganarles repiten ese prefijo.
- **Agregar una paleta**: (1) en `theme.css`, sección «Palettes», copiar un bloque y cambiar la clave: `--pal-c` más, para claro y para oscuro, `--pal-rgb`, `--lico-accent`, `--lico-accent-hover`, `--lico-accent-soft`, `--btn-grad`, `--btn-grad-hover`, `--head-grad`, `--dots`, `--ring`, `--brand-primary`, `--brand-primary-hover`, `--brand-light`, `--topbar-bg`, `--topbar-border` (y en claro `--topbar-text`, `--topbar-muted`); (2) agregar la clave a `PALETTES` en `app.js` y a la lista de `App.razor`; (3) agregar su botón `.sw` en `MainLayout.razor`. Verificar contraste ≥ 4,5:1 en claro y oscuro.
- **Límites conocidos**: Peticiones (Razor Pages) usa el mismo marco (paletas, estilo, tema; `peticiones.css` adapta los tokens de `css/site.css`). Los controles dentro de las tablas densas (fechas, comuna, selector de clase, observación) miden 36 px para mantener filas de una línea. Con filas «Excel» en tema claro se ven los colores originales del Excel (texto ≥ 4,5:1). Hay animaciones infinitas de Lico (rebote, parpadeo, saludo, travesuras), pulso de «búsqueda pendiente» y el spinner de carga; todas, salvo el spinner, se desactivan con `prefers-reduced-motion`.

## Pendiente
- Estado «Clase pendiente» no tiene transiciones de salida en el diagrama actual; confirmar flujo con el departamento.
- Reclasificación: la clasificación inicial excluye Solicitado/Subido y las otras etapas posteriores. Excel rechaza y reporta las filas que actualizan carpetas pendientes, conservándolas para revisión manual. Sigue pendiente comprobar visualmente todos los accesos de entrantes.
- La bandeja «Cambios de domicilio que nos solicitan por correo» también lista salientes; decidir si debe mostrar solo entrantes.
- Antes de producción: autenticación del endpoint de pantallazos, buzón real de correo (secretos fuera del repo) y piloto en paralelo con el Excel. `SubirPruebaAsync` exige un pantallazo de envío real y que haya pasado el día de vencimiento, calculado desde la citación con el plazo configurado y sus feriados.
- La confirmación y corrección de Reclasificación usan la marca pendiente como token de concurrencia: una operación basada en datos anteriores se rechaza si otro funcionario ya confirmó la carpeta.

## Arquitectura
Diagrama interactivo vivo: [`docs/arquitectura.html`](docs/arquitectura.html) (abrir en navegador).
Para actualizarlo: editar el bloque `DATA` del archivo y agregar entrada en `CHANGELOG`.

---
© 2026 Raúl Salazar. Todos los derechos reservados.

### Cancelaciones en las cargas sucesivas de agenda

Las cancelaciones se procesan antes de las citas activas de cada archivo. Una reserva identificada sin ambigüedad anula su carpeta de agenda únicamente si sigue en Por clasificar, sin trabajo manual ni solicitudes o asistencia; se conserva íntegra en Anuladas. Si ya hubo trabajo o la carpeta fue restaurada, aparece en «Cancelaciones por revisar» para una decisión explícita y auditada. Nunca se anula solo por coincidir el RUT ni por faltar una fila en una descarga posterior. Un archivo activo antiguo no reactiva reservas ya canceladas.

Las estadísticas guardan un identificador pseudónimo SHA-256 de la reserva, sin RUT ni nombre en texto, para distinguir cancelaciones con el mismo horario. No es anonimización irreversible. Los registros anteriores sin identificador se preservan; cada coincidencia histórica puede vincularse al reimportar y las reservas adicionales se cuentan por separado. Sin identificador de reserva y con datos incompletos no puede garantizarse identidad absoluta; las coincidencias de carpetas ambiguas requieren revisión. La migración de identidad no admite reversión automática porque restituir la antigua unicidad podría eliminar estadísticas válidas; se debe usar el respaldo previo si se necesita volver a una versión anterior.
