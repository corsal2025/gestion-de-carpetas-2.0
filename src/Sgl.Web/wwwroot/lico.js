/* Lico — mascota "licencia de conducir" (SVG generado por código, sin imágenes).
 * Poses: 'saluda' | 'explica' | 'alerta' | 'celebra'
 *
 * Uso declarativo (recomendado):
 *   <span data-lico="saluda"></span>     -> inserta a Lico en ese elemento
 * Uso por código:
 *   elemento.innerHTML = Lico.svg('celebra');
 * Las animaciones (rebote, parpadeo, saludo) viven en tema-lico.css (.lico-svg ...).
 */
(function () {
  'use strict';

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
        '<rect x="44" y="138" width="60" height="7" rx="3.5" fill="#d9dcee"/><rect x="44" y="151" width="40" height="7" rx="3.5" fill="#d9dcee"/>' +
        '<rect x="110" y="140" width="26" height="18" rx="4" fill="#f6d23c" stroke="#c9a400" stroke-width="2"/>' +
        der + extra +
      '</g></svg>';
  }

  function montarLicos() {
    var els = document.querySelectorAll('[data-lico]');
    for (var i = 0; i < els.length; i++) {
      if (!els[i].querySelector('svg')) {
        els[i].insertAdjacentHTML('afterbegin', licoSvg(els[i].getAttribute('data-lico')));
      }
    }
  }

  window.Lico = { svg: licoSvg, mount: montarLicos };
  document.addEventListener('DOMContentLoaded', montarLicos);

  // Blazor Server vuelve a renderizar el DOM (navegacion, circuito interactivo):
  // se repinta a Lico cuando aparece un [data-lico] sin SVG. Idempotente y con
  // rAF para no trabajar mas de una vez por frame (insertar el SVG dispara el
  // observer otra vez, pero la segunda pasada no encuentra nada que montar).
  var pendiente = false;
  function programar() {
    if (pendiente) return;
    pendiente = true;
    requestAnimationFrame(function () { pendiente = false; montarLicos(); });
  }
  if (window.MutationObserver) {
    new MutationObserver(programar).observe(document.body || document.documentElement, { childList: true, subtree: true });
  }
})();
