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

  var licoSeq = 0; // unique gradient ids per instance (an instance inside a display:none ancestor would break shared ids)

  function licoSvg(pose) {
    var INK = '#23262a', AZ = '#26388c';
    var id = 'lc' + (++licoSeq) + '-';
    var url = function (n) { return 'url(#' + id + n + ')'; };
    // SMIL cannot be switched off from CSS, so animated nodes are simply omitted for reduced motion.
    var reduce = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    var anim = function (xml) { return reduce ? '' : xml; };

    var down = 'M24 104Q6 120 12 142';
    var P = {
      saluda:  { armL: down, hxL: 12, hyL: 144, armR: 'M156 100Q184 88 180 60', hxR: 180, hyR: 56, waving: true,
                 mouth: 'M72 108Q90 136 108 108Z', fill: '#e8503f', tongue: 'M80 123q10 8 20 0q-10-7-20 0z',
                 brows: 'M54 76q11-7 22-1M104 75q11-6 22 1', ex: [66, 114], happy: false },
      explica: { armL: down, hxL: 12, hyL: 144, armR: 'M156 104Q190 108 204 90', hxR: 206, hyR: 86, waving: false,
                 mouth: 'M74 108Q90 126 106 108Z', fill: '#e8503f', tongue: 'M82 118q8 5 16 0q-8-4-16 0z',
                 brows: 'M56 77q10-5 20 0M104 77q10-5 20 0', ex: [69, 117], happy: false },
      alerta:  { armL: down, hxL: 12, hyL: 144, armR: 'M156 100Q184 88 184 62', hxR: 184, hyR: 58, waving: false,
                 mouth: 'M72 121q9-9 18 0t18 0', fill: 'none', tongue: '',
                 brows: 'M54 74l24 8M126 74l-24 8', ex: [66, 114], happy: false },
      celebra: { armL: 'M24 104Q-2 90 6 54', hxL: 6, hyL: 50, armR: 'M156 104Q182 90 174 54', hxR: 174, hyR: 50, waving: false,
                 mouth: 'M68 106Q90 146 112 106Z', fill: '#e8503f', tongue: 'M78 126q12 10 24 0q-12-8-24 0z',
                 brows: '', ex: [66, 114], happy: true }
    };
    var c = P[pose] || P.saluda;
    var isAlerta = pose === 'alerta', isExplica = pose === 'explica', isCelebra = pose === 'celebra';

    var brazo = function (d) {
      return '<path d="' + d + '" stroke="' + AZ + '" stroke-width="9" fill="none" stroke-linecap="round"/>';
    };
    var mano = function (x, y) {
      return '<circle cx="' + x + '" cy="' + y + '" r="9" fill="#fff" stroke="' + AZ + '" stroke-width="3.5"/>';
    };
    var grad = function (n, x2, y2, stops) {
      return '<linearGradient id="' + id + n + '" x1="0" y1="0" x2="' + x2 + '" y2="' + y2 + '">' + stops + '</linearGradient>';
    };
    var stop = function (o, col, op) {
      return '<stop offset="' + o + '" stop-color="' + col + '"' + (op === undefined ? '' : ' stop-opacity="' + op + '"') + '/>';
    };
    var defs = '<defs>' +
      grad('card', 1, 1, stop(0, '#ffffff') + stop(1, '#e3e9fb')) +
      grad('head', 1, 0, stop(0, '#1d2c75') + stop(1, '#3550c8')) +
      grad('leg', 1, 0, stop(0, AZ) + stop(1, '#3a55cf')) +
      grad('shoe', 0, 1, stop(0, '#ffe680') + stop(1, '#f0b90b')) +
      grad('chip', 1, 1, stop(0, '#ffe680') + stop(0.5, '#f6d23c') + stop(1, '#d9a800')) +
      grad('holo', 1, 0, stop(0, '#ffffff', 0) + stop(0.5, '#ffffff', 0.75) + stop(1, '#ffffff', 0)) +
      '<clipPath id="' + id + 'clip"><rect x="20" y="20" width="140" height="150" rx="24"/></clipPath>' +
      '</defs>';

    var eyes;
    if (c.happy) {
      eyes = '<path d="M57 98q9-15 18 0M105 98q9-15 18 0" stroke="' + INK + '" stroke-width="4.5" fill="none" stroke-linecap="round"/>';
    } else {
      var x1 = c.ex[0], x2 = c.ex[1];
      eyes = '<g class="lico-ojos">' +
        '<circle cx="' + x1 + '" cy="92" r="9" fill="' + INK + '"/><circle cx="' + x2 + '" cy="92" r="9" fill="' + INK + '"/>' +
        '<circle cx="' + (x1 + 3) + '" cy="89" r="3" fill="#fff"/><circle cx="' + (x2 + 3) + '" cy="89" r="3" fill="#fff"/>' +
        '<circle cx="' + (x1 - 3) + '" cy="95" r="1.4" fill="#fff" opacity=".8"/><circle cx="' + (x2 - 3) + '" cy="95" r="1.4" fill="#fff" opacity=".8"/>' +
        '</g>';
    }
    var brows = c.brows ? '<path d="' + c.brows + '" stroke="' + INK + '" stroke-width="4" stroke-linecap="round" fill="none"/>' : '';
    var mouth = '<path d="' + c.mouth + '" fill="' + c.fill + '" stroke="' + INK + '" stroke-width="3.2" stroke-linejoin="round" stroke-linecap="round"/>' +
      (c.tongue ? '<path d="' + c.tongue + '" fill="#ff8a95"/>' : '');

    var armR = brazo(c.armR) + mano(c.hxR, c.hyR);
    if (c.waving) armR = '<g class="lico-saludo">' + armR + '</g>';

    var float = function (vals, dur) {
      return anim('<animateTransform attributeName="transform" type="translate" values="' + vals + '" dur="' + dur + 's" repeatCount="indefinite"/>');
    };
    var extra = '';
    if (isCelebra) {
      extra = '<g>' +
        '<rect x="28" y="2" width="8" height="14" rx="2" fill="#1fb5d9">' + float('0 -4;0 12;0 -4', 1.3) + '</rect>' +
        '<rect x="186" y="26" width="8" height="14" rx="2" fill="#e889c4">' + float('0 6;0 -8;0 6', 1.1) + '</rect>' +
        '<rect x="88" y="-8" width="8" height="14" rx="2" fill="#f6d23c">' + float('0 0;0 12;0 0', 1.5) + '</rect>' +
        '<circle cx="206" cy="84" r="5" fill="#2fb56a">' + float('0 -6;0 8;0 -6', 1.2) + '</circle>' +
        '<circle cx="8" cy="16" r="5" fill="#e8503f">' + float('0 6;0 -6;0 6', 1.4) + '</circle></g>';
    } else if (isAlerta) {
      extra = '<path d="M164 62q8 12 0 18q-8-6 0-18z" fill="#1fb5d9"/>' +
        '<circle cx="198" cy="30" r="18" fill="#fff" stroke="#e8503f" stroke-width="5"/>' +
        '<path d="M198 20v11l8 5" stroke="#e8503f" stroke-width="4" fill="none" stroke-linecap="round"/>';
    } else if (isExplica) {
      extra = '<circle cx="196" cy="26" r="12" fill="#ffe680" stroke="#c9a400" stroke-width="3">' +
        anim('<animate attributeName="opacity" values="1;.7;1" dur="1.6s" repeatCount="indefinite"/>') + '</circle>' +
        '<rect x="191" y="36" width="10" height="6" rx="2" fill="#9aa0bd"/>' +
        '<path d="M196 4v5M214 12l-4 4M218 28h-5M178 12l4 4M174 28h5" stroke="#f0b90b" stroke-width="3" stroke-linecap="round"/>';
    }
    if (!isAlerta && !isCelebra) {
      extra += '<path d="M6 44l3 7 7 3-7 3-3 7-3-7-7-3 7-3z" fill="#f6d23c" opacity=".6">' +
        anim('<animate attributeName="opacity" values=".2;1;.2" dur="2.2s" repeatCount="indefinite"/>') + '</path>';
    }

    var shine = anim('<g clip-path="' + url('clip') + '"><rect x="-90" y="10" width="46" height="190" fill="' + url('holo') + '" transform="skewX(-18)">' +
      '<animate attributeName="x" values="-90;230;230" keyTimes="0;.32;1" dur="5.5s" repeatCount="indefinite"/></rect></g>');
    var dot = function (cx, col) {
      return '<circle cx="' + cx + '" cy="42" r="9" fill="' + col + '" opacity=".3">' +
        anim('<animate attributeName="opacity" values=".18;.4;.18" dur="2.4s" repeatCount="indefinite"/>') + '</circle>' +
        '<circle cx="' + cx + '" cy="42" r="5" fill="' + col + '"/>';
    };

    return '<svg class="lico-svg" viewBox="-10 -14 240 252" role="img" aria-label="Lico, la mascota de licencias de conducir">' +
      defs +
      '<ellipse cx="90" cy="216" rx="54" ry="7" fill="rgba(27,33,80,.22)"/>' +
      '<g class="lico-cuerpo">' +
        '<rect x="52" y="160" width="14" height="32" rx="7" fill="' + url('leg') + '"/><rect x="104" y="160" width="14" height="32" rx="7" fill="' + url('leg') + '"/>' +
        '<ellipse cx="58" cy="196" rx="17" ry="9.5" fill="' + url('shoe') + '" stroke="' + AZ + '" stroke-width="3.5"/>' +
        '<ellipse cx="112" cy="196" rx="17" ry="9.5" fill="' + url('shoe') + '" stroke="' + AZ + '" stroke-width="3.5"/>' +
        '<path d="M46 197q12 4 24 0M100 197q12 4 24 0" stroke="#fff" stroke-width="2" fill="none" opacity=".8" stroke-linecap="round"/>' +
        brazo(c.armL) + mano(c.hxL, c.hyL) +
        '<rect x="20" y="20" width="140" height="150" rx="24" fill="' + url('card') + '" stroke="' + AZ + '" stroke-width="5"/>' +
        '<path d="M22.5 60V46a21.5 21.5 0 0 1 21.5-21.5h92a21.5 21.5 0 0 1 21.5 21.5V60z" fill="' + url('head') + '"/>' +
        dot(44, '#1fb5d9') + dot(58, '#2fb56a') + dot(72, '#e889c4') + dot(86, '#f6d23c') +
        '<rect x="110" y="38" width="34" height="8" rx="4" fill="#fff" opacity=".9"/>' +
        '<path d="M30 30q60-12 120 0" stroke="#fff" stroke-width="2" fill="none" opacity=".18" stroke-linecap="round"/>' +
        shine + brows + eyes +
        '<ellipse cx="48" cy="114" rx="10" ry="6.5" fill="#ff8fc1" opacity=".6"/><ellipse cx="132" cy="114" rx="10" ry="6.5" fill="#ff8fc1" opacity=".6"/>' +
        mouth +
        '<rect x="44" y="140" width="58" height="7" rx="3.5" fill="#cfd5ee"/><rect x="44" y="153" width="38" height="7" rx="3.5" fill="#cfd5ee"/>' +
        '<rect x="110" y="140" width="26" height="19" rx="4" fill="' + url('chip') + '" stroke="#b8920a" stroke-width="2"/>' +
        '<path d="M110 149.5h26M123 140v19" stroke="#b8920a" stroke-width="1.2" opacity=".6"/>' +
        armR + extra +
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
