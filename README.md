# Sistema de Gestión de Licencias de Conducir (SGL)

Módulo independiente de **gestión de carpetas** del Depto. de Licencias de Conducir (sedes Av. Argentina, Placilla, Merc. Puerto). Reemplaza el seguimiento de carpetas en planilla Excel.

**Creado por Raúl Salazar** — ver [AUTHORS.md](AUTHORS.md).

> ⚠️ Este repositorio contiene SOLO código. Los datos personales (RUT, nombres), bases de datos (`*.db`) y planillas (`*.xlsx`) nunca se versionan.

## Alcance (v0.2.0)
Módulo núcleo de carpetas, sin autenticación (uso local de prueba):
- Registrar carpeta (ciudadano con RUT validado por dígito verificador, sede, fecha de citación).
- Listado con búsqueda por RUT o nombre, filtros por sede y estado, paginado.
- Detalle: cambio de estado con lista cerrada de **solo** los estados siguientes válidos, edición de datos (sede, fechas, idoneidad moral) e historial de cambios.
- Cada cambio queda registrado en el historial (fecha, autor, campo, valor anterior y nuevo). El autor se ingresa como texto en el campo «Funcionario».

Fuera de alcance (ya existen en otros sistemas): autenticación, usuarios, roles, filtrado por sede según usuario, dashboard e importador Excel.

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

## Pendiente
- Importador Excel 2026 (normalizar valores como EXÁMEN/EXAMEN, espacios). No incluido en esta versión.
- Estado «Clase pendiente» no tiene transiciones de salida en el diagrama actual; confirmar flujo con el departamento.

## Arquitectura
Diagrama interactivo vivo: [`docs/arquitectura.html`](docs/arquitectura.html) (abrir en navegador).
Para actualizarlo: editar el bloque `DATA` del archivo y agregar entrada en `CHANGELOG`.

---
© 2026 Raúl Salazar. Todos los derechos reservados.
