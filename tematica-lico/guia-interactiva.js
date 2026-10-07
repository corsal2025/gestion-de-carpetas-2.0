(function () {
  'use strict';

  var tourPasoActual = 0;
  var tourKeyHandler = null;

  var SECCIONES = { '/': 'Peticiones', '/comunas': 'Comunas', '/estadisticas': 'Estadísticas', '/configuracion': 'Configuración' };

  // pagina: ruta donde vive el paso. pose: pose de Lico (saluda | explica | alerta | celebra).
  var PASOS_TOUR = [
    // ---------- PETICIONES: cabecera ----------
    {
      pagina: '/', pose: 'saluda',
      target: '#btn-guia-global',
      titulo: '¡Hola! Me llamo Lico',
      icono: '👋',
      descripcion: 'Soy tu guía. Este botón (y yo, en el encabezado) abre esta guía en cualquier momento. Vamos a recorrer <b>todo el sistema</b>: Peticiones, Comunas, Estadísticas y Configuración. Usa <b>Siguiente / Anterior</b> o las flechas del teclado; <b>Esc</b> cierra la guía.'
    },
    {
      pagina: '/',
      target: '.navbar-nav',
      titulo: 'Los 4 módulos del sistema',
      icono: '🧭',
      descripcion: '<b>Peticiones</b>: la gestión diaria. <b>Comunas</b>: directorio de correos municipales. <b>Estadísticas</b>: tiempos de respuesta. <b>Configuración</b>: verificación del sistema y parámetros. El módulo activo se marca en azul.'
    },
    {
      pagina: '/', pose: 'alerta',
      target: '.modo-franja',
      fallbackTarget: '.navbar',
      titulo: 'Franja de modo de envío',
      icono: '🚦',
      descripcion: 'Si aparece una franja bajo el menú, indica cómo salen los correos: <b>ENVÍO REAL ACTIVO</b> (llegan a las municipalidades) o <b>MODO PRUEBA</b> (todo se desvía a un correo de prueba y nada llega a las comunas). Mírala siempre antes de enviar.'
    },
    {
      pagina: '/',
      target: '.stat-row',
      titulo: 'Contadores y plazo legal de 15 días',
      icono: '📊',
      descripcion: '<b>Total</b> de solicitudes cargadas, <b>Pendientes</b> por enviar, <b>Enviadas</b> y <b>Vencidas</b> según el plazo de <b>15 días hábiles</b> (Decreto Supremo 170, Art. 14).'
    },
    {
      pagina: '/', pose: 'alerta',
      target: '.alert',
      fallbackTarget: '.page-head',
      titulo: 'Avisos del sistema',
      icono: '⚠️',
      descripcion: 'Bajo el encabezado pueden aparecer avisos, por ejemplo <b>comunas sin correo en el directorio</b>: esas peticiones quedan en espera. Carga el correo en <b>Comunas</b> y vuelve a enviar: se retoman solas. También avisa si el modo prueba está activo.'
    },

    // ---------- PETICIONES: barra de acciones ----------
    {
      pagina: '/',
      target: '#btn-importar',
      fallbackTarget: '.toolbar',
      titulo: 'Cargar cambios de domicilio',
      icono: '📥',
      descripcion: 'Lee el libro Excel departamental y trae a todas las personas marcadas con <code>CAMBIO DE DOMICILIO</code> en las hojas mensuales de cada oficina (Av. Argentina, Placilla, Mercado Puerto). Valida RUTs, normaliza comunas y <b>omite duplicados</b>.'
    },
    {
      pagina: '/',
      target: '#btn-actualizar-carpetas',
      fallbackTarget: '.toolbar',
      titulo: 'Actualizar estado solicitud',
      icono: '🔄',
      descripcion: 'Busca en el Excel, por RUT, si la carpeta de cada persona avanzó de estado (por ejemplo, ya fue subida). Si el Excel la tiene más avanzada, actualiza la base local sin alterar los envíos ya realizados.'
    },
    {
      pagina: '/',
      target: '#btn-respaldar',
      fallbackTarget: '.toolbar',
      titulo: 'Respaldar ahora',
      icono: '💾',
      descripcion: 'Guarda una copia de la base local en tu carpeta de documentos, fuera de la carpeta de la aplicación. Úsalo antes de operaciones masivas. Además, el sistema crea un respaldo automático diario.'
    },
    {
      pagina: '/',
      target: '#btn-enviar-marcadas',
      fallbackTarget: '.toolbar',
      titulo: 'Enviar marcadas',
      icono: '✉️',
      descripcion: 'Marca personas con la casilla y pulsa este botón: se genera el oficio y se envía <b>un único correo por comuna</b> con todas las personas marcadas de esa municipalidad, con pausas antispam. El número indica cuántas marcadas hay. Pide confirmación y está desactivado si no hay marcadas.'
    },
    {
      pagina: '/',
      target: '#btn-marcar-sin-correo',
      fallbackTarget: '.toolbar',
      titulo: 'Marcar como enviadas (sin correo)',
      icono: '✓',
      descripcion: 'Registra las filas marcadas como <b>enviadas sin mandar correo</b> a la comuna. Sirve para regularizar casos que ya se tramitaron por otra vía. Pide confirmación.'
    },
    {
      pagina: '/',
      target: '#btn-ver-ya-subidas',
      fallbackTarget: '.toolbar',
      titulo: 'Ver todas como ya subidas',
      icono: '☑️',
      descripcion: 'Deja <b>todas las peticiones pendientes</b> como ya subidas, sin enviar correos a las comunas. Es la versión masiva del botón anterior; se desactiva si no queda nada pendiente. Pide confirmación.'
    },
    {
      pagina: '/', pose: 'alerta',
      target: '#btn-borrar-seleccion',
      fallbackTarget: '.toolbar',
      titulo: 'Borrar selección',
      icono: '🗑️',
      descripcion: 'Borra las peticiones seleccionadas y su historial de envío. Antes guarda un <b>respaldo automático</b> y <b>nunca toca el Excel</b>. El número indica cuántas hay seleccionadas.'
    },
    {
      pagina: '/',
      target: '#btn-peticion-manual',
      fallbackTarget: '.toolbar',
      titulo: 'Petición manual',
      icono: '✍️',
      descripcion: 'Despliega un panel para agregar a una persona que la carga automática no trae: <b>Nombre, RUT y Comuna</b>. El sistema valida el RUT y la incorpora a la tabla lista para enviar.'
    },
    {
      pagina: '/',
      target: '#btn-imprimir-lista',
      fallbackTarget: '.toolbar',
      titulo: 'Imprimir lista',
      icono: '🖨️',
      descripcion: 'Abre una vista limpia, lista para imprimir, con <b>todas</b> las peticiones, su fecha de solicitud y su plazo. Se abre en una pestaña nueva.'
    },
    {
      pagina: '/',
      target: '#btn-imprimir-marcadas',
      fallbackTarget: '.toolbar',
      titulo: 'Imprimir marcadas',
      icono: '🖨️',
      descripcion: 'Igual que el anterior, pero solo con el <b>lote marcado</b>. Ideal para cotejo físico o archivo en papel.'
    },
    {
      pagina: '/', pose: 'alerta',
      target: '#btn-borrar-todo',
      fallbackTarget: '.toolbar',
      titulo: 'Borrar todos los datos',
      icono: '⛔',
      descripcion: 'Vacía todas las peticiones cargadas, incluido el historial de envío. Pide confirmación y guarda antes un <b>respaldo automático</b> de la base. El Excel maestro no se modifica.'
    },

    // ---------- PETICIONES: búsqueda y tabla ----------
    {
      pagina: '/',
      target: '#buscador',
      titulo: 'Buscador instantáneo',
      icono: '🔍',
      descripcion: 'Filtra la tabla mientras escribes, sin recargar. Busca por <b>RUT</b> (con o sin puntos y guion) o por cualquier parte del <b>nombre</b>, sin importar mayúsculas ni tildes. A su lado verás cuántas filas coinciden.'
    },
    {
      pagina: '/',
      target: '#chk-marcar-todas',
      titulo: 'Marcar o desmarcar todas',
      icono: '☑️',
      descripcion: 'La casilla del encabezado marca o desmarca <b>todas las pendientes</b> de una vez. Las marcadas alimentan los botones <b>Enviar marcadas</b>, <b>Marcar como enviadas</b> e <b>Imprimir marcadas</b>.'
    },
    {
      pagina: '/',
      target: '.js-marcar-btn',
      fallbackTarget: '#tabla-peticiones',
      titulo: 'Casilla de cada persona',
      icono: '✔️',
      descripcion: 'Solo las carpetas <b>pendientes de tratar</b> tienen casilla ☐ / ☑. Al marcarla, la fila se pinta de <b>amarillo</b> y los contadores de los botones se actualizan al instante. Las carpetas ya enviadas o subidas muestran «—» y <b>no se pueden seleccionar</b>.'
    },
    {
      pagina: '/',
      target: '#tabla-peticiones thead',
      fallbackTarget: '#tabla-peticiones',
      titulo: 'Columnas: Nombre, RUT, Comuna y F. solicitud',
      icono: '📋',
      descripcion: '<b>Nombre</b> y <b>RUT</b> de la persona (un ⚠ junto al RUT indica que no pudo validarse). <b>Comuna</b> de destino. <b>F. solicitud</b> es la fecha en que se pidió la carpeta a la comuna; dice «sin pedir» hasta que se envía el correo.'
    },
    {
      pagina: '/',
      target: '#tabla-peticiones tbody tr td:nth-child(6)',
      fallbackTarget: '#tabla-peticiones',
      titulo: 'Columna Envío',
      icono: '📮',
      descripcion: 'Estado del correo: <b>Sin enviar</b>, <b>Enviado</b> (con fecha y hora), <b>Sin correo comuna</b> (falta cargar su correo) o <b>Error</b>. Pasa el cursor sobre la etiqueta para ver el detalle.'
    },
    {
      pagina: '/',
      target: '#tabla-peticiones tbody tr td:nth-child(7)',
      fallbackTarget: '#tabla-peticiones',
      titulo: 'Columna Plazo (15 días hábiles)',
      icono: '⏱️',
      descripcion: 'El reloj parte cuando se envía el correo. La barra es <b>verde</b> con holgura, <b>ámbar</b> cuando quedan 3 días o menos y <b>roja</b> si está vencido. Al subirse la carpeta muestra «Cerrada» con los días que tardó.'
    },
    {
      pagina: '/',
      target: '.js-carpeta-select',
      fallbackTarget: '#tabla-peticiones',
      titulo: 'Estado de la carpeta',
      icono: '🗂️',
      descripcion: 'Selector por persona: <b>— sin subir —</b>, <b>Subida a CONASET</b> o <b>Subida con correo</b>. Se guarda al cambiarlo y, si corresponde, muestra la fecha de subida. La fila cerrada se pinta de azul.'
    },
    {
      pagina: '/',
      target: '#tabla-peticiones tbody tr td:nth-child(9)',
      fallbackTarget: '#tabla-peticiones',
      titulo: 'Columna Origen',
      icono: '🏢',
      descripcion: 'La oficina de donde viene la solicitud: Av. Argentina, Placilla o Mercado Puerto. Pasa el cursor para ver la hoja exacta del Excel de la que se leyó.'
    },
    {
      pagina: '/',
      target: 'button[formaction*="EnviarComuna"]',
      fallbackTarget: '.col-acciones',
      titulo: 'Enviar comuna (N)',
      icono: '📤',
      descripcion: 'Despacha de golpe <b>un correo con todas las personas pendientes de esa comuna</b>; el número N es cuántas son. Si alguna tiene RUT sin validar, te lo advierte antes de confirmar. Cuando ya se envió, la fila muestra «✓ Enviada».'
    },
    {
      pagina: '/', pose: 'alerta',
      target: 'button[formaction*="Eliminar"]',
      fallbackTarget: '.col-acciones',
      titulo: 'Eliminar una petición (✕)',
      icono: '❌',
      descripcion: 'Quita solo esa petición, previa confirmación. No afecta al Excel.'
    },
    {
      pagina: '/',
      target: '#tabla-peticiones tbody',
      fallbackTarget: '#tabla-peticiones',
      titulo: 'Colores de las filas',
      icono: '🎨',
      descripcion: '<b>Amarillo</b>: marcada. <b>Lila</b>: correo enviado. <b>Azul</b>: carpeta subida y cerrada. <b>Naranja</b>: sin correo de comuna o RUT dudoso. <b>Rojo</b>: error de envío. <b>Blanco</b>: pendiente normal.'
    },

    // ---------- COMUNAS ----------
    {
      pagina: '/comunas', pose: 'saluda',
      target: 'main h1',
      fallbackTarget: 'main',
      titulo: 'Directorio de correos municipales',
      icono: '🏛️',
      descripcion: 'Aquí viven los correos oficiales de cada municipalidad. Sin el correo de una comuna, sus peticiones no pueden enviarse. Una comuna puede tener más de un correo.'
    },
    {
      pagina: '/comunas',
      target: '#comuna-agregar',
      fallbackTarget: 'main',
      titulo: 'Agregar comuna',
      icono: '➕',
      descripcion: 'Escribe el nombre de la comuna (el campo sugiere las ya conocidas) y su correo, y pulsa <b>Agregar</b>. El dominio se deduce solo del correo.'
    },
    {
      pagina: '/comunas',
      target: '#comuna-importar',
      fallbackTarget: 'main',
      titulo: 'Importar desde el Excel',
      icono: '📥',
      descripcion: 'Lee la hoja <code>CORREOS CAMBIO DE DOMICLIO</code> del libro y agrega las comunas que falten, <b>sin borrar ni pisar</b> las existentes. Se desactiva si falta configurar la ruta del Excel.'
    },
    {
      pagina: '/comunas',
      target: '#comuna-buscar',
      fallbackTarget: 'main',
      titulo: 'Buscar comuna o correo',
      icono: '🔍',
      descripcion: 'Filtra el directorio por nombre de comuna o por cualquier parte del correo. Pulsa <b>Buscar</b> para aplicar el filtro.'
    },
    {
      pagina: '/comunas',
      target: '#tabla-comunas',
      fallbackTarget: 'main',
      titulo: 'Tabla del directorio',
      icono: '📇',
      descripcion: 'Columnas <b>Comuna</b>, <b>Correo</b> y <b>Dominio</b>. Para corregir un correo, edítalo en su casilla y pulsa <b>✔</b> para guardar. El <b>✕</b> elimina ese correo, previa confirmación.'
    },

    // ---------- ESTADÍSTICAS ----------
    {
      pagina: '/estadisticas', pose: 'saluda',
      target: '#est-contadores',
      fallbackTarget: 'main',
      titulo: 'Estadísticas: resumen general',
      icono: '📈',
      descripcion: '<b>Carpetas pedidas</b>, <b>comunas</b> con envíos, % <b>dentro de plazo</b>, <b>demora promedio</b>, cuántas <b>subimos nosotros</b> y cuántas siguen <b>sin cerrar</b>. Si aún no hay envíos, la pantalla lo indica y las estadísticas aparecen al empezar a enviar.'
    },
    {
      pagina: '/estadisticas',
      target: '#est-ranking',
      fallbackTarget: 'main',
      titulo: 'Comunas con más cambios de domicilio',
      icono: '🏆',
      descripcion: 'Ranking de las comunas a las que más carpetas hemos pedido. Cada barra es proporcional a la cantidad y el número a su derecha es el total.'
    },
    {
      pagina: '/estadisticas',
      target: '#est-quien',
      fallbackTarget: 'main',
      titulo: 'Quién sube la carpeta a CONASET',
      icono: '🗄️',
      descripcion: 'De las carpetas ya cerradas, la proporción que subió la <b>comuna</b> frente a las que <b>subimos nosotros</b>. Sirve para ver qué municipalidades no responden y hay que cubrir.'
    },
    {
      pagina: '/estadisticas',
      target: '#est-tabla',
      fallbackTarget: 'main',
      titulo: 'Cumplimiento por comuna',
      icono: '✅',
      descripcion: 'Por cada comuna: <b>Solicitadas</b>, <b>Subió comuna</b>, <b>Subimos nosotros</b>, <b>Demora promedio</b> y <b>% en plazo</b>. La demora se mide desde el envío del correo hasta que la carpeta figura como subida en el Excel.'
    },

    // ---------- CONFIGURACIÓN ----------
    {
      pagina: '/configuracion', pose: 'saluda',
      target: '#cfg-verificacion',
      fallbackTarget: 'main',
      titulo: 'Verificación del sistema',
      icono: '🩺',
      descripcion: 'Lista de chequeos: <b>✔</b> correcto, <b>✖</b> bloqueante, <b>⚠</b> advertencia. El sello indica <b>LISTO PARA USAR</b> o <b>FALTAN COSAS</b>. Revísala antes de enviar por primera vez.'
    },
    {
      pagina: '/configuracion',
      target: '#btn-probar-correo',
      fallbackTarget: '#cfg-verificacion',
      titulo: 'Enviarme un correo de prueba',
      icono: '🧪',
      descripcion: 'Manda el correo de la vista previa, tal cual, a tu propio buzón (o al de prueba) con <code>[PRUEBA]</code> al inicio. Nada llega a ninguna municipalidad.'
    },
    {
      pagina: '/configuracion',
      target: '#cfg-vista',
      fallbackTarget: 'main',
      titulo: 'Correo que recibe la comuna',
      icono: '📨',
      descripcion: 'Vista previa exacta de lo que se envía por cada petición: asunto y cuerpo. Solo cambian el nombre y el RUT de la persona.'
    },
    {
      pagina: '/configuracion', pose: 'celebra',
      target: '#cfg-tabla',
      fallbackTarget: 'main',
      titulo: 'Parámetros del sistema',
      icono: '⚙️',
      descripcion: 'Muestra <b>Correo EWS</b>, <b>Modo prueba</b>, <b>buzón remitente</b>, <b>Excel de solicitudes</b>, la <b>orden que dispara</b> la carga, el <b>mapeo de columnas</b> y el directorio de comunas. Se editan en <code>appsettings.Local.json</code> y luego se reinicia la aplicación. ¡Terminaste el recorrido!'
    }
  ];

  // ---- Lico: mascota licencia de conducir (SVG generado, sin archivos externos) ----
  function licoSvg(pose) {
    var INK = '#23262a', AZ = '#26388c';
    var brazo = function (d) {
      return '<path d="' + d + '" stroke="' + AZ + '" stroke-width="9" fill="none" stroke-linecap="round"/>';
    };
    var mano = function (x, y, cls) {
      return '<circle ' + (cls ? 'class="' + cls + '" ' : '') + 'cx="' + x + '" cy="' + y + '" r="9" fill="#fff" stroke="' + AZ + '" stroke-width="3.5"/>';
    };
    var ojos = function (dx) {
      dx = dx || 0;
      return '<g class="lico-ojos">' +
        '<circle cx="' + (66 + dx) + '" cy="92" r="9" fill="' + INK + '"/><circle cx="' + (69 + dx) + '" cy="89" r="3" fill="#fff"/>' +
        '<circle cx="' + (114 + dx) + '" cy="92" r="9" fill="' + INK + '"/><circle cx="' + (117 + dx) + '" cy="89" r="3" fill="#fff"/></g>';
    };
    var bocaAbierta = '<path d="M72 108Q90 134 108 108Z" fill="#e8503f" stroke="' + INK + '" stroke-width="3" stroke-linejoin="round"/>';
    var izq = '', der = '', cara = '', extra = '';

    if (pose === 'celebra') {
      izq = brazo('M24 104Q-2 90 6 54') + mano(6, 50);
      der = brazo('M156 104Q182 90 174 54') + mano(174, 50);
      cara = '<path d="M57 96q9-14 18 0M105 96q9-14 18 0" stroke="' + INK + '" stroke-width="4" fill="none" stroke-linecap="round"/>' +
        '<path d="M70 106Q90 140 110 106Z" fill="#e8503f" stroke="' + INK + '" stroke-width="3" stroke-linejoin="round"/>';
      extra = '<rect x="30" y="8" width="8" height="14" rx="2" fill="#1fb5d9" transform="rotate(20 34 15)"/>' +
        '<rect x="188" y="30" width="8" height="14" rx="2" fill="#e889c4" transform="rotate(-25 192 37)"/>' +
        '<rect x="90" y="0" width="8" height="14" rx="2" fill="#f6d23c" transform="rotate(35 94 7)"/>' +
        '<circle cx="204" cy="86" r="5" fill="#2fb56a"/><circle cx="14" cy="22" r="5" fill="#e8503f"/>';
    } else if (pose === 'alerta') {
      izq = brazo('M24 104Q6 120 12 142') + mano(12, 144);
      der = brazo('M156 100Q184 88 184 62') + mano(184, 58);
      cara = '<g class="lico-ojos"><circle cx="66" cy="94" r="9" fill="' + INK + '"/><circle cx="114" cy="94" r="9" fill="' + INK + '"/>' +
        '<circle cx="69" cy="91" r="3" fill="#fff"/><circle cx="117" cy="91" r="3" fill="#fff"/></g>' +
        '<path d="M54 76l24 6M126 76l-24 6" stroke="' + INK + '" stroke-width="4" stroke-linecap="round"/>' +
        '<path d="M74 118q8-10 16 0t16 0" stroke="' + INK + '" stroke-width="4" fill="none" stroke-linecap="round"/>';
      extra = '<path d="M158 62q8 12 0 18q-8-6 0-18z" fill="#1fb5d9"/>' +
        '<circle cx="196" cy="34" r="18" fill="#fff" stroke="#e8503f" stroke-width="5"/>' +
        '<path d="M196 24v11l8 5" stroke="#e8503f" stroke-width="4" fill="none" stroke-linecap="round"/>';
    } else if (pose === 'explica') {
      izq = brazo('M24 104Q6 120 12 142') + mano(12, 144);
      der = brazo('M156 104Q190 108 204 90') + mano(206, 86);
      cara = ojos(3) + '<path d="M74 108Q90 126 106 108Z" fill="#e8503f" stroke="' + INK + '" stroke-width="3" stroke-linejoin="round"/>';
    } else { // saluda
      izq = brazo('M24 104Q6 120 12 142') + mano(12, 144);
      der = '<g class="lico-saludo">' + brazo('M156 100Q184 88 180 60') + mano(180, 56) + '</g>';
      cara = ojos(0) + bocaAbierta;
    }

    return '<svg class="lico-svg" viewBox="-6 -4 232 228" role="img" aria-label="Lico, la mascota de licencias de conducir">' +
      '<ellipse cx="90" cy="214" rx="52" ry="7" fill="rgba(27,33,80,.18)"/>' +
      '<g class="lico-cuerpo">' +
        '<rect x="52" y="160" width="14" height="30" rx="7" fill="' + AZ + '"/><rect x="104" y="160" width="14" height="30" rx="7" fill="' + AZ + '"/>' +
        '<ellipse cx="58" cy="194" rx="16" ry="9" fill="#f6d23c" stroke="' + AZ + '" stroke-width="3.5"/>' +
        '<ellipse cx="112" cy="194" rx="16" ry="9" fill="#f6d23c" stroke="' + AZ + '" stroke-width="3.5"/>' +
        izq +
        '<rect x="20" y="20" width="140" height="150" rx="24" fill="#fff" stroke="' + AZ + '" stroke-width="5"/>' +
        '<path d="M22.5 60V46a21.5 21.5 0 0 1 21.5-21.5h92a21.5 21.5 0 0 1 21.5 21.5V60z" fill="' + AZ + '"/>' +
        '<circle cx="44" cy="42" r="5" fill="#1fb5d9"/><circle cx="58" cy="42" r="5" fill="#2fb56a"/>' +
        '<circle cx="72" cy="42" r="5" fill="#e889c4"/><circle cx="86" cy="42" r="5" fill="#f6d23c"/>' +
        '<rect x="110" y="38" width="34" height="8" rx="4" fill="#fff" opacity=".9"/>' +
        cara +
        '<ellipse cx="48" cy="114" rx="9" ry="6" fill="#e889c4" opacity=".7"/><ellipse cx="132" cy="114" rx="9" ry="6" fill="#e889c4" opacity=".7"/>' +
        '<text x="44" y="147" font-family="Segoe UI, system-ui, sans-serif" font-size="17" font-weight="800" letter-spacing="1.5" fill="' + AZ + '">LICO</text>' +
        '<rect x="44" y="154" width="40" height="6" rx="3" fill="#d9dcee"/>' +
        '<rect x="110" y="140" width="26" height="18" rx="4" fill="#f6d23c" stroke="#c9a400" stroke-width="2"/>' +
        der + extra +
      '</g></svg>';
  }

  function poseParaPaso(i) {
    return PASOS_TOUR[i].pose || 'explica';
  }

  // Ruta actual normalizada: '/', '/comunas', '/estadisticas', '/configuracion'.
  function rutaActual() {
    var p = location.pathname.toLowerCase().replace(/\/+$/, '');
    if (p === '' || p === '/index') return '/';
    return p;
  }

  function seccionDe(paso) {
    return SECCIONES[paso.pagina] || '';
  }

  // Primer paso que pertenece a la página donde está el usuario.
  function primerPasoDePaginaActual() {
    var ruta = rutaActual();
    for (var i = 0; i < PASOS_TOUR.length; i++) {
      if (PASOS_TOUR[i].pagina === ruta) return i;
    }
    return 0;
  }

  // Cambia de paso; si el paso vive en otra página, navega y la guía continúa allá.
  function irAPaso(idx) {
    if (idx < 0 || idx >= PASOS_TOUR.length) return;
    if (PASOS_TOUR[idx].pagina !== rutaActual()) {
      cerrarGuiaInteractiva();
      location.href = PASOS_TOUR[idx].pagina + '?guia=1&paso=' + idx;
      return;
    }
    tourPasoActual = idx;
    renderPasoTour();
  }

  function montarLicos() {
    var els = document.querySelectorAll('[data-lico]');
    for (var i = 0; i < els.length; i++) {
      if (!els[i].querySelector('svg')) {
        els[i].insertAdjacentHTML('afterbegin', licoSvg(els[i].getAttribute('data-lico')));
      }
    }
  }

  function iniciarGuiaInteractiva(pasoInicial) {
    var tip = document.getElementById('tooltip-flotante');
    if (tip) tip.classList.remove('visible');

    cerrarGuiaInteractiva();
    tourPasoActual = (typeof pasoInicial === 'number' && pasoInicial >= 0 && pasoInicial < PASOS_TOUR.length) ? pasoInicial : 0;

    var overlay = document.createElement('div');
    overlay.id = 'tour-overlay';
    overlay.className = 'tour-overlay';
    overlay.innerHTML =
      '<button class="tour-btn-salir-flotante" id="tour-salir-flotante" title="Terminar y cerrar la guía">' +
        '<span>✕</span> Cerrar guía' +
      '</button>' +
      '<div id="tour-spotlight" class="tour-spotlight"></div>' +
      '<div id="tour-card" class="tour-card">' +
        '<div class="tour-card-header">' +
          '<span class="tour-paso-badge" id="tour-badge">Paso 1 de ' + PASOS_TOUR.length + '</span>' +
          '<button class="tour-btn-cerrar" id="tour-cerrar" title="Cerrar guía">&times;</button>' +
        '</div>' +
        '<div class="tour-card-body">' +
          '<div class="tour-mascota" id="tour-mascota"></div>' +
          '<div class="tour-card-texto">' +
            '<h3 id="tour-titulo" class="tour-card-titulo"></h3>' +
            '<p id="tour-desc" class="tour-card-desc"></p>' +
          '</div>' +
        '</div>' +
        '<div class="tour-card-footer">' +
          '<button class="btn btn-sm btn-outline-secondary" id="tour-prev">Anterior</button>' +
          '<div class="tour-dots" id="tour-dots"></div>' +
          '<button class="btn btn-sm btn-primary" id="tour-next">Siguiente</button>' +
        '</div>' +
        '<div id="tour-flecha" class="tour-flecha"></div>' +
      '</div>';

    document.body.appendChild(overlay);

    document.getElementById('tour-cerrar').onclick = cerrarGuiaInteractiva;
    document.getElementById('tour-salir-flotante').onclick = cerrarGuiaInteractiva;
    overlay.onclick = function (e) {
      if (e.target === overlay) cerrarGuiaInteractiva();
    };

    tourKeyHandler = function (e) {
      if (e.key === 'Escape') {
        cerrarGuiaInteractiva();
      } else if (e.key === 'ArrowRight' && tourPasoActual < PASOS_TOUR.length - 1) {
        irAPaso(tourPasoActual + 1);
      } else if (e.key === 'ArrowLeft' && tourPasoActual > 0) {
        irAPaso(tourPasoActual - 1);
      }
    };
    window.addEventListener('keydown', tourKeyHandler);

    document.getElementById('tour-prev').onclick = function () {
      if (tourPasoActual > 0) irAPaso(tourPasoActual - 1);
    };

    document.getElementById('tour-next').onclick = function () {
      if (tourPasoActual < PASOS_TOUR.length - 1) {
        irAPaso(tourPasoActual + 1);
      } else {
        cerrarGuiaInteractiva();
        mostrarToast('¡Guía interactiva completada!');
      }
    };

    renderPasoTour(true);
  }

  function renderPasoTour(esPrimerRender) {
    var paso = PASOS_TOUR[tourPasoActual];
    var target = document.querySelector(paso.target) || (paso.fallbackTarget ? document.querySelector(paso.fallbackTarget) : null) || document.querySelector('main');
    if (!target) return;

    var badge = document.getElementById('tour-badge');
    var titulo = document.getElementById('tour-titulo');
    var desc = document.getElementById('tour-desc');
    var prev = document.getElementById('tour-prev');
    var next = document.getElementById('tour-next');
    var dots = document.getElementById('tour-dots');

    if (badge) badge.textContent = 'Paso ' + (tourPasoActual + 1) + ' de ' + PASOS_TOUR.length + ' · ' + seccionDe(paso);
    if (titulo) titulo.innerHTML = '<span class="tour-ico">' + paso.icono + '</span> ' + paso.titulo;
    if (desc) desc.innerHTML = paso.descripcion;
    var mascota = document.getElementById('tour-mascota');
    if (mascota) mascota.innerHTML = licoSvg(poseParaPaso(tourPasoActual));
    if (prev) prev.disabled = tourPasoActual === 0;

    var esUltimo = tourPasoActual === PASOS_TOUR.length - 1;
    if (next) {
      if (esUltimo) {
        next.textContent = '✔ ¡Finalizar!';
        next.style.background = '#10b981';
        next.style.borderColor = '#059669';
        next.style.color = '#fff';
        next.style.fontWeight = '700';
      } else {
        next.textContent = 'Siguiente';
        next.style.background = '';
        next.style.borderColor = '';
        next.style.color = '';
        next.style.fontWeight = '';
      }
    }

    if (dots) {
      // Barra de progreso: con decenas de pasos, los puntos ya no caben.
      dots.innerHTML = '<span class="tour-progreso" role="progressbar" aria-valuemin="1" aria-valuemax="' + PASOS_TOUR.length +
        '" aria-valuenow="' + (tourPasoActual + 1) + '"><i style="width:' + Math.round((tourPasoActual + 1) * 100 / PASOS_TOUR.length) + '%"></i></span>';
    }

    var posicionar = function () {
      var r = target.getBoundingClientRect();
      var spot = document.getElementById('tour-spotlight');
      var card = document.getElementById('tour-card');
      var flecha = document.getElementById('tour-flecha');
      if (!spot || !card) return;

      var cardW = Math.min(540, window.innerWidth - 32);
      var cardH = 260;
      var pad = 6;
      var esGrilla = paso.target === '.table-responsive' || r.height > window.innerHeight * 0.55;

      if (esPrimerRender) {
        spot.style.transition = 'none';
        card.style.transition = 'none';
      }

      if (esGrilla) {
        var spotTop = Math.max(12, Math.round(r.top));
        var spotH = Math.min(Math.round(r.height), window.innerHeight - spotTop - 24);
        spot.style.left = Math.max(10, Math.round(r.left - pad)) + 'px';
        spot.style.top = spotTop + 'px';
        spot.style.width = Math.min(window.innerWidth - 20, Math.round(r.width + pad * 2)) + 'px';
        spot.style.height = Math.max(220, spotH) + 'px';

        var cardLeft = Math.round((window.innerWidth - cardW) / 2);
        var cardTop = Math.round(Math.max(80, (window.innerHeight - cardH) / 2));
        card.style.left = cardLeft + 'px';
        card.style.top = cardTop + 'px';
        if (flecha) flecha.style.display = 'none';
      } else {
        if (flecha) flecha.style.display = 'block';

        // Spotlight regular
        spot.style.left = Math.max(0, Math.round(r.left - pad)) + 'px';
        spot.style.top = Math.max(0, Math.round(r.top - pad)) + 'px';
        spot.style.width = Math.round(r.width + pad * 2) + 'px';
        spot.style.height = Math.round(r.height + pad * 2) + 'px';
        // Elementos más altos que la pantalla: el recuadro se recorta al área visible.
        var spotTopPx = parseInt(spot.style.top, 10);
        var maxH = window.innerHeight - spotTopPx - 12;
        if (parseInt(spot.style.height, 10) > maxH) spot.style.height = Math.max(120, maxH) + 'px';

        // Posicionar tarjeta
        var cLeft = Math.round(r.left + (r.width / 2) - (cardW / 2));
        if (cLeft < 16) cLeft = 16;
        if (cLeft + cardW > window.innerWidth - 16) cLeft = window.innerWidth - cardW - 16;

        var cTop = Math.round(r.bottom + 14);
        var flechaArriba = true;

        if (cTop + cardH > window.innerHeight - 16) {
          cTop = Math.round(r.top - cardH - 14);
          flechaArriba = false;
        }

        if (cTop < 16) cTop = 16;
        if (cTop + cardH > window.innerHeight - 16) cTop = window.innerHeight - cardH - 16;

        card.style.left = cLeft + 'px';
        card.style.top = cTop + 'px';

        if (flecha) {
          flecha.className = 'tour-flecha ' + (flechaArriba ? 'flecha-arriba' : 'flecha-abajo');
          var flechaX = Math.max(24, Math.min(cardW - 36, (r.left + r.width / 2) - cLeft));
          flecha.style.left = Math.round(flechaX) + 'px';
        }
      }

      if (esPrimerRender) {
        void card.offsetHeight;
        spot.style.transition = '';
        card.style.transition = '';
      }

      spot.classList.add('visible');
      card.classList.add('visible');
    };

    var rect = target.getBoundingClientRect();
    var yaVisible = rect.top >= 0 && rect.bottom <= window.innerHeight;

    if (yaVisible) {
      posicionar();
    } else {
      // Instantáneo: el spotlight se mide después del desplazamiento, no durante.
      target.scrollIntoView({ behavior: 'auto', block: 'center' });
      setTimeout(posicionar, 60);
    }
  }

  function cerrarGuiaInteractiva() {
    if (tourKeyHandler) {
      window.removeEventListener('keydown', tourKeyHandler);
      tourKeyHandler = null;
    }
    var o = document.getElementById('tour-overlay');
    if (o) o.remove();
  }

  function mostrarToast(mensaje) {
    var toast = document.getElementById('tour-toast');
    if (!toast) {
      toast = document.createElement('div');
      toast.id = 'tour-toast';
      toast.className = 'tour-toast';
      document.body.appendChild(toast);
    }
    toast.innerHTML = '<span>✔</span> ' + mensaje;
    toast.classList.add('visible');
    setTimeout(function () {
      toast.classList.remove('visible');
    }, 3200);
  }

  function iniciarTooltipsGlobales() {
    var tip = document.getElementById('tooltip-flotante');
    if (!tip) {
      tip = document.createElement('div');
      tip.id = 'tooltip-flotante';
      tip.className = 'tooltip-flotante';
      document.body.appendChild(tip);
    }

    document.addEventListener('mouseover', function (e) {
      var el = e.target.closest('[data-tooltip]');
      if (!el) {
        tip.classList.remove('visible');
        return;
      }
      var texto = el.getAttribute('data-tooltip');
      if (!texto) return;
      tip.textContent = texto;
      tip.classList.add('visible');

      var rect = el.getBoundingClientRect();
      var tipRect = tip.getBoundingClientRect();
      var left = rect.left + rect.width / 2 - tipRect.width / 2;
      if (left < 10) left = 10;
      if (left + tipRect.width > window.innerWidth - 10) left = window.innerWidth - tipRect.width - 10;
      var top = rect.bottom + 8;
      tip.classList.remove('pos-arriba');
      if (top + tipRect.height > window.innerHeight - 8) {
        top = rect.top - tipRect.height - 8;
        tip.classList.add('pos-arriba');
      }
      tip.style.left = Math.round(left) + 'px';
      tip.style.top = Math.round(top) + 'px';
    });

    document.addEventListener('mouseout', function (e) {
      var el = e.target.closest('[data-tooltip]');
      if (el) {
        tip.classList.remove('visible');
      }
    });
  }

  // Inicializar eventos de navegación y botón de guía
  document.addEventListener('DOMContentLoaded', function () {
    iniciarTooltipsGlobales();
    montarLicos();

    var btnLico = document.getElementById('btn-lico-saludo');
    if (btnLico) btnLico.addEventListener('click', function () { iniciarGuiaInteractiva(0); });

    // El botón global inicia la guía desde el primer paso de la página donde estás.
    var btnGuiaGlobal = document.getElementById('btn-guia-global');
    if (btnGuiaGlobal) {
      btnGuiaGlobal.addEventListener('click', function () {
        iniciarGuiaInteractiva(primerPasoDePaginaActual());
      });
    }

    // Si viene de otra página de la guía (?guia=1&paso=N), continúa en ese paso.
    var params = new URLSearchParams(location.search);
    if (params.get('guia') === '1') {
      var paso = parseInt(params.get('paso'), 10);
      if (isNaN(paso) || paso < 0 || paso >= PASOS_TOUR.length || PASOS_TOUR[paso].pagina !== rutaActual()) {
        paso = primerPasoDePaginaActual();
      }
      if (window.history && window.history.replaceState) {
        params.delete('guia');
        params.delete('paso');
        var qs = params.toString();
        window.history.replaceState({}, document.title, location.pathname + (qs ? '?' + qs : ''));
      }
      setTimeout(function () { iniciarGuiaInteractiva(paso); }, 300);
    }
  });

  // Exponer API global
  window.iniciarGuiaInteractiva = iniciarGuiaInteractiva;
  window.cerrarGuiaInteractiva = cerrarGuiaInteractiva;

})();
