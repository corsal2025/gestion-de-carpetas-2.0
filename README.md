# Sistema de Gestión de Licencias de Conducir (SGL)

Módulo independiente de **gestión de carpetas** del Depto. de Licencias de Conducir (sedes Av. Argentina, Placilla, Merc. Puerto). Reemplaza el seguimiento de carpetas en planilla Excel.

**Creado por Raúl Salazar** — ver [AUTHORS.md](AUTHORS.md).

> ⚠️ Este repositorio contiene SOLO código. Los datos personales (RUT, nombres), bases de datos (`*.db`) y planillas (`*.xlsx`) nunca se versionan.

## Alcance (v0.3.0)
Módulo de carpetas, sin autenticación (uso local de prueba):
- Registrar carpeta (ciudadano con RUT validado por dígito verificador, sede, fecha de citación), también en línea desde el listado.
- Listado con búsqueda por RUT o nombre, filtros por sede, estado, trámite y caja, paginado, columnas congeladas, edición en línea y panel lateral de detalle.
- Detalle: cambio de estado con lista cerrada de **solo** los estados siguientes válidos, edición de datos e historial de cambios.
- Cada cambio queda registrado en el historial (fecha, autor, campo, valor anterior y nuevo). El autor se ingresa como texto en el campo «Funcionario».
- **Cajas de archivo**: asignar carpetas a cajas (individual o por lote), cola de caja, acta imprimible y reapertura de cajas.
- **Importador Excel** masivo (planillas multihoja por sede) que normaliza estados y valores.
- Listados PDF por sector (Archivo y Oficina 43) y guía interactiva «Cómo usar el sistema».
- **Interfaz**: tema claro/oscuro y dos estilos de fila (suaves, o los colores originales del Excel), configurables desde la barra superior y recordados por navegador. Los estilos viven en `src/Sgl.Web/wwwroot/theme.css`, que se carga después de `app.css` (ver «Diseño y paletas»).

Fuera de alcance (ya existen en otros sistemas): autenticación, usuarios, roles, filtrado por sede según usuario y dashboard.

## Stack
- .NET 10 · Blazor Server (render interactivo en servidor)
- Entity Framework Core · **SQLite por ahora** (proveedor aislado en Infraestructura; SQL Server soportado por configuración)
- xUnit (TDD)

## Estructura
```
src/Sgl.Domain          Entidades y reglas puras (Carpeta, Ciudadano, Rut, máquina de estados, historial)
src/Sgl.Application     Casos de uso (CarpetaService) y puerto ICarpetaRepository
src/Sgl.Infrastructure  EF Core: SglDbContext, repositorio, migraciones, selección de proveedor
src/Sgl.Web             UI Blazor Server
tests/                  Sgl.Domain.Tests, Sgl.Application.Tests (SQLite en memoria)
```

## Ejecutar
```bash
dotnet run --project src/Sgl.Web
```
Abrir http://localhost:5020 (el puerto 5010 lo usa otra aplicación). La base SQLite `sgl.db` se crea y migra automáticamente al iniciar.

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

## Fase 2: módulos, clasificación sin duplicados e inicio

Contrato: `docs/spec-fase2-reorganizacion.md`. Decisiones de diseño:

- **Clase obligatoria (R1)**: `Carpeta.Clasificar` exige al menos una clase; el servicio bloquea si el RUT ya tiene una carpeta abierta con una clase en común (también entre filas del mismo lote). Las carpetas antiguas no se tocan.
- **Alerta de duplicado (R2)**: se calcula en vivo al listar "Por clasificar" (carpeta abierta = sin decisión final, no anulada y sin caja real), así desaparece sola al resolverse. La importación informa cuántas filas entran con alerta. Reagendar = la carpeta conservada toma fecha y sede de la otra, que se anula con motivo "Reagendamiento".
- **Anulación (R3)**: flag `Anulada` + motivo/fecha/funcionario e historial; filtro global de EF (`HasQueryFilter`) la saca de módulos, contadores, estadísticas y duplicados. Solo la vista *Anuladas*, restaurar y la idempotencia de importadores usan `IgnoreQueryFilters()`. Nunca hay borrado físico.
- **Navegación (R4)**: Inicio (`/`) con tarjetas por módulo; dentro de un módulo, menú lateral (`ModuloLayout`) plegable a 56 px con estado en `localStorage`. La barra superior queda para herramientas globales: Inicio, Cajas, Cargar Excel y Estadísticas; *Posibles duplicados* y *Anuladas* viven en el menú lateral (grupos Entrada y Cierre).
- **Semáforo por módulo**: rojo si alguna carpeta alcanzó el umbral rojo (días desde citación, `Bandejas:Umbrales`), amarillo si alguna alcanzó el amarillo. Cerradas/Archivadas no llevan semáforo. Por clasificar: 1/3 días.
- **Serie de 7 días**: entradas diarias al módulo según el historial (agenda, registro, cambio de estado, caja real).
- **Alerta con varias abiertas**: si el RUT tiene 2+ carpetas abiertas se muestra la más reciente, el total y (al pasar el mouse) la lista completa. Dos filas del mismo RUT dentro de Por clasificar se alertan entre sí a propósito.
- **Reimportación**: una fila ya importada y luego anulada (p. ej. por reagendamiento) cuenta como "ya existía" y además se informa aparte como "ya existían (anuladas)"; nunca se vuelve a crear.
- **Restaurar**: bloqueado si el RUT tiene otra carpeta abierta (no archivada) con una clase en común; si solo coincide el RUT, se pide confirmación. Reagendar solo acepta como destino una carpeta abierta.
- **Unidad de trabajo**: el DbContext vive por circuito Blazor; toda mutación de `CarpetaService`, `DuplicadosService` e importadores descarta los cambios rastreados si falla (`ChangeTracker.Clear()`), y la clasificación en lote valida todas las filas antes de modificar alguna. Las carpetas anuladas solo admiten Restaurar (servicio y dominio lo exigen).
- **Escrituras serializadas por circuito**: `CarpetaService` serializa sus mutaciones con un semáforo (un `Clear()` nunca corre durante otro `SaveChanges`); el formulario de detalle se guarda con un solo comando y un solo `SaveChanges`.
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
- **Límites conocidos**: Anuladas, Integridad, Firmantes y las páginas de Peticiones (Razor Pages) no fueron rediseñadas: heredan la barra y el fondo nuevos pero conservan sus tarjetas y botones anteriores (en Peticiones los botones miden ~31 px). Los controles dentro de las tablas densas (fechas, comuna, selector de clase) miden menos de 40 px a propósito. Con el estilo de filas «Excel» en tema claro las filas se ven neutras (ver `docs/pendientes.md`, rediseño). Hay animaciones infinitas de Lico (rebote, parpadeo, saludo, travesuras), pulso de «búsqueda pendiente» y el spinner de carga; todas, salvo el spinner, se desactivan con `prefers-reduced-motion`.

## Pendiente
- Estado «Clase pendiente» no tiene transiciones de salida en el diagrama actual; confirmar flujo con el departamento.

## Arquitectura
Diagrama interactivo vivo: [`docs/arquitectura.html`](docs/arquitectura.html) (abrir en navegador).
Para actualizarlo: editar el bloque `DATA` del archivo y agregar entrada en `CHANGELOG`.

---
© 2026 Raúl Salazar. Todos los derechos reservados.
