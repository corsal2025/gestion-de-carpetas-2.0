# Revisión visual de la plataforma

Revisión iniciada el 31 de octubre y cerrada el 1 de noviembre de 2026. Entorno local de pruebas: http://localhost:5098. Base independiente de los datos de trabajo.

## Resultado

Se unificó la presentación de las pantallas Blazor y del módulo Peticiones: superficies, tipografía, formularios, botones, tablas, navegación y ventanas. Se sustituyeron ocho iconos abstractos del menú por ilustraciones vectoriales de objetos reconocibles. Lico conserva su identidad de licencia de conducir y sus cuatro gestos, con materiales más suaves, volumen, manos y zapatos redibujados, y una expresión más serena.

Los botones principales tienen un acabado azul uniforme, bordes suaves, sombra discreta y estados de interacción y de teclado. El modo oscuro utiliza texto oscuro sobre los botones claros. Se eliminaron franjas decorativas de las ventanas y se mejoró la legibilidad del contador de observaciones.

## Problemas visuales corregidos durante la comprobación

- F8 y certificados heredaban tamaños horizontales al apilar su cabecera en el móvil: aparecían espacios vacíos de cientos de píxeles y un buscador demasiado alto.
- El buscador global se estrechaba y cortaba su texto en el móvil.
- Las tablas de configuración mostraban texto claro sobre fondo blanco en el modo oscuro.
- El lápiz de observaciones era casi invisible en el modo oscuro.
- Las ventanas podían quedar detrás de una tabla maximizada.
- La columna de nombres de «Todas las carpetas» se comprimía excesivamente. Ahora tiene un ancho mínimo de 256 píxeles y conserva el desplazamiento horizontal de la planilla.
- El panel lateral conservaba tarjetas redondeadas sin espacio interior y títulos en mayúsculas. Ahora muestra secciones separadas, etiquetas legibles y campos alineados; en el móvil usa una columna.

## Cobertura real

Se recorrieron y capturaron 46 rutas o vistas en escritorio: 23 bandejas, 20 pantallas principales y tres vistas adicionales de detalle y comunas. Se comprobaron también 20 pantallas principales a 390 × 844 y 25 vistas en modo oscuro. Las correcciones detectadas se volvieron a comprobar en sus pantallas afectadas. Se probaron la apertura y cancelación de observaciones, el selector de clases, la ampliación y restauración de tabla, una ventana sobre la tabla ampliada, el panel lateral y la guía con Lico.

Pantallas principales: nueva carpeta, importación, asistencia, F8, certificados, firmantes, cajas, estadísticas, integridad, anuladas, otorgamiento, nos piden, pedimos, pantallazos, seguimiento, comunas, historial de Peticiones, configuración de correo y sus estadísticas y directorio.

Las bandejas sin registros permitieron revisar su estado vacío; esto no verifica cómo se comportarían con registros en todos los estados posibles. Las capturas y el examen visual no equivalen a una certificación de accesibilidad ni a pruebas con personas mayores reales. La revisión de uso para mayores de 60 años sigue siendo una evaluación experta, no un estudio con participantes.

## Validación y límites

La solución compila correctamente. Los ocho iconos y la lámina de Lico contienen SVG válido. Se comprobaron los cuatro gestos de Lico y que su dibujo omite las animaciones cuando está activada la preferencia de movimiento reducido.

Esta pasada cambia presentación y etiquetas. No acredita que los errores de funcionamiento estén resueltos. La revisión de reglas, permisos, envíos, transiciones e importaciones debe continuar por separado. No se enviaron correos ni se activaron envíos reales para comprobar el diseño. La base usada aquí es una copia de pruebas; las pantallas antiguas de configuración pueden mostrar opciones distintas de las de los flujos actuales.

## Evidencia

Capturas en `temp/revision-visual-20261031/`:

- `asistencia-final.jpg`: botones y menú actuales.
- `lico-guia-final.jpg`: Lico y la guía.
- `detalle-lateral-final.jpg`: secciones y campos del panel lateral.
- `tabla-ampliada-modal.jpg`: ventana de observaciones sobre una tabla ampliada.
- `oscuro-correo-config.jpg`: contraste corregido en configuración.
- `lico-cuatro-gestos.svg`: lámina vectorial de los cuatro gestos.

Los archivos `escritorio-*`, `movil-*` y `oscuro-*` documentan el recorrido. Algunas capturas son previas al ajuste final de su pantalla; las terminadas en `final` corresponden a las vistas finales señaladas arriba.
