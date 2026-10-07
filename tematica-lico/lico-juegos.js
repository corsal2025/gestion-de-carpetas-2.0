/* Lico travieso — si pasa mucho rato sin actividad, Lico hace travesuras en pantalla:
 *   'toc'    -> se acerca y golpea la pantalla ("¡Toc, toc! ¿Sigues ahí?")
 *   'pelota' -> juega a la pelota y grita gol
 *   'duerme' -> se queda dormido (Zzz)
 *   'domina' -> mantiene la pelota en el aire sobre la cabeza
 * Cualquier actividad (mouse, teclado, clic, scroll, toque) lo hace desaparecer al instante.
 *
 * Requiere window.Lico.svg(pose) (lico.js o guia-interactiva.js) y los estilos .lico-travieso (theme.css).
 * Tiempo de inactividad: define window.LICO_IDLE_MS (ms) ANTES de cargar este archivo. Por defecto 90 000 (90 s).
 * Desactivar: window.LICO_JUEGOS = false antes de cargarlo.
 * Clic en el Lico del encabezado (.head-lico): hace una gracia en su sitio (salta, gira, baila, toc, duerme, pelota, domina la pelota).
 * Prueba manual: LicoJuegos.jugar('toc' | 'pelota' | 'duerme' | 'domina')
 *
 * Es solo decorativo: no recibe clics (pointer-events: none), no aparece con la guía abierta,
 * con un cuadro de diálogo abierto, con la pestaña oculta ni si el sistema pide reducir movimiento.
 */
(function () {
  'use strict';

  if (window.LICO_JUEGOS === false) return;

  var IDLE_MS = (window.LICO_IDLE_MS | 0) || 90000;
  var JUEGOS = {
    toc:    { pose: 'saluda',  ms: 7000,  texto: '¡Toc, toc! ¿Sigues ahí?' },
    pelota: { pose: 'celebra', ms: 9500,  texto: '¡Gooool!' },
    duerme: { pose: 'saluda',  ms: 16000, texto: 'Zzz… muévete para despertarme' },
    domina: { pose: 'saluda',  ms: 11000, texto: '¡Mira, no se me cae!' }
  };
  var IDS = Object.keys(JUEGOS);

  var el = null;
  var timerIdle = null;
  var timerFin = null;
  var timerLimpiar = null;
  var jugando = false;
  var inicioMs = 0;
  var manual = false;   // lanzada a mano (prueba): el mouse no la espanta, solo clic o tecla
  var ultimo = null;

  function reducido() {
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  }

  function puedeAparecer() {
    if (document.hidden) return false;
    if (reducido()) return false;
    if (!window.Lico || typeof window.Lico.svg !== 'function') return false;
    if (document.getElementById('tour-overlay')) return false;              // guía abierta
    if (document.querySelector('.modal-overlay, .pdf-modal-overlay')) return false; // cuadro de diálogo abierto
    return true;
  }

  // Ojos cerrados, boquita y gorro de dormir: se añaden al SVG de Lico y solo se ven mientras duerme (CSS).
  function ponerSueno(contenedor) {
    var cuerpo = contenedor && contenedor.querySelector('.lico-cuerpo');
    if (!cuerpo || cuerpo.querySelector('.lico-sueno')) return;
    cuerpo.insertAdjacentHTML('beforeend', '<g class="lico-sueno"><ellipse cx="90" cy="117" rx="25" ry="17" fill="#fff"/><path d="M57 92Q66 101 75 92M105 92Q114 101 123 92" stroke="#23262a" stroke-width="4.5" fill="none" stroke-linecap="round"/><ellipse cx="90" cy="114" rx="6" ry="5" fill="#e8503f" stroke="#23262a" stroke-width="2.5"/><path d="M30 24Q44 -2 98 1Q132 3 152 24Z" fill="#6d5bd0" stroke="#26388c" stroke-width="3" stroke-linejoin="round"/><circle cx="70" cy="12" r="3" fill="#f6d23c"/><circle cx="104" cy="10" r="3" fill="#f6d23c"/><rect x="24" y="20" width="132" height="13" rx="6.5" fill="#fff" stroke="#26388c" stroke-width="3"/><circle cx="156" cy="34" r="9" fill="#fff" stroke="#26388c" stroke-width="3"/></g>');
  }

  function crear() {
    if (el) return;
    el = document.createElement('div');
    el.className = 'lico-travieso';
    el.setAttribute('aria-hidden', 'true');
    el.innerHTML =
      '<div class="lt-ondas"><span class="lt-onda"></span><span class="lt-onda"></span><span class="lt-onda"></span></div>' +
      '<div class="lt-pelota"><span class="lt-pelota-i"></span></div>' +
      '<div class="lt-lico">' +
        '<div class="lt-globo"></div>' +
        '<div class="lt-zzz"><b>z</b><b>z</b><b>Z</b></div>' +
        '<div class="lt-cuerpo"></div>' +
      '</div>';
    document.body.appendChild(el);
  }

  function programar(ms) {
    clearTimeout(timerIdle);
    timerIdle = setTimeout(jugarAleatorio, ms || IDLE_MS);
  }

  function jugarAleatorio() {
    var candidatos = IDS.filter(function (id) { return id !== ultimo; });
    jugar(candidatos[Math.floor(Math.random() * candidatos.length)]);
  }

  function jugar(id, esManual) {
    var juego = JUEGOS[id];
    if (!juego || jugando) return;
    if (!puedeAparecer()) { programar(); return; }

    crear();
    clearTimeout(timerLimpiar);
    ultimo = id;
    jugando = true;
    manual = !!esManual;
    inicioMs = Date.now();

    el.querySelector('.lt-cuerpo').innerHTML = window.Lico.svg(juego.pose);
    ponerSueno(el.querySelector('.lt-cuerpo'));
    el.querySelector('.lt-globo').textContent = juego.texto;
    el.className = 'lico-travieso modo-' + id;
    void el.offsetWidth;                       // reinicia las animaciones CSS
    el.classList.add('activo');

    clearTimeout(timerFin);
    timerFin = setTimeout(function () { terminar(false); }, juego.ms);
  }

  function terminar(porActividad) {
    clearTimeout(timerFin);
    if (!jugando) return;
    jugando = false;
    if (el) el.classList.add('saliendo');
    timerLimpiar = setTimeout(function () {
      if (el) el.className = 'lico-travieso';
    }, 320);
    // Si se fue sola, la próxima travesura tarda el doble; si la ahuyentó el usuario, el tiempo normal.
    programar(porActividad ? IDLE_MS : IDLE_MS * 2);
  }

  function alHaberActividad(e) {
    // Ignora los primeros 400 ms: el propio temblor del mouse al soltarlo no debe espantarla.
    if (jugando && manual && e && /^(mousemove|wheel|scroll)$/.test(e.type)) {
      return;                                  // en prueba manual solo clic o tecla la despiden
    }
    if (jugando && Date.now() - inicioMs > 400) {
      terminar(true);
    } else if (!jugando) {
      programar();
    }
  }

  ['mousemove', 'mousedown', 'keydown', 'wheel', 'scroll', 'touchstart', 'click'].forEach(function (ev) {
    window.addEventListener(ev, alHaberActividad, { passive: true, capture: true });
  });

  document.addEventListener('visibilitychange', function () {
    if (document.hidden) { if (jugando) terminar(true); } else { programar(); }
  });


  // ---- Clic sobre el Lico del encabezado: él mismo hace una gracia (en vez de abrir la guía) ----
  var GRACIAS = [
    { cls: 'hl-salta',  ms: 1400, texto: '¡Wiii! ¡Mira cómo salto!' },
    { cls: 'hl-gira',   ms: 1500, texto: '¡Ta-dá! ¡Giro completo!' },
    { cls: 'hl-baila',  ms: 2100, texto: '¡A bailar se ha dicho!' },
    { cls: 'hl-toc',    ms: 2200, texto: '¡Toc, toc! ¿Me ves?' },
    { cls: 'hl-duerme', ms: 3100, texto: 'Zzz… cinco minutitos más' },
    { cls: 'hl-pelota', ms: 2600, texto: '¡Gooool!' },
    { cls: 'hl-domina', ms: 3100, texto: '¡Mira, no se me cae!' }
  ];
  var ultimaGracia = -1;
  var timerGracia = null;

  function graciaLico(boton) {
    var burbuja = boton.querySelector('.head-lico-burbuja');
    if (burbuja && boton.getAttribute('data-hl-orig') === null) boton.setAttribute('data-hl-orig', burbuja.textContent);
    var i;
    do { i = Math.floor(Math.random() * GRACIAS.length); } while (i === ultimaGracia);
    ultimaGracia = i;
    var g = GRACIAS[i];

    // Idempotente: si Blazor repintó el botón, se vuelven a crear la pelota y el gorro de dormir.
    if (!boton.querySelector('.hl-bola')) {
      var bola = document.createElement('span');
      bola.className = 'hl-bola';
      bola.setAttribute('aria-hidden', 'true');
      boton.appendChild(bola);
    }
    ponerSueno(boton);
    boton.classList.add('hl-activa');
    clearTimeout(timerGracia);
    GRACIAS.forEach(function (x) { boton.classList.remove(x.cls); });
    void boton.offsetWidth;                    // reinicia la animación aunque se repita
    boton.classList.add(g.cls);
    if (burbuja) burbuja.textContent = g.texto;
    timerGracia = setTimeout(function () {
      if (!boton.isConnected) boton = document.querySelector('.head-lico') || boton;
      burbuja = boton.querySelector('.head-lico-burbuja');
      boton.classList.remove(g.cls);
      boton.classList.remove('hl-activa');
      if (burbuja) burbuja.textContent = boton.getAttribute('data-hl-orig') || burbuja.textContent;
    }, g.ms);
  }

  // En captura y con stopPropagation: la gracia reemplaza al clic que abría la guía (sigue el botón "Cómo usar el sistema", #btn-guia-global).
  document.addEventListener('click', function (e) {
    var b = e.target && e.target.closest ? e.target.closest('.head-lico') : null;
    if (!b) return;
    e.preventDefault();
    e.stopPropagation();
    graciaLico(b);
  }, true);

  window.LicoJuegos = {
    jugar: function (id) { clearTimeout(timerIdle); jugar(id, true); },
    terminar: function () { terminar(true); }
  };

  programar();
})();
