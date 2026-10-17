# Historial de ramas

Este repositorio tiene **una sola rama de trabajo: `main`**. Las ramas que se usaron antes están guardadas como **etiquetas `archivo/…`**: conservan todos sus commits, pero no aparecen como ramas. Para ver una, usa `git checkout <etiqueta>`.

El detalle de cada cambio está en el historial de `main` (`git log`), con commits en formato convencional (`feat`, `fix`, `style`, `docs`…).

## Cronología

| Fecha | Rama original | Etiqueta | Qué contenía | Estado |
|---|---|---|---|---|
| 2026-10-04 | `feat/sgl-mvp` | `archivo/2026-10-04-sgl-mvp` | Primera versión del sistema (MVP) | Dentro de `main` |
| 2026-10-07 | `claude/hopeful-planck-uazl91` | `archivo/2026-10-07-claude-hopeful-planck` | Trabajo de exploración | Dentro de `main` |
| 2026-10-07 | `feat/lico-comunas-pendiente` | `archivo/2026-10-07-lico-comunas-pendiente` | Directorio de comunas y asistente Lico | Dentro de `main` |
| 2026-10-11 | `fix/excel-import-validation` | `archivo/2026-10-11-fix-excel-import-validation` | Validación de la importación de Excel | Dentro de `main` |
| 2026-10-11 | `fix/import-transaction` (solo local) | `archivo/2026-10-11-local-fix-import-transaction` | Importación de Excel en una sola transacción | Dentro de `main` |
| 2026-10-16 | `integracion/remoto-peticiones` (solo local) | `archivo/2026-10-16-local-integracion-remoto-peticiones` | Integración intermedia con el módulo Peticiones | Dentro de `main` |
| 2026-10-17 | `feat/clase-licencia-duplicados` | `archivo/2026-10-17-feat-clase-licencia-duplicados` | Trabajo principal: duplicados, F8, certificados, Revisión y diseño | Fusionada en `main` |

## Cómo seguir trabajando

- Todo cambio nuevo se hace en `main`, con commits pequeños y convencionales.
- No se crean ramas por tarea. Si algún día hace falta probar algo aislado, se crea una rama temporal y se borra al fusionarla.
- Los hitos importantes se marcan con etiquetas de versión (por ejemplo `v2.0.0`).
