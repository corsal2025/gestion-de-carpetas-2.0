# Evaluación de uso y mejoras verificadas

Fecha de trabajo: 31 de octubre de 2026, hora de Chile.

## Evaluación honesta

La versión inicial es difícil para una persona con poca experiencia digital. El problema no es la edad por sí sola: exige conocer muchos términos internos, interpretar iconos, descubrir acciones ocultas y anticipar cambios que se guardaban automáticamente. Una persona mayor con experiencia administrativa puede entender el proceso, pero eso no elimina las barreras visuales y de interacción.

Tras estos cambios resulta más predecible registrar y clasificar información, y la planilla es más legible. Aun así, la dificultad global sigue siendo media: hay muchos módulos y estados que requieren capacitación. No se ha demostrado que cualquier persona pueda usarlo sin ayuda. No se realizaron sesiones con participantes mayores de 60 años ni mediciones de tiempos de aprendizaje.

## Datos y entorno utilizados

Se utilizaron los dos archivos que el usuario autorizó de Descargas:

- `citas_20261030_092945.csv`: 550 filas; 502 carpetas creadas; 48 canceladas guardadas.
- `citas_20261030_155709.csv`: 585 filas; 35 carpetas nuevas; 497 existentes; 53 canceladas, de las que 48 ya estaban guardadas y 5 eran nuevas. Se informaron tres alertas de duplicado y cinco carpetas existentes que llegaron canceladas.

Resultado conjunto: 537 carpetas y 53 cancelaciones únicas en la base de pruebas. Esas carpetas pueden aparecer en distintas bandejas de revisión; no deben confundirse con el contador de Licencias para clasificar.

Se reimportó el segundo archivo después de probar cancelar y volver a seleccionar el mismo archivo: cero carpetas nuevas, 532 existentes, cero cancelaciones nuevas y 53 ya registradas.

Los archivos originales no se modificaron. La base de trabajo es `temp/usabilidad-20261031/prueba.db`, servida en `http://localhost:5098`. Se hicieron movimientos simulados sobre esa copia. No se enviaron correos, no se sincronizó el buzón y no se activó correo real. No se modificó la base de la instancia habitual. El proyecto conserva cambios locales que ya existían.

## Cambios implementados

| Obstáculo | Cambio final |
|---|---|
| Elegir archivo comenzaba a guardar inmediatamente | Selección separada de «Importar archivo», con nombre del archivo y «Cancelar selección». El selector queda desactivado durante la carga. Cancelar permite elegir nuevamente el mismo archivo. |
| Seleccionar fecha, clase o destino podía sacar la fila de la bandeja | Estos campos preparan la clasificación. Solo el botón «Guardar» mueve la carpeta. Se conserva la validación de campos y de comuna cuando corresponde. |
| Letra y controles pequeños | Controles principales de 16 px y altura mínima de 44 px; foco de teclado visible; nombres largos pueden ocupar dos líneas. No es una certificación de accesibilidad completa. |
| Tabla presentada como bloques grandes | Se mantiene el formato de planilla solicitado: filas y columnas, celdas rectangulares, bordes continuos, selector sencillo y Guardar en la misma línea. |
| Parte de la tabla queda fuera del área de trabajo | Control compartido «Maximizar tabla». La salida queda en una barra fuera de la tabla, en la parte inferior, y también funciona Esc. No se mueve ni se reconstruye el contenido de la tabla. |
| Menú y cabecera compiten por espacio | Preferencias visuales agrupadas en «Apariencia». En pantallas pequeñas hay «Cambiar sección» con nombres escritos, en lugar de depender solo de iconos. |
| Difícil reconocer la pantalla actual | Título de bandeja visible y ayuda breve sobre cómo clasificar. |
| Orden del menú solicitado por el usuario | Revisión y corrección de errores queda inmediatamente antes de Cierre. |

La maximización se incorpora también a tablas que aparecen después de navegar. No agrega controles a los documentos independientes de impresión. Los controles de ampliación se ocultan al imprimir; no se verificó una impresora física.

## Pruebas de interacción realizadas

- Seleccionar el primer archivo no creó carpetas antes de confirmar: la base continuaba con cero carpetas.
- Carga de ambos archivos reales y revisión de sus resultados agregados.
- Cancelar selección, volver a elegir el mismo archivo y reimportar sin duplicación.
- Seleccionar B y C en una carpeta: la fila permaneció en la bandeja hasta guardar; después se movió correctamente.
- Elegir Creación de F8 y clase B en otra carpeta: no hubo movimiento al preparar los datos. Guardar redujo la bandeja de 161 a 160 y creó una solicitud F8.
- Marcar F8 subido: la solicitud salió de Solicitado y apareció en el contador Subido. Esta acción no demuestra que se haya cargado un documento real en un sistema externo.
- Maximizar la tabla de carpetas, mantenerla ampliada mientras se guarda una fila y restaurar con Esc.
- Maximizar una tabla de Estadísticas y restaurarla.
- Estadísticas tenía 13 tablas y controles asociados; dos pertenecían a secciones plegadas, por lo que solo 11 botones eran visibles inicialmente.
- Verificar en 1366×900 que la tabla ampliada cabe en el ancho disponible: tabla 1335 px y contenedor 1335 px, manteniendo visibles las siete columnas de clasificación.
- Revisión del tema oscuro; se corrigió el botón Guardar para evitar texto blanco sobre un acento demasiado claro.

Compilación final sin advertencias ni errores. La batería automática ejecutada durante esta revisión pasó: 1.796 pruebas de dominio, 759 de aplicación y 92 de Peticiones, total 2.647. Las comprobaciones de interfaz anteriores son adicionales; no se infiere cobertura completa de las pantallas a partir de esas pruebas.

## Dificultades que siguen existiendo

1. **Terminología y cantidad de destinos.** Primera licencia, creación de F8, solicitudes de F8, subida, otorgamiento y cierre necesitan definiciones claras. No se renombraron reglas de negocio sin validarlas.
2. **Tablas amplias en pantallas pequeñas.** Maximizar ayuda, pero una planilla con muchas columnas sigue requiriendo desplazamiento horizontal en espacios reducidos. Mantener el formato Excel tiene ese costo; no se redujo la letra para ocultarlo.
3. **Correcciones de nombre y RUT.** Todavía existen celdas que se editan al hacer clic y se guardan al salir del campo. Esto debe evaluarse con funcionarios antes de decidir entre edición directa y un formulario separado.
4. **Confirmaciones y recuperación desiguales.** Otorgamiento dispone de Deshacer; no todas las acciones de todos los módulos tienen la misma recuperación. El F8 subido se aplica con un botón directo.
5. **Información de importación extensa.** El resumen contiene varios contadores útiles, pero requiere distinguir filas, carpetas, cancelaciones y duplicados. Una presentación más guiada necesita comprobar qué información utiliza realmente la oficina.
6. **Permisos y estadísticas.** Los hallazgos técnicos de la auditoría inicial son un trabajo separado. Estos cambios de interfaz no acreditan que autenticación, permisos, fechas agregadas o conteo de importadas estén resueltos.

## Validación todavía necesaria

- Sesiones con 3–5 funcionarios, incluidos participantes mayores de 60 años con distinta experiencia digital. Pedirles que completen tareas sin indicaciones y registrar dónde se detienen.
- Circuitos completos de certificado/oficio y cajas, documentos abiertos e impresión física.
- Correo y sincronización contra un entorno de ensayo, sin destinatarios reales.
- Trabajo simultáneo de varios funcionarios, interrupciones y restauración de respaldos.
- Teclado y lector de pantalla en todas las pantallas, Chrome/Edge de oficina y zoom 125%/150%.

Conclusión: hay mejoras concretas y verificadas en lectura, confirmación y espacio de trabajo. El sistema todavía requiere familiaridad con el proceso. La facilidad de uso para personas mayores no queda validada hasta observarlas utilizarlo.
