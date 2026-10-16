# Pendientes (cierre de fase 3)

Resuelto en esta fase: ARB6-01 (orden cacheado entre sesiones), ARB6-02 (reparación de comunas en el registro de importaciones), ARB6-04 (campo Funcionario fluido), ARB6-05 (títulos «<Módulo> · Gestión de Carpetas 2.0») y R6–R12 de `spec-fase3-interacciones.md`.

| ID | Sev. | Qué pasa | Acción sugerida |
|---|---|---|---|
| F3-01 | ALTA (datos) | El vigilante de integridad detecta 3.673 carpetas importadas con «fecha de subida anterior a la fecha de citación» (todas con citación en 2026, la mayoría en Subida a Conaset / con F8). Es un dato heredado: el aviso rojo de Inicio seguirá visible hasta corregirlas. | Revisar si la citación del Excel viene con día/mes invertidos o si la subida es correcta; decidir corrección masiva con Gilda. |
| ARB6-03 | BAJA | 247 carpetas de cambio de domicilio quedaron sin fecha de última carpeta tras la reparación, así el PDF por sector no las clasifica. 103 valores heredados no se reconocen. | Revisar a mano o con CAS Chile. |
| F3-02 | BAJA | R9 queda en la opción A (la carpeta se queda en su pestaña «Subida…»; sin mover a Cerradas). | Confirmar con el usuario si se prefiere B. |
| F3-03 | BAJA | «Fecha de etapa» de carpetas anteriores a esta fase es la del último cambio de estado auditado o, si no hubo, la de ingreso al sistema (no la fecha real del Excel). | Informativo. |
| F3-04 | BAJA | La verificación visual se hizo con Edge (CDP). Falta mirar en Chrome real de la oficina y con la impresión a PDF física. | Revisar el martes. |
| Pendiente | — | «Clase pendiente» no tiene transiciones de salida en el diagrama actual. | Confirmar flujo con el departamento. |

## Para retomar (2026-10-13, fin de jornada)
- **Preguntas abiertas con el usuario**
  - Columna «Fecha últ. carpeta» de «Por clasificar»: hoy es un selector de calendario; el usuario había dicho «eso no debe ir ahí» y no se definió si se saca (A), solo se quita el texto de ayuda (B) u otra cosa (C).
  - Cierre por subida (R9): se mantiene la opción A (la carpeta se queda en su pestaña «Subida…» con fecha de cierre automática).
  - 3.673 carpetas heredadas con fecha de subida anterior a la citación (vigilante de integridad; el aviso rojo se retiró de Inicio, la vista «Integridad» sigue en el menú).
  - Si la lupa de duplicado en «Por clasificar» debe quedar o salir.
- **Datos de CAS Chile** (el usuario los extrae por años): importar como tabla de consulta por RUT (última carpeta, lugar, clases).
- **Pendientes de revisión visual**: probar en Chrome real las bandejas grandes (> 200 filas); el navegador de pruebas no cargaba sus filas.
- **App**: corre como tarea de la sesión; para dejarla siempre disponible hay que instalarla como servicio de Windows.
- **Opus**: los agentes con modelo Opus fallaban con «modelo no encontrado»; la última auditoría fue con Sonnet (aprobación provisional).

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
