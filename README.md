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
- **Interfaz**: tema claro/oscuro y dos estilos de fila (suaves, o los colores originales del Excel), configurables desde la barra superior y recordados por navegador. Los estilos viven en `src/Sgl.Web/wwwroot/theme.css`, que se carga después de `app.css`.

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
- **Meta del día**: clasificadas hoy (hora de Chile) frente a las que siguen en Por clasificar.

## Pendiente
- Estado «Clase pendiente» no tiene transiciones de salida en el diagrama actual; confirmar flujo con el departamento.

## Arquitectura
Diagrama interactivo vivo: [`docs/arquitectura.html`](docs/arquitectura.html) (abrir en navegador).
Para actualizarlo: editar el bloque `DATA` del archivo y agregar entrada en `CHANGELOG`.

---
© 2026 Raúl Salazar. Todos los derechos reservados.
