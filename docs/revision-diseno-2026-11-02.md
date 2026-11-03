# Revisión visual y funcional del sistema

Revisión iniciada el 2 de noviembre y verificación final del 3 de noviembre de 2026. Aplicada localmente para revisión del usuario; este rediseño aún no se publicó en GitHub.

La dirección elegida conserva el menú lateral, Lico, el logo municipal, las paletas y los colores de estado. La mejora se concentra en jerarquía, lectura y consistencia de las acciones.

Referencia posterior del usuario para tablas: encabezado azul `#26388c`, franja cian de 4 px, marco azul redondeado, rejilla fina, filas alternadas y acciones delineadas. Aplicada a la presentación compartida, conservando los colores semánticos y los registros individuales (sin inventar agrupaciones). Buscar, cargar Excel, nueva carpeta y ampliar tabla se reúnen en la misma barra de clasificación.

Ajuste del 3 de noviembre: se elimina la columna Trámite de clasificación, cola de cajas y acta impresa, y se redistribuyen los anchos. El encabezado azul y la franja cian continúan por el canal del scroll hasta el marco derecho. ResizeObserver mide la altura del encabezado y el grosor efectivo de su borde inferior, que puede ser fraccionario al cambiar el zoom; esos mismos valores posicionan la franja del canal. También se actualizan al redimensionar. La comprobación inicial en navegador abarcó vista normal y ampliada (entonces cinco columnas; posteriormente se separó nuevamente el RUT y se retiró Guardar). La corrección final de la franja se comprobó con análisis de sintaxis y entrega HTTP de CSS/JS actualizados; su acabado visual queda pendiente de revisar en el navegador.

Cobertura general: el registro de encabezados ahora recorre `table > thead` en todo el documento y es independiente del botón Ampliar. Incluye tablas independientes, estadísticas, historiales, ventanas de detalle y Peticiones. Reconoce también contenedores adicionales que contienen únicamente una tabla con scroll y les aplica el mismo marco. La medición se recalcula con cambios de página, tamaño y apariencia, y se libera al retirar tablas. Una comprobación ejecutable cubrió ocho tipos de tabla, el grosor fraccionario y la limpieza de observadores; esto verifica la cobertura del código, no sustituye una revisión visual de cada pantalla.

Verificación visual posterior: se reprodujo el escalón a escala fraccionaria en Chromium aislado. La corrección final dibuja una sola franja decorativa, sin eventos ni contenido accesible, que abarca las celdas y el canal del scroll; evita dos bordes rasterizados por separado. Usa medidas de layout, se posiciona de nuevo con scroll y redimensionado, respeta ventanas y vista ampliada y se oculta al imprimir. Pasaron 21 comparaciones de píxeles (densidades 1, 1.25 y 2; escalas 100 %, 110 % y 125 %; scroll y ampliación). Se comprobó la cobertura de encabezados en clasificación (1), integridad (1), estadísticas (8), Peticiones (1) y Comunas de Peticiones (1). F8 y Certificados estaban vacíos: no se verificaron filas en esas dos pantallas. Captura sin datos personales: `temp/revision-tablas/encabezado-corregido.png`.

Pruebas posteriores de F8 y Certificados con datos: se creó una base independiente con 125 carpetas ficticias, 62 solicitudes F8 y 63 certificados iniciales. Los recorridos de creación dejaron 66 certificados. El entorno de prueba escucha en `http://127.0.0.1:56555`, separado del sistema habitual en el puerto 5030, con correo desactivado. Pasaron 102 pruebas de servicios/documentos/correo, 38 comprobaciones de interfaz y seis comparaciones de píxeles de la franja en ambos módulos cargados. Se probaron búsqueda, paginación, filtros, edición y Escape, F8 subido, derivación a certificado, respaldos mensuales, años inválidos/válidos, firma bloqueada si faltan datos, firma y subida separadas, creación manual de Registro Civil y Oficio, rechazo de duplicados y cinco descargas Word con contenido completo. La auditoría confirmó los estados persistidos y cero correos registrados como enviados.

Los datos cargados permitieron corregir nombres partidos en palabras muy cortas, acciones excesivamente altas, paginación fuera del área visible, altura insuficiente en vista ampliada y pérdida de las columnas de identidad durante el scroll horizontal. Se revisaron escritorio, móvil de 390 px y modo oscuro. Capturas finales y resultados reproducibles de esta ejecución están en `temp/pruebas-solicitudes-20261103/`; los datos ficticios no se mezclaron con la base real ni se incorporan a Git. El envío real de correo permanece fuera de estas pruebas.

## Hallazgos y cambios

| Problema observado | Cambio aplicado | Resultado buscado |
|---|---|---|
| Nombres y controles centrados sin una jerarquía clara | Textos a la izquierda, nombre con mayor peso y RUT secundario con cifras tabulares | Reconocer a la persona antes de interpretar las acciones |
| Siete columnas ocultaban Guardar en escritorios medianos | Nombre y RUT agrupados, ambos editables, solo en clasificación | Mantener destino y acción a la vista con el lateral abierto |
| Botones grandes, bordes de edición y formas inconsistentes | Alturas y radios uniformes, iconos secundarios discretos y foco visible | Reducir ruido sin eliminar funciones |
| Rejilla densa y filas que se desplazaban al pasar el ratón | Separadores horizontales, menos bordes y posiciones estables | Seguir una fila sin que cambie bajo el cursor |
| Instrucciones extensas sin una secuencia visible | Tres pasos: fecha si existe, clase y revisión del destino | Comprender qué falta antes de guardar |
| Vista ampliada sin título y botón de retorno suelto | Cabecera fija con módulo, salida y mensajes de trabajo | Conservar el contexto y las acciones de recuperación |
| Selector de clase recortado por el scroll | Panel en capa Popover, anclado y ajustado al espacio disponible | Elegir clases sin recortes ni desplazamientos de la tabla |
| Escape podía cerrar selector y ampliación a la vez | Cierre por capas, con devolución del foco | Mantener el punto de trabajo al usar teclado |
| Campos de filas con etiquetas genéricas | Etiquetas por persona, mensajes asociados y estado inválido explícito | Localizar y corregir el campo que falta |
| Formularios, solicitudes y estadísticas tenían escalas distintas | Tipografía, controles, tarjetas, avisos y separación compartidos | Reconocer una misma aplicación en todos los módulos |
| Lico se ocultaba en encabezados y estados vacíos | Se recupera su presencia con una escala contenida | Conservar la identidad solicitada |

## Alcance

La capa compartida se carga tanto en Blazor como en Peticiones. Abarca clasificación, listados, F8, certificados, cajas, estadísticas, formularios, cambios de domicilio, asistencia, firmantes e integridad. Los colores Excel, las paletas, los estados y las reglas administrativas se conservan.

La edición del nombre y del RUT sigue disponible. Los cambios visuales no clasifican, anulan ni corrigen carpetas automáticamente. Las pruebas interactivas se realizan sobre una base ficticia separada.

## Verificación

- Navegador integrado: revisión de clasificación, F8 y Estadísticas, además de carga de Nueva carpeta, Importar, Certificados, Nos piden, Cajas, Asistencia, Firmantes, Integridad y Peticiones.
- Escritorio de 1366 píxeles y móvil de 390 píxeles: sin desbordamiento horizontal de la página en las pantallas revisadas. Las tablas densas conservan su desplazamiento interno.
- Tema oscuro: comprobación visual de superficies, textos, indicadores y controles.
- Paletas: las seis opciones cambian correctamente el color de acción; se conserva el selector existente.
- Teclado: Escape cierra el selector de clases o el diálogo antes de restaurar la tabla ampliada.
- Edición: cambiar el RUT y pulsar Escape conserva el RUT original y mantiene la vista ampliada. Tab desde la última acción vuelve a la cabecera aunque esté abierto el selector.
- Selector de clases: panel de 300 píxeles sin desbordamiento interno, por encima del contenedor desplazable.
- Validación: el intento de guardar incompleto conserva los valores y muestra la explicación junto a la fila.
- JavaScript: comprobación de sintaxis de los archivos modificados; compilación Release correcta y 2.779 pruebas aprobadas (dominio 1.838, aplicación 848, Peticiones 93).
- Verificación final sobre localhost: clasificación carga con seis columnas, sin pantalla de error; menú lateral con nombres accesibles también al plegarse. Base de datos y correos reales sin cambios por el rediseño.

### Comprobaciones adicionales del 3 de noviembre

- Franja celeste: 21 comparaciones de píxeles con distintas escalas y densidades, desplazamiento vertical y vista ampliada. La franja se dibuja de forma continua, incluido el espacio de la barra de desplazamiento.
- Cobertura de encabezados: clasificación, integridad, ocho tablas de estadísticas, Peticiones y Comunas.
- F8 y certificados: 102 pruebas de servicios y documentos y 38 comprobaciones de navegador con datos ficticios, incluidos filtros, paginación, edición, estados, firma, carga y descarga de cinco documentos Word. Auditoría sin envíos de correo.
- Tablas de F8 y certificados con datos: seis comparaciones adicionales de la franja, revisión móvil y tema oscuro.
- La clasificación se realiza al seleccionar explícitamente **Mover a**, cuando se completan los datos requeridos; se eliminó el botón Guardar de esta operación.

Para revertir el rediseño, retirar los cambios de `visual.css`, `tablas.js`, `app.js`, `modulos.css`, `ModulosSidebar.razor`, `Cajas.razor`, `Listado.razor` y `ClaseRapida.razor`. Conservar las reglas `.entrada-agendas-*` si sigue instalada la recepción de jefatura. No requiere modificar los datos.

No equivale a una certificación de accesibilidad ni a una prueba física de impresión. La validación final con funcionarios y sus navegadores sigue siendo necesaria para decidir ajustes de densidad o tamaño de letra.
