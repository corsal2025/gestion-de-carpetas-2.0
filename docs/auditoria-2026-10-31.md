# Auditoría funcional, técnica y de facilidad de uso

Fecha: 31 de octubre de 2026, hora de Chile. Proyecto revisado: GESTION DE LICENCIAS 2.0, carpeta del Escritorio.

## Dictamen

El sistema tiene una base funcional amplia y las 2.639 pruebas automáticas existentes pasan. Las operaciones principales ensayadas en navegador funcionaron. Sin embargo, hay errores de estadísticas y riesgos de autorización y trazabilidad que deben resolverse antes de considerar validado su uso compartido. La facilidad de uso mejora si se hace explícito cuándo una edición mueve una carpeta y se reduce la cantidad de opciones que el usuario necesita interpretar.

Esta es una auditoría de código y una prueba práctica con datos ficticios; no equivale a una certificación de seguridad, una prueba de carga ni una validación con funcionarios reales. Los módulos inspeccionados sin datos no se consideran probados de extremo a extremo.

## Entorno y protección de información

- Código de trabajo actual, incluidos cambios locales existentes. No se revirtieron ni modificaron archivos de aplicación.
- .NET SDK 10.0.302; Blazor Server; EF Core; SQLite; Razor Pages para Peticiones.
- Instancia separada en http://localhost:5097, base nueva `temp/auditoria-20261031/auditoria.db`.
- Directorio de comunas y archivos de prueba separados. Respaldo automático desactivado en esta instancia; envío real de cambios de domicilio y certificados desactivado.
- No se importó la base real, no se enviaron correos, no se sincronizó el buzón y no se activó el envío real. No se leyeron contraseñas.
- Las capturas, el CSV sintético y los resultados HTTP están en `temp/auditoria-20261031/`.

## Cómo funciona el sistema

1. La agenda CSV/Excel o el registro manual crean carpetas de ciudadanos. El RUT identifica a la persona; una persona puede tener más de una carpeta.
2. La agenda entra en «Licencias para clasificar» o en una bandeja de revisión si presenta problemas. Las cancelaciones se cuentan aparte.
3. La clasificación usa clase de licencia, trámite, fecha de última carpeta y, cuando corresponde, comuna. Puede derivar a archivo físico, Of. 43, primera licencia, F8, cambio de domicilio u otras etapas.
4. Las solicitudes especiales gestionan F8 y certificados/oficios. Los cambios de domicilio tienen circuitos para carpetas que pedimos y carpetas que nos piden.
5. Otorgamiento registra la decisión final. Las carpetas cerradas pueden continuar hacia almacenamiento en cajas, según sus reglas.
6. El historial registra modificaciones; estadísticas agrega información; integridad revisa inconsistencias.

No hay un único estado que explique todo: el sistema combina estado, decisión, bandeja calculada, fechas, clasificación física/digital y datos de solicitudes. Conviene mostrar al usuario «Dónde está», «Qué falta» y «Qué puedo hacer ahora», evitando que deba conocer esa estructura interna.

## Pruebas ejecutadas

| Prueba | Resultado observado |
|---|---|
| `dotnet test Sgl.slnx --nologo --verbosity minimal` | 1.796 dominio + 751 aplicación + 92 Peticiones = 2.639 correctas; cero fallos y cero omitidas. |
| Crear base nueva y aplicar migraciones | Arranque correcto; vigilante inicial sin problemas. No acredita migración de una base real. |
| Omitir nombre de funcionario | Permitió continuar; modificaciones registradas como «Funcionario». |
| Registrar formulario vacío | Mensajes para RUT, nombre y apellido obligatorios. |
| Registrar persona ficticia con RUT válido y clase B | Guardó y abrió detalle con historial. |
| Cambiar de Citada a 1° Licencia | Guardó el estado y estampó Fecha de subida. Este último comportamiento requiere aclaración de negocio. |
| Encontrar la carpeta en 1° Licencia | Menú mostró contador 1 y la fila correspondiente. |
| Otorgar carpeta ficticia | Pasó a Cerradas; contador de Otorgamiento bajó y el de Cerradas subió. |
| Deshacer otorgamiento | Volvió a Para otorgar; contadores se restauraron. |
| Importar CSV con cuatro filas | Creó dos carpetas, rechazó un RUT inválido y guardó una cancelación sin crear carpeta para ella. |
| Reimportar el mismo CSV | Creó cero carpetas; reconoció dos existentes y una cancelación ya registrada. |
| Clasificar por fecha 2022-06-15 y clase B | Al abandonar el campo de fecha movió la fila a Archivos y ofreció listado de búsqueda. |
| Buscar asistencia por RUT sin puntos | Normalizó RUT, encontró la cita y permitió marcar Vino. |
| Marcar Vino | Contador pasó de 0 a 1; apareció registro y opción Deshacer. |
| Buscar globalmente por RUT completo | Encontró la persona en Archivos después de clasificarla. |
| Estadísticas con datos de las operaciones | Cargó; mostró cancelación y asistencia; se detectaron errores detallados más abajo. |
| Exportar estadísticas sin autenticación | HTTP 200; archivo XLSX; Cache-Control no-store. |
| Exportar con días=0 | HTTP 400. |
| Exportar con fechas invertidas | HTTP 400. |
| Exportar con Origin externo | HTTP 403. |
| Listado en 1366×768 y 714×668 | En escritorio se ve acción; en 714 px queda fuera del área visible de la tabla. |
| Revisar F8, certificados, cajas y cambios de domicilio | Pantallas cargaron; inspección de estados vacíos y ayudas. No se completaron sus circuitos documentales o de correo. |

Los tests existentes no detectaron los problemas de estadísticas observados en navegador. Pasar tests no demuestra por sí solo que toda la experiencia sea correcta.

## Hallazgos prioritarios

### A01 — Alta: autorización y autor del historial dependen de configuración permisiva

La configuración incluida desactiva autenticación y deja `Seguridad:Otorgadores` vacío. `PermisosPorConfiguracion.PuedeOtorgar()` devuelve true cuando esa lista está vacía. En la instancia de prueba se pudo otorgar después de elegir «Ahora no» en el ingreso de nombre.

Con lista no vacía, el permiso consulta `AutorState.Nombre`; sin autenticación ese nombre se ingresa manualmente. Por ello, llenar la lista sin activar identidad autenticada no ofrece una separación confiable de funciones. Además, el autor genérico impide atribuir cambios a personas concretas.

Evidencia: `src/Sgl.Web/appsettings.json`, `Services/PermisosPorConfiguracion.cs`, `Services/AutorState.cs`, `Components/Layout/MainLayout.razor`, `Program.cs`. La exportación sin iniciar sesión confirmó el acceso anónimo en la configuración auditada. No se comprobó cuál configuración usa un servidor instalado.

Recomendación: identidad institucional automática, permiso de otorgamiento basado en esa identidad y restricción de administración/correo por función. Mantener simple el trabajo del funcionario usando el usuario Windows, sin pedir nombres repetidamente. Verificar también el acceso a documentos y pantallazos bajo autenticación. Una validación de Origin no reemplaza el inicio de sesión.

### A02 — Alta: activar correo real carece de permiso específico

«Pedimos carpetas» ofrece «Activar envío real…». La implementación usa confirmaciones, pero `CorreoComunasService.EstablecerEnvioRealAsync` guarda el interruptor global en la base sin recibir identidad ni comprobar un permiso administrativo. El valor guardado tiene prioridad sobre `CambioDomicilio:EnvioReal` de configuración.

Evidencia: `Components/Pages/CambioDomicilio/PedimosPorPedir.razor` y `src/Sgl.Infrastructure/CambioDomicilio/CorreoComunasService.cs:153–190`. Inspección de código y del botón; no se activó ni se envió correo.

Recomendación: limitar el interruptor a administración autenticada, registrar quién y cuándo lo cambió y mantener un bloqueo de entorno de pruebas que no pueda anularse desde la pantalla de trabajo.

### A03 — Media: estadísticas agrupan movimientos por fecha UTC

Reproducción: efectuar operaciones el 31-10-2026 alrededor de las 22:31–22:33 en Chile y abrir Estadísticas. «Días con más cambios» mostró DOM 01-11-2026, mientras detalle e importaciones mostraron 31-10-2026.

Causa: `EstadisticasQuery.UsoAsync` agrupa `h.Fecha.Date` sin convertir a hora Chile. `Historial` también construye límites de filtros con medianoche sin convertirlos a UTC. Las consultas de importaciones tienen un patrón similar que debe revisarse.

Recomendación: definir límites de días locales y convertirlos a UTC al consultar; agrupar en fecha Chile. Agregar comprobaciones cerca de medianoche y cambios de horario.

### A04 — Media: el indicador de importadas no cuenta la agenda

Reproducción: importar el CSV suministrado. El resultado informó dos creadas y el historial de importaciones mostró esa carga; «Importadas desde Excel» informó cero.

Causa: `UsoAsync` cuenta únicamente `Campo == "Importacion"`; la creación desde agenda registra `Campo == "Agenda"` en `Carpeta.Agenda.cs`. El indicador tampoco explica la diferencia entre agenda CSV y planilla DETALLE.

Recomendación: definir un origen persistente común o incluir ambas fuentes; renombrar a «Importadas de archivo» y distinguir agenda/DETALLE solo en el detalle. Contar carpetas únicas, no asumir que cada evento de historial es una carpeta distinta.

### A05 — Media: editar datos puede mover inmediatamente la carpeta

Reproducción: en Por clasificar, seleccionar B e ingresar fecha de última carpeta 2022-06-15; al salir del campo la fila desapareció y pasó a Archivos. No hubo botón Aplicar ni vista previa persistente del destino.

Evidencia: `Listado.razor`, `FechaElegidaAsync` y `ClaseElegidaAsync`. Es un comportamiento implementado deliberadamente, pero constituye un riesgo de uso para quien interpreta que está completando datos.

Recomendación: mostrar «Destino: Archivos» y una acción visible «Enviar a Archivos». Para usuarios experimentados se puede evaluar un modo rápido explícito. Mantener Deshacer visible y consistente en cada movimiento.

### A06 — Media: seleccionar archivo lo importa inmediatamente

La pantalla muestra pasos «Elegir archivo → Procesar → Resultado», pero elegir el CSV inició el procesamiento y escribió datos sin una acción independiente. `OnArchivoSeleccionado` llama directamente al importador.

Recomendación: selección → revisión resumida de archivo/tipo/filas/errores → botón «Importar». Separar claramente cargar agenda de actualizar planilla DETALLE. Los resúmenes deben decir qué se creó, qué cambió y qué debe corregir la persona.

### A07 — Media: la acción de fila pierde visibilidad en pantalla angosta

A 714×668, «Ir a Otorgamiento» queda fuera de la vista a la derecha. La cabecera ocupa varias filas y el menú lateral se vuelve principalmente iconográfico. A 1366×768 la acción está visible.

Evidencia: `listado-714.jpg` y `listado-1366.jpg` en la carpeta de evidencias.

Recomendación: conservar nombre, RUT y acción principal visibles, llevar información secundaria al detalle y ofrecer un menú de acciones claramente rotulado. Verificar 1366 px con zoom 125% y 150%, habitual en equipos de oficina, además de móvil.

### A08 — Media, decisión de negocio: Primera Licencia cuenta como subida

Cambiar de Citada a 1° Licencia estampó FechaSubida; Estadísticas mostró una subida en vía «Otra» aunque en esta prueba nunca se efectuó una subida a Conaset. `Carpeta.EsEstadoSubida` incluye `PrimeraLicencia`.

Puede representar una regla operativa válida, pero el nombre del indicador induce a interpretarlo como una acción efectivamente realizada. No se clasifica como error de negocio confirmado sin validar esa definición con el departamento.

Recomendación: confirmar qué acredita «Subida» y separar preparación/primera licencia de una subida realizada si son eventos diferentes. No corregir fechas históricas automáticamente hasta resolverlo.

### A09 — Media: navegación con conceptos superpuestos

Hay ocho grupos y 34 destinos en el catálogo. Cambios de domicilio mezcla «Nos piden», «Pedimos», «Cambios de domicilio que nos solicitan por correo», «Cambio dom. solicitado» y cierres relacionados. F8 tiene «Creación de F8» y «Solicitudes de F8». La diferencia puede ser válida internamente, pero exige aprendizaje.

Recomendación: organizar la entrada por tareas: «Recibir y clasificar», «Buscar carpeta», «Solicitar antecedentes», «Resolver», «Guardar en caja». En cada pantalla mostrar una frase que explique qué casos llegan y cuál es el siguiente paso. Unificar nombres y evitar abreviaciones como etiquetas principales.

### A10 — Baja: documentación y ayuda requieren actualización

README describe una pantalla Inicio con tarjetas que ya no existe: `Home.razor` redirige a Por clasificar. También mantiene como pendiente una transición de Clase pendiente que otros documentos dan por resuelta. No deben usarse los conteos históricos de `docs/pendientes.md` como diagnóstico de los datos actuales.

Recomendación: actualizar el manual desde el comportamiento actual y validar la guía interactiva con tareas reales. El usuario debería poder completar las operaciones habituales sin consultar documentación técnica.

## Fortalezas observadas

- Validación de campos obligatorios y RUT.
- Reimportación de agenda sin duplicar los casos ya cargados.
- Separación de cancelaciones respecto de carpetas activas.
- Historial y Deshacer de otorgamiento con contadores coherentes en la prueba.
- Búsqueda global que encuentra carpetas después de moverlas.
- Avisos explícitos de modo prueba y de firmantes pendientes.
- Exportación con no-store, rechazo de origen externo y validación de filtros HTTP.
- Lógica de dominio y aplicación separada de las pantallas; amplia batería de pruebas.

## Plan de simplificación propuesto

1. Antes de uso compartido: identidad automática, permisos de otorgamiento/administración y control del envío real; resolver A03 y A04.
2. Siguiente mejora de uso: acciones explícitas de clasificar/importar, destino claro, confirmación de resultado y Deshacer consistente. Mantener la velocidad mediante valores sugeridos y teclado.
3. Reducir menús superpuestos y destacar una sola acción principal por fila. Las preferencias visuales pueden agruparse en «Apariencia».
4. Revisar tablas con zoom de oficina y pantallas angostas. Dejar ayudas extensas en segundo plano y mensajes cortos junto al campo relevante.
5. Pilotear con funcionarios y ajustar según errores observados, antes de añadir nuevas funciones.

## Pruebas pendientes para cerrar la validación integral

- Usuarios reales: 3–5 funcionarios con tareas de recepción, clasificación, búsqueda, resolución y archivo. Medir finalización sin ayuda, errores, clics innecesarios y dudas; objetivo propuesto ≥90% de éxito sin asistencia para tareas habituales.
- Duplicados: conservar, anular, restaurar y reagendar desde navegador, comprobando historial y recuperación.
- F8 completo, certificado/oficio completo, documento abierto en Word y validación de firmantes.
- Cajas: asignación individual y masiva, cierre, reapertura y acta; impresión/PDF real.
- Correo: pruebas con transporte de ensayo, nunca con destinatarios municipales reales; comprobar reintentos y doble envío. Sincronización contra un buzón de pruebas.
- Dos usuarios editando la misma carpeta, reconexión de Blazor, interrupción durante importación, bloqueo SQLite y recuperación. El control por circuito no demuestra por sí solo ausencia de conflictos entre usuarios.
- Rendimiento con más de 200 filas visibles y volúmenes representativos de datos; no inferirlo de esta base pequeña.
- Restauración efectiva de un respaldo y migración de una copia consistente de base real.
- Chrome y Edge de oficina, Firefox si se utiliza; teclado completo, lector de pantalla, contraste medido y zoom.
- Despliegue: autenticación Windows, restricciones de red, HTTPS y permisos de carpetas en el servidor real. No se auditó ese servidor.

## Criterio de cierre

Esta revisión entrega hallazgos reproducibles y un plan de trabajo. La validación integral queda pendiente de los escenarios anteriores y de acordar las definiciones de negocio. No se recomienda declarar el sistema completamente auditado o listo para producción únicamente por el resultado de las pruebas automáticas.
