# Spec — Reorganización de módulos, clasificación sin duplicados y panel de inicio

Acordado con el usuario (protocolo Fase 1, 2026-10-12). Proyecto: "Gestión de Carpetas 2.0" (no confundir con "SGL", sistema del Estado).

> **Nota (2026-10-14):** el módulo «Posibles Duplicados» (`/duplicados`) que menciona este contrato fue reemplazado: los duplicados se muestran y resuelven dentro de «Por clasificar» (ver `docs/pendientes.md`). La barra superior ya no incluye «Cargar Excel» ni «Duplicados» (R4).

## Contexto de negocio
- Javiera no atiende público: revisa otros sistemas (CAS Chile, SGL del Estado) y clasifica en este sistema.
- La agenda diaria (Excel de Gilda / descargada de agendas) entra a "Por clasificar".
- Error más frecuente hoy: la misma persona registrada en distintas fechas para el mismo trámite/licencia.

## R1. Clase de licencia obligatoria al clasificar
- Al clasificar desde "Por clasificar" (individual y en lote) es obligatorio marcar al menos una clase (A1..A5, B, C, D, E, F). Sin clase la carpeta no sale de Por clasificar (validación en dominio + mensaje claro en UI).
- Carpetas antiguas (importadas antes de hoy) NO se modifican ni se exige clase; el control de duplicados por clase rige solo para carpetas con clase.

## R2. Alerta de duplicado al importar agenda y al clasificar
- Al importar agenda: si el RUT ya tiene una carpeta ABIERTA (decisión Pendiente, no anulada, no archivada), la nueva fila entra igual a "Por clasificar" pero marcada con alerta roja: "Ya tiene carpeta abierta del dd-mm-aaaa en <sede> (<estado>)". No se crea ni se resuelve automáticamente.
- En "Por clasificar", cada fila con alerta muestra tres acciones:
  1. **Reagendar**: no crea carpeta nueva; actualiza la FechaCitacion (y sede si difiere) de la carpeta existente con la de la agenda, registra en historial "Reagendada desde dd-mm a dd-mm", y anula la fila nueva de Por clasificar (motivo "Reagendamiento").
  2. **Crear nueva**: la fila sigue el flujo normal de clasificación (clase obligatoria). Si al clasificar coincide RUT + clase con una abierta → bloqueo (regla existente).
  3. **Anular duplicada**: anula (soft) la carpeta elegida (la nueva o la existente, Javiera elige cuál) con motivo "Duplicado", fecha y funcionario.
- Mismo RUT con distintas fechas detectado también en "Posibles Duplicados" con estas mismas tres acciones.

## R3. Anulación recuperable (soft delete)
- Nuevo estado/flag "Anulada" con motivo, fecha, funcionario (historial). Las anuladas desaparecen de todos los módulos, contadores, estadísticas y duplicados.
- Vista "Anuladas" (en Cierre) para consultarlas y botón "Restaurar" (vuelve al estado previo, auditado).
- Nunca borrado físico.

## R4. Navegación: Opción C
- **Inicio (/)**: panel de tarjetas, una por módulo, agrupadas (Entrada, Preparación, Cambio de domicilio, Subidas, Cierre). Cada tarjeta: nombre, contador, color de grupo, semáforo (verde al día / amarillo / rojo atrasos según umbrales), mini gráfico 7 días (línea simple sin ejes). Clic → abre el módulo.
- **Dentro de un módulo**: menú lateral fijo a la izquierda con grupos plegables y módulos con contador; plegable a franja de íconos (~56px) con botón; estado recordado en localStorage (try/catch). Reemplaza la barra de pastillas actual (eliminarla).
- El menú superior global (Gestión de Carpetas, Cajas, Cargar Excel, Duplicados, Estadísticas) se integra coherente con el lateral (decidir y documentar).
- La tabla ocupa todo el resto; mantener: RUT y nombre fijos, columnas centradas con separadores, lápiz de observación uniforme, sin paginación (Virtualize).

## R5. Panel didáctico (sin sobrecargar)
- A) Metas del día: barra "Hoy: X de Y clasificadas" en el inicio y barra delgada en Por clasificar; celebración sutil al 100%.
- B) Colores por grupo + semáforo en tarjetas y menú; números con animación suave al cambiar (respetar prefers-reduced-motion).
- C) Lico: un globo de mensaje contextual en el inicio (p. ej. "Tienes N duplicados por revisar", "¡Por clasificar al día!"), máximo uno, silenciable (recordado).
- D) Mini gráfico 7 días en cada tarjeta.
- Dentro de módulos solo la tabla (más barra de meta en Por clasificar).
- Claro y oscuro deben verse bien.

## Restricciones
- .NET 10 Blazor Server, EF Core SQLite, capas limpias, TDD (xUnit), sin datos personales en logs/salidas.
- Rendimiento con 25.5k carpetas: inicio < 2 s, cambio de módulo < 1 s.
- Hora de Chile (RelojChile/HoraChile ya existentes).
- No romper funcionalidades existentes (cajas, PDF por sector, importadores, estadísticas, duplicados, clasificación en lote).
