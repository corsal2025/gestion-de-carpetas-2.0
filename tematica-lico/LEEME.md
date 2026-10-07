# Temática visual "Lico" — kit para replicar en otros sistemas

Sistema de origen: *Petición Cambio de Domicilio* (Depto. Licencias de Conducir, Municipalidad de Valparaíso).
Stack del original: ASP.NET Razor Pages + Bootstrap 5. El kit es **HTML/CSS/JS puro**, sirve en cualquier stack con Bootstrap 5.

## Contenido del kit

| Archivo | Para qué sirve |
|---|---|
| `tema-lico.css` | Tema completo: paleta, componentes, animaciones, estilos de la guía. |
| `lico.js` | La mascota Lico (SVG generado por código, sin imágenes). Solo la mascota. |
| `guia-interactiva.js` | Guía paso a paso con spotlight + Lico (incluye a Lico dentro). Es **un ejemplo a adaptar**: sus pasos (`PASOS_TOUR`) son de este sistema. |
| `logo-municipal.png` | Logo usado en la cabecera. |

## ¿Dónde está cada parte de Lico? (para copiar a otro proyecto)

| Qué | Dónde está | Qué copiar |
|---|---|---|
| **Dibujo de Lico** (cara, brazos, 4 poses) | `lico.js`, función `licoSvg()` | El archivo completo |
| **Placa con su nombre "LICO"** en la tarjeta | `lico.js`, dentro de `licoSvg()` (línea `<text ...>LICO</text>`) | Viene en el mismo archivo; para cambiar el nombre, edita esa palabra |
| **Globo de diálogo "¡Hola! Me llamo Lico"** | HTML: `<span class="head-lico-burbuja">…</span>` · Estilos: `tema-lico.css`, reglas `.head-lico`, `.head-lico-burbuja` y `.head-lico-burbuja::before` (la cola) | El HTML de abajo + el CSS completo |
| **Animaciones** (rebote, parpadeo, saludo) | `tema-lico.css`, reglas `.lico-svg` y `@keyframes lico-*` | El CSS completo |
| **Botón de ayuda con la carita** | HTML con `<span class="lico-mini" data-lico="saluda">` · Estilos `.btn-guia-accion`, `.lico-mini` | Ver `demo-lico.html` |
| **Guía paso a paso con Lico** | `guia-interactiva.js` (adaptar `PASOS_TOUR`) | Solo si quieres la guía |

**Atajo:** abre `demo-lico.html` en el navegador: muestra el encabezado con Lico, la placa, el globo y las 4 poses. Copia de ahí el bloque que necesites.

## La temática en una frase

**Tarjeta de licencia + carretera:** superficies con esquinas muy redondeadas como una licencia, una línea amarilla discontinua (la línea de la calzada) como firma visual, la paleta del logo municipal, y una mascota —Lico, una licencia de conducir con cara— que guía al usuario.

## Paleta (variables en `:root`)

| Variable | Valor | Uso |
|---|---|---|
| `--accent` | `#26388c` | Azul institucional: botones primarios, encabezados, nav activo |
| `--accent-hover` | `#1a2766` | Hover del azul |
| `--accent-soft` | `#e6e9f6` | Fondos suaves, chips, burbujas de Lico |
| `--brand-cyan` | `#1fb5d9` | Borde bajo el encabezado de tabla, degradés |
| `--brand-green` | `#2fb56a` | Barra "Enviadas" |
| `--brand-pink` | `#e889c4` | Mejillas de Lico, franja del logo |
| `--brand-yellow` | `#f6d23c` | **Línea de carretera**, foco, spotlight de la guía |
| `--brand-red` | `#e8503f` | Barra "Vencidas", alertas |
| `--canvas` | `#eef2fb` | Fondo de página (con puntitos sutiles) |
| `--ink` / `--ink-soft` / `--ink-faint` | `#1b2140` / `#4d5578` / `#7a82a3` | Texto |

Estados de fila (cada uno con `ink`, `bg`, `rail`, `chip`): `ok` lila (enviado), `warn` naranja, `bad` rojo, `mark` amarillo (marcada), `done` azul (cerrada).
**Para otra dependencia con otros colores, cambia solo estas variables**: todo el tema las usa.

## Motivos de diseño (lo que hace que "se vea igual")

1. **Encabezado de página** (`.page-head`): tarjeta azul con degradé, radio 22px, línea amarilla discontinua abajo (`--road`) y contadores blancos encima.
2. **Contadores** (`.stat-chip`): tarjetas blancas con barra de color arriba y número grande; al pasar el cursor se levantan y giran 1°.
3. **Botones** en forma de píldora (`border-radius: 999px`); primario con degradé azul; foco amarillo de 3px.
4. **Tabla** (`.table-responsive`): radio 20px, sombra, encabezado azul con borde inferior cian de 4px, chips redondeados para comuna/estado.
5. **Navegación**: píldoras; la activa en degradé azul con sombra.
6. **Franja superior** de 5px con los colores del logo (en `.navbar::before`).
7. **Lico** (mascota) en 4 poses y botón de guía circular con su cara.
8. **Guía interactiva**: spotlight con borde amarillo, tarjeta con línea de carretera arriba, Lico a la izquierda y texto en burbuja, barra de progreso.

## Animaciones

Todas en CSS, dentro de `tema-lico.css`:

| Animación | Dónde | Detalle |
|---|---|---|
| `lico-rebote` | Cuerpo de Lico | sube 5px y se inclina ±3°, 2.6s, infinito |
| `lico-parpadeo` | Ojos de Lico | parpadea cada 4.5s |
| `lico-saludo` | Brazo de Lico (pose `saluda`) | balanceo de ±16°, 1.4s |
| Hover de contadores / botones | `.stat-chip`, `.btn-primary` | `translateY(-3px) rotate(-1deg)` / `translateY(-1px)` |
| Spotlight de la guía | `.tour-spotlight` | transición de posición y tamaño .25s |

Respeta `prefers-reduced-motion: reduce`: las animaciones de Lico se apagan solas.

## Cómo instalarlo en otro sistema (3 pasos)

1. Copia `tema-lico.css`, `lico.js` (y `guia-interactiva.js` si quieres la guía) y `logo-municipal.png` a la carpeta de estáticos del otro sistema.
2. En el `<head>`, **después** de Bootstrap 5:
   ```html
   <link rel="stylesheet" href="/css/bootstrap.min.css">
   <link rel="stylesheet" href="/css/tema-lico.css">
   ```
   Antes de `</body>`:
   ```html
   <script src="/js/lico.js"></script>
   ```
   (Si usas `guia-interactiva.js`, **no** cargues `lico.js`: ya incluye a Lico.)
3. Usa las clases del tema en el markup. Mínimo útil:
   ```html
   <div class="page-head">
     <button type="button" class="head-lico" data-lico="saluda"><span class="head-lico-burbuja">¡Hola! Me llamo Lico</span></button>
     <div><h1 class="h3">Título de la página</h1><div class="sub">Subtítulo</div></div>
     <div class="stat-row">
       <div class="stat-chip"><b class="num">120</b><span>Total</span></div>
       <div class="stat-chip stat-chip--soft"><b class="num">12</b><span>Pendientes</span></div>
       <div class="stat-chip stat-chip--ok"><b class="num">100</b><span>Listas</span></div>
       <div class="stat-chip stat-chip--bad"><b class="num">8</b><span>Vencidas</span></div>
     </div>
   </div>
   <div class="toolbar mb-2"> <button class="btn btn-sm btn-primary">Acción</button> </div>
   <div class="table-responsive"><table class="table align-middle">…</table></div>
   ```
   Lico en cualquier parte: `<span data-lico="celebra"></span>` (poses: `saluda`, `explica`, `alerta`, `celebra`).

## Clases del tema (referencia rápida)

`.page-head` `.sub` · `.stat-row` `.stat-chip` (`--soft` `--ok` `--bad`) · `.head-lico` `.head-lico-burbuja` · `.toolbar` `.divider` `.spacer` ·
`.btn-primary` `.btn-outline-*` · `.table-responsive` `.table` · `.comuna-chip` `.pill` (`--ok` `--warn` `--bad` `--idle`) ·
`.plazo` (`--ok` `--warn` `--bad`) con `.plazo-bar > i` y `.plazo-txt` · filas `.fila-pendiente` `.fila-enviada` `.fila-sincorreo` `.fila-error` `.fila-marcada` `.fila-resuelta` ·
`.modo-franja` (`--real` `--prueba`) · `.btn-guia-accion` con `<span class="lico-mini" data-lico="saluda">` · `.sin-seleccion` · `.tour-*`.

## Instrucción lista para pegar a Claude en el otro sistema

> Aplica a este proyecto la temática visual "Lico" **sin cambiar ninguna función, ruta, handler ni lógica**: solo estilos y markup decorativo.
>
> 1. Copia desde `<RUTA>/docs/tematica-lico/` los archivos `tema-lico.css` y `lico.js` a la carpeta de estáticos de este proyecto (y `logo-municipal.png` si la cabecera usa logo). Si quiero guía interactiva, copia también `guia-interactiva.js` y reemplaza `PASOS_TOUR` por los pasos reales de este sistema (selector `target`, `titulo`, `descripcion`, `pagina`, `pose`); no cargues `lico.js` junto con ese archivo.
> 2. Carga `tema-lico.css` **después** de Bootstrap 5 en el layout principal, y `lico.js` antes de `</body>`.
> 3. Adapta el markup existente a las clases del tema: encabezado de página → `.page-head` con `.stat-row`/`.stat-chip` para los contadores; barra de acciones → `.toolbar`; tablas → `.table-responsive` + `.table`; estados → `.pill`, `.comuna-chip`, `.plazo`; filas → `.fila-*`. Agrega a Lico con `data-lico` en el encabezado y en el botón de ayuda.
> 4. Si este sistema tiene otra paleta, cambia solo las variables `:root` (`--accent`, `--brand-*`), nunca los componentes.
> 5. No uses fuentes ni librerías externas (el sistema corre on-premise). Mantén accesibilidad: foco visible, `aria-label` en botones solo-icono, y respeta `prefers-reduced-motion`.
> 6. Al terminar, levanta la app, revisa cada pantalla en el navegador y confirma que los tests existentes siguen pasando. No hagas commit.

## Notas para quien lo reutilice

- `tema-lico.css` es el `site.css` **completo** del sistema original (base + rediseño); incluye estilos de componentes que otro sistema quizás no use (se ignoran sin problema).
- Algunas clases del tema (`.carpeta-select`, `.plazo`, `.comuna-chip`) nacieron de este sistema; en otro, conviértelas a su equivalente o elimínalas.
- Lico es SVG por código: se puede cambiar de pose o colores editando `licoSvg()` en `lico.js`.
- `lico-juegos.js` (opcional, decorativo): travesuras de Lico tras 90 s de inactividad (toc, pelota, duerme, domina) y 7 gracias al hacer clic en `.head-lico`. Se carga **despues** de `lico.js` o `guia-interactiva.js` (requiere `window.Lico.svg`). Los estilos (`.lico-travieso`, `.lt-*`, `.modo-*`, `.hl-*`, `.lico-sueno`) van en el CSS del tema. Se desactiva con `window.LICO_JUEGOS = false` y respeta `prefers-reduced-motion`. Ojo: el clic en `.head-lico` ya no abre la guia (usa el boton `.btn-guia-accion`). Esta copia es la adaptada a Gestion de carpetas 2.0 (deteccion de modales `.modal-overlay`).
