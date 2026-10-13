# Spec fase 3 — Interacciones entre estados, fechas automáticas y listados PDF

Acordado con el usuario (protocolo Fase 1, 2026-10-12/13). Alcance: llegar hasta el traspaso de cada carpeta a su módulo; los módulos ya diseñados se conectan después (fuera de alcance).

## Flujo
| Paso | Evento | Resultado |
|---|---|---|
| 1 | Entra la agenda (correo de Gilda) | Por clasificar |
| 2 | Javiera ingresa la fecha de última carpeta y aplica | Antes de 01-07-2023 → "Se encuentra en Archivos"; desde 01-07-2023 → "Se encuentra en Of. 43" (automático, usa SectorFisico) |
| 3 | Carpeta física encontrada y subida | Quien la encontró cambia el estado a mano a "Subida a Conaset" (sin estado intermedio) → pasa sola a esa bandeja |
| 4 | Carpeta física no existe | Se marca "No existe carpeta" → bandeja "No existe carpeta (F8)" |
| 5 | Se sube el F8 | "Subida con F8" |
| 6 | Se crea oficio/certificado | "Crear certificado / oficio" |

## Reglas
- **R6 Clasificación por fecha:** en Por clasificar, si Javiera ingresa fecha de última carpeta y no elige otro estado explícito, la acción por defecto es "Buscar carpeta física": el estado destino se calcula solo (Archivos / Of. 43) y el selector lo muestra antes de aplicar. Si elige otro estado explícito (1° licencia, cambio de domicilio, canje, etc.) se respeta. Sin fecha ni estado explícito → no permite aplicar.
- **R7 Fecha automática:** cada cambio de estado deja una fecha de etapa automática (hora de Chile) y la columna "Fecha de etapa" aparece en las bandejas; al pasar a un estado "Subida a…/Subida con…" se estampa FechaSubida si está vacía (ya existe: no duplicar lógica) y la fecha de cierre; nunca inventar fechas cuando el Excel no la trae.
- **R8 Transiciones permitidas** (máquina de estados, auditadas): PorClasificar → {PrimeraLicencia, SeEncuentraEnArchivos, SeEncuentraEnOf43, CambioDomicilioSolicitado, CambioDomicilio, NoExisteCarpeta, CrearCertificado, CanjeLicExtranjera, Subida*}; SeEncuentraEnArchivos/SeEncuentraEnOf43 → {SubidaConaset, NoExisteCarpeta}; NoExisteCarpeta → {SubidaConF8, CrearCertificado, SubidaConOficio, SeEncuentraEn*} (por si la carpeta aparece después; si aparece se sube "como un archivo más"). Verificar contra la máquina actual; solo agregar lo faltante, con tests.
- **R9 Cierre por subida (decisión pendiente, default A):** la carpeta se queda en su pestaña "Subida…" como fin del proceso; se anota sola la fecha de cierre y cuenta como cerrada en estadísticas. Alternativa B (pasar a Cerradas y eliminar pestañas Subida…) NO implementar sin confirmación del usuario. "Crear certificado / oficio" y "Subida con F8" cierran el proceso con la misma regla.
- **R10 Listados PDF para búsqueda:** en las pestañas "Se encuentra en Archivos" y "Se encuentra en Of. 43" botón "Generar listado para búsqueda" que arma el PDF con todas las carpetas del módulo (o solo las marcadas si hay selección), sin exigir marcar a mano; orden por fecha de última carpeta, con folio, fecha/hora de emisión (Chile), sede, total, espacio para marcar "encontrada / no existe". Título y folio según sector (ya corregido). Tras generarlo, ofrecer "marcar como impreso" (fecha de impresión auditada). Desde Por clasificar, tras clasificar a Archivos/Of. 43, ofrecer generar el listado de lo recién clasificado.
- **R11 Interacciones entre columnas en el listado:** cambiar Estado actualiza automáticamente Fecha de etapa, Sector (según fecha última carpeta), habilita/deshabilita Comuna (solo cambio de domicilio, ya existe), y mueve la carpeta de bandeja con contadores en vivo (ya existe); mostrar un toast "Pasó a <módulo>" con opción deshacer durante 10 s (deshacer = volver al estado anterior, auditado).

## Restricciones
- Mismas que fase 2: TDD, hora Chile, sin datos personales en logs, sin paginación, rendimiento, a11y, contraste ≥4.5 en 4 combinaciones de tema (incluyendo estados interactivos), una sola unidad de trabajo por operación.
- Solo escribe un constructor a la vez.
