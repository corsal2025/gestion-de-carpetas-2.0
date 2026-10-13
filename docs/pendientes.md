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
