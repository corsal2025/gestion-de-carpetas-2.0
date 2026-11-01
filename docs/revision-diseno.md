# Revisión de diseño — Gestión de Carpetas 2.0

Revisión hecha sobre la aplicación en ejecución (base de datos copiada, sin tocar la real),
capturando pantallas en Chrome a 1920, 1440, 1280, 1024, 768 y 390 px, en tema claro y oscuro.

Capturas sin datos personales en `docs/revision-diseno/`.

---

## 1. Lo que está bien (no tocar)

- **Identidad propia y coherente**: Lico, la paleta institucional y la barra flotante dan un
  producto reconocible, no una plantilla de Bootstrap.
- **La guía interactiva** (7 pasos, foco sobre el elemento real, barra de progreso) está muy por
  encima de lo habitual en software municipal. Es el mejor activo de interfaz del sistema.
- **El estado vacío** de Por clasificar (mascota + mensaje + botón «Cargar agenda del día») es
  exactamente lo que hay que hacer: el usuario nuevo nunca se queda mirando una pantalla en blanco.
- **Semáforo por módulo** y contadores en el menú lateral: el operador ve la carga sin entrar.
- **Tema claro/oscuro y 6 paletas** ya implementados y persistentes.

---

## 2. Hallazgos, por prioridad

### P0 — Rompen el uso diario

#### 2.1 La barra superior cambia de alto entre pantallas (salto de 52 px)

Medido a 1440 px:

| Pantalla | Alto de `.topbar` | Buscador en la barra |
|---|---|---|
| `/` (Inicio) | **70 px** | no |
| `/carpetas?bandeja=…` | **70 px** | no |
| `/estadisticas` | **122 px** | sí |
| `/cajas` | **122 px** | sí |
| `/certificados` | **122 px** | sí |

Cuando aparece el buscador global, la barra se parte en dos filas y **todo el contenido baja 52 px**.
Navegar entre módulos hace saltar la página. A 1280 px se parte en **todas** las pantallas.

#### 2.2 La tabla del listado no cabe y esconde la acción principal

A 1440 px, con nombres largos reales:

- Tabla: **1138 px** · Contenedor: **1070 px** → **68 px ocultos**
- El botón de la última columna («Solucionar» / «Mover a…») queda **cortado a media palabra**.

El desbordamiento está contenido (`overflow-x: auto`), así que hay barra horizontal dentro de la
tabla, pero la acción principal no se ve sin desplazarse. La página en sí **no** desborda
(`scrollWidth == innerWidth` en todos los anchos), el problema es interno.

#### 2.3 El modo oscuro no está resuelto en las tablas

En tema oscuro, las filas con color de estado siguen siendo claras (rosa pálido) sobre fondo
oscuro: la zona más grande de la pantalla es la más brillante. Además:

- Contadores rojos del menú («563», «2.799»): texto rojo sobre píldora roja oscura → contraste muy bajo.
- Los `<input type="checkbox">` nativos se pintan negros sobre fila oscura (invisibles).
- Falta `color-scheme: dark`, así que los controles nativos no siguen el tema.

Ver `docs/revision-diseno/listado-oscuro.png`.

#### 2.4 En móvil (390 px) es inutilizable

- Barra superior: **182 px de alto** (logo / botón guía / navegación en 3 filas).
- La tabla mantiene su ancho de escritorio (1138 px) y se corta contra el borde.
- «Estadísticas», «TODAS», «Nueva carpeta» quedan cortados fuera de pantalla.

Ver `docs/revision-diseno/movil-barra.png`.

> Si el uso es solo en PC de escritorio, esto es opcional. Pero conviene decidirlo explícitamente
> y, si es el caso, **bloquear** el diseño móvil en vez de dejarlo a medio camino.

---

### P1 — El sistema de diseño no está formalizado

Medido sobre los 9 archivos CSS de `wwwroot`:

| Métrica | Valor | Comentario |
|---|---|---|
| Líneas de CSS | **11.265** | 482,7 KB |
| Declaraciones `!important` | **1.381** | la cascada no se controla, se fuerza |
| Hacks `:not(#sgl)` | **246** | especificidad artificial para ganar a hojas antiguas |
| Tamaños de letra distintos | **64** | deberían ser 6–8 |
| Radios de borde distintos | **48** | deberían ser 3–4 |
| Colores hex distintos | **382** | + 147 en `rgb()/rgba()` |
| Iconos SVG vs emojis en `.razor` | **38 vs 103** | el mismo icono aparece de las dos formas |

Consecuencias prácticas:

- Cambiar un color o un tamaño obliga a buscar y reemplazar en 9 archivos.
- Añadir una paleta = editar `theme.css` + `app.js` + `App.razor` + `MainLayout.razor` + `_Layout.cshtml`.
- La barra superior y el menú están **duplicados** en `MainLayout.razor` (Blazor) y en
  `_Layout.cshtml` (Peticiones): cualquier cambio hay que hacerlo dos veces.

---

### P2 — Jerarquía, flujo y microcopy

#### 2.5 Dos buscadores en la misma pantalla
En Certificado/Oficio hay un «Buscar RUT o nombre» en la barra superior **y** otro en el encabezado
de la página, con botones distintos.

#### 2.6 La acción principal del detalle no se lee como acción
En `docs/revision-diseno/detalle.png`:

- Los estados destino («SUBIDA A CONASET», «CREACIÓN DE F8», «CERTIFICADO / OFICIO») son botones
  que **parecen campos de texto deshabilitados**.
- «Cambiar estado» está en gris (deshabilitado) sin decir por qué: hay que adivinar que primero
  se elige un destino.

#### 2.7 Barra de herramientas sobrecargada (Peticiones)
13 controles en 3 filas sin agrupación ni jerarquía: cargar, actualizar, respaldar, enviar,
marcar, correo, reenviar, ver como subidas, borrar selección, petición manual, imprimir,
imprimir marcadas, borrar todo.

#### 2.8 El semáforo pierde significado
Casi todos los grupos del menú lateral aparecen en **rojo**. Si todo es urgente, nada lo es.
Además cada uno de los ~30 módulos tiene un color de círculo propio que no aporta información
(no hay leyenda ni patrón reconocible).

#### 2.9 Jerarquía tipográfica plana
Las tarjetas de Certificados muestran 8 campos con la misma etiqueta en versalitas y el mismo
valor: SEDE / TIPO / ORIGEN / SOLICITADO POR / N° OFICIO / AÑO RENOVACIÓN / ESTADO / FECHA.
Todo pesa igual, así que no se puede escanear.

#### 2.10 La tabla se alinea distinto según la bandeja
En «Se encuentra en Archivos» los nombres van alineados a la izquierda; en «Duplicadas» al
centro/derecha. Los encabezados están centrados mientras el contenido no lo está.

#### 2.11 Formatos de fecha y números inconsistentes
- `13/10/2026` (tabla) · `13-10-2026` (ficha) · `dd/mm/aaaa` (formulario)
- **`10/12/0002`** aparece en producción en «Fecha últ. carpeta» (año 2 d.C.).
- `25,517` con coma de miles (formato inglés) en vez de `25.517`.
- **«1 días»** y **«1 carpeta(s)»**: falta pluralización.

#### 2.12 El modal de nombre interrumpe y luego no se exige
Al entrar pregunta «¿Quién está usando el sistema?» de forma modal (bloquea la pantalla). Pero
`AutorState.HasNombre` devuelve siempre `true`, y varios formularios muestran el error «Ingrese su
nombre…» que en la práctica nunca puede ocurrir. O se pide en la barra de forma no modal, o se
exige de verdad.

---

### P3 — Pulido visual

- **Manchas de color** en las tarjetas KPI (degradados difuminados): en claro parecen suciedad y
  reducen la legibilidad del número. En oscuro se ven como borrones grises/cian.
- **Lico aparece dos veces por pantalla**: en la barra («Cómo usar el sistema») y otra vez en el
  encabezado de la página.
- La **«carretera» amarilla** (`--lico-road`, guiones de 22 px) como borde superior se lee como
  cinta de peligro/obra, no como decoración.
- **Estados vacíos inconsistentes**: Por clasificar tiene el estado vacío con Lico; el resto de
  bandejas dejan una tabla en blanco con solo los encabezados.
- **CTAs de igual peso**: «Nueva carpeta» y «Listado para búsqueda» son ambos azules primarios.
- Los `<select>` y `<input>` dentro de las filas de Certificados permiten **editar en una vista de
  lista**, con riesgo de cambios accidentales.

---

## 3. Plan de mejora sugerido

### Fase 1 — Arreglar lo que rompe (1–2 días)

1. **Barra superior de altura fija.**
   - Un solo componente de barra (partial/razor compartido) para Blazor y Razor Pages.
   - Grid de 3 zonas: marca · navegación · herramientas, con `flex-wrap: nowrap` y `min-width: 0`.
   - Mover el buscador global a un **icono de lupa** que abre un panel, o a la fila de herramientas
     de cada página. Deja de robar ancho a la barra.

2. **Tabla del listado.**
   - Columna de acciones `position: sticky; right: 0` con fondo y sombra → siempre visible.
   - `table-layout: fixed` + anchos por columna; que SEDE y «EN PROCESO DE BÚSQUEDA» se puedan
     colapsar en la vista compacta.
   - Anunciar el desplazamiento con una sombra en el borde.

3. **Modo oscuro de las tablas.**
   - Tokens por tema: `--fila-peligro-bg`, `--fila-ok-bg`, `--fila-aviso-bg` con alfa bajo
     (`rgb(... / 0.14)`) sobre la superficie oscura, en vez de colores pastel sólidos.
   - `color-scheme: dark` en `html[data-theme="dark"]`.
   - Revisar los contadores del menú: fondo neutro con punto de color, no píldora roja.

4. **Móvil: decidir.** Si se quiere soportar: tabla → tarjetas bajo 768 px y barra → 2 filas
   máximo. Si no, fijar `min-width` y mostrar un aviso de «mejor en escritorio».

### Fase 2 — Formalizar el sistema de diseño (3–5 días)

5. **Tokens en un solo sitio** (`tokens.css` generado o `:root` único):

   ```
   Tipografía:  6 tamaños (12, 13, 14, 16, 20, 28 px) con 2 pesos por nivel
   Radios:      3 valores (6, 10, 999 px)
   Espaciado:   escala de 4 px (4, 8, 12, 16, 24, 32)
   Color:       superficies (3), texto (3), bordes (2), estados (5), acento por paleta
   ```

6. **Retirar `!important`**. Ordenar la cascada de verdad: base → componentes → utilidades.
   Los 246 `:not(#sgl)` y los 1.381 `!important` son deuda que crece sola.

7. **Una sola iconografía.** Sustituir los 103 emojis por SVG del mismo set que la barra
   (trazo 2 px, 24×24). Emojis cambian según el sistema operativo y rompen la identidad.

8. **Una sola definición de paleta.** `palettes.json` → genera `theme.css` y el arreglo de
   `app.js`; los botones se renderizan desde el mismo dato.

### Fase 3 — Mejorar la operación (2–3 días)

9. **Detalle**: los estados destino pasan a tarjetas seleccionables (radio visible) con el CTA
   «Cambiar a Subida a Conaset» activo solo al elegir. Un solo buscador por pantalla.
10. **Peticiones**: agrupar en «Enviar ▾» / «Más acciones ▾», dejando 2 botones primarios.
11. **Semáforo útil**: un indicador por grupo (no por módulo) y un solo color cuando hay algo
    realmente atrasado; el resto en neutro.
12. **Microcopy**: helper de plural (`1 día` / `2 días`), `CultureInfo("es-CL")` para miles y
    fechas, y validar el año al importar (evita `10/12/0002`).
13. **Jerarquía en las tarjetas de Certificados**: nombre + estado en negrita grande, el resto en
    una línea secundaria gris.

### Fase 4 — Pulido

14. Quitar los degradados difuminados de las tarjetas KPI.
15. Un solo Lico por pantalla.
16. Estado vacío en todas las bandejas (reutilizar el de Por clasificar).
17. Un único CTA primario por pantalla.
18. Decidir el modal del nombre: barra de entrada no modal, o validación real.

---

## 4. Checklist de verificación (para no volver a romperlo)

- [ ] `.topbar` mide lo mismo en `/`, `/estadisticas`, `/cajas`, `/certificados`.
- [ ] El `scrollWidth` de `.table-container` es ≤ su `clientWidth` a 1440 px con nombres largos.
- [ ] A 390 px, `document.scrollWidth == innerWidth` y no hay elementos cortados.
- [ ] En oscuro, ninguna fila de tabla tiene luminancia > 0,25.
- [ ] Toda acción principal es visible sin desplazamiento horizontal.
- [ ] Un solo buscador por pantalla.
- [ ] `1 día` / `2 días`, `1 carpeta` / `2 carpetas`, `25.517`.
- [ ] Un solo CTA primario por pantalla.

---

## 5. Documentación desactualizada detectada

- El README dice que `/` muestra «tarjetas por módulo»; `Home.razor` redirige directamente a
  `carpetas?bandeja=PorClasificar`. La portada de tarjetas ya no existe.
- El README dice que la app escucha en el puerto **5020**; `launchSettings.json` usa **5030**.
