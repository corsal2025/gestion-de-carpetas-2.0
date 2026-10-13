# Pendientes (ronda 6 del árbitro, modo liviano)

Sin fallos BLOQUEANTE ni ALTA. Decide tú cuáles atender.

| ID | Sev. | Qué pasa | Acción sugerida |
|---|---|---|---|
| ARB6-01 | MEDIA | Entre dos sesiones abiertas, el orden cacheado de carpetas puede quedar desactualizado (fila movida aún visible, o filas nuevas sin aparecer hasta recargar). | Corregir (se hace en esta ronda). |
| ARB6-02 | BAJA | La reparación de comunas no deja rastro en el historial. | Opcional: registrarla en ImportacionLogs. |
| ARB6-03 | BAJA | 247 carpetas de cambio de domicilio quedaron sin fecha de última carpeta tras la reparación, así el PDF por sector no las clasifica. 103 valores heredados no se reconocen. | Revisar a mano o con CAS Chile el martes. |
| ARB6-04 | BAJA | En pantallas de 1440 px el campo "Funcionario" del encabezado se corta. | Ajuste de ancho. |
| ARB6-05 | BAJA | Títulos de pestaña sin formato único. | Unificar formato. |
| Pendiente | — | 3 puntos sin probar en la UI: batch atómico ante fallo a mitad, conflicto al restaurar, "Mantener ambas" y persistencia de Lico. Están cubiertos por pruebas automáticas que pasan. | Probar a mano en una copia. |
