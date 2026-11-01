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
    var INK = '#203c59', AZ = '#345e86';
    var id = 'lc' + (++licoSeq) + '-';
    var url = function (n) { return 'url(#' + id + n + ')'; };
    // SMIL cannot be switched off from CSS, so animated nodes are simply omitted for reduced motion.
    var reduce = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    var anim = function (xml) { return reduce ? '' : xml; };

    var down = 'M24 104Q6 120 12 142';
    var P = {
      saluda:  { armL: down, hxL: 12, hyL: 144, armR: 'M156 100Q184 88 180 60', hxR: 180, hyR: 56, waving: true,
                 mouth: 'M75 111Q90 130 105 111', fill: 'none', tongue: '',
                 brows: 'M54 76q11-7 22-1M104 75q11-6 22 1', ex: [66, 114], happy: false },
      explica: { armL: down, hxL: 12, hyL: 144, armR: 'M156 104Q190 108 204 90', hxR: 206, hyR: 86, waving: false,
                 mouth: 'M77 112Q91 126 105 111', fill: 'none', tongue: '',
                 brows: 'M56 77q10-5 20 0M104 77q10-5 20 0', ex: [69, 117], happy: false },
      alerta:  { armL: down, hxL: 12, hyL: 144, armR: 'M156 100Q184 88 184 62', hxR: 184, hyR: 58, waving: false,
                 mouth: 'M72 121q9-9 18 0t18 0', fill: 'none', tongue: '',
                 brows: 'M55 78q11-9 22-1M104 77q11-8 22 1', ex: [66, 114], happy: false },
      celebra: { armL: 'M24 104Q-2 90 6 54', hxL: 6, hyL: 50, armR: 'M156 104Q182 90 174 54', hxR: 174, hyR: 50, waving: false,
                 mouth: 'M72 109Q90 115 108 109Q104 130 90 130Q76 130 72 109Z', fill: '#294964', tongue: 'M80 126q10-7 20 0q-10 5-20 0z',
                 brows: '', ex: [66, 114], happy: true }
    };
    var c = P[pose] || P.saluda;
    var isAlerta = pose === 'alerta', isExplica = pose === 'explica', isCelebra = pose === 'celebra';

    var brazo = function (d) {
      return '<path d="' + d + '" stroke="#294d70" stroke-width="10" fill="none" stroke-linecap="round"/>' +
        '<path d="' + d + '" stroke="#7298bc" stroke-width="5" fill="none" stroke-linecap="round"/>';
    };
    var mano = function (x, y) {
      return '<g transform="translate(' + x + ' ' + y + ')">' +
        '<path d="M-9 4V-6q0-5 3-3l2 7v-11q1-4 4-2l1 12 2-11q2-3 4 0L6 0l3-7q3-2 4 1L8 8q-3 7-10 6-8-1-7-10Z" fill="' + url('glove') + '" stroke="#7190ac" stroke-width="1.5" stroke-linejoin="round"/>' +
        '<path d="M-5 4q5-4 10 0M-3 9h5" stroke="#d0dce8" stroke-width="1.3" stroke-linecap="round"/></g>';
    };
    var grad = function (n, x2, y2, stops) {
      return '<linearGradient id="' + id + n + '" x1="0" y1="0" x2="' + x2 + '" y2="' + y2 + '">' + stops + '</linearGradient>';
    };
    var stop = function (o, col, op) {
      return '<stop offset="' + o + '" stop-color="' + col + '"' + (op === undefined ? '' : ' stop-opacity="' + op + '"') + '/>';
    };
    var defs = '<defs>' +
      grad('card', 1, 1, stop(0, '#ffffff') + stop(.55, '#f3f8fe') + stop(1, '#cfdeed')) +
      grad('head', 1, 1, stop(0, '#456e99') + stop(1, '#1a395a')) +
      grad('leg', 1, 0, stop(0, '#244865') + stop(.5, '#7294b1') + stop(1, '#335877')) +
      grad('shoe', 0, 1, stop(0, '#527697') + stop(1, '#244563')) +
      grad('glove', 1, 1, stop(0, '#ffffff') + stop(.55, '#f6f9fc') + stop(1, '#c0d1e0')) +
      grad('chip', 1, 1, stop(0, '#f3dfaa') + stop(.5, '#d5b875') + stop(1, '#b08b44')) +
      grad('holo', 1, 0, stop(0, '#ffffff', 0) + stop(0.5, '#ffffff', 0.35) + stop(1, '#ffffff', 0)) +
      '<clipPath id="' + id + 'clip"><rect x="20" y="20" width="140" height="150" rx="24"/></clipPath>' +
      '</defs>';

    var eyes;
    if (c.happy) {
      eyes = '<path d="M57 97q9-13 18 0M105 97q9-13 18 0" stroke="' + INK + '" stroke-width="3.8" fill="none" stroke-linecap="round"/>';
    } else {
      var x1 = c.ex[0], x2 = c.ex[1];
      eyes = '<g class="lico-ojos">' +
        '<ellipse cx="' + x1 + '" cy="93" rx="8.5" ry="10.5" fill="' + INK + '"/><ellipse cx="' + x2 + '" cy="93" rx="8.5" ry="10.5" fill="' + INK + '"/>' +
        '<circle cx="' + (x1 - 2) + '" cy="89" r="3" fill="#fff"/><circle cx="' + (x2 - 2) + '" cy="89" r="3" fill="#fff"/>' +
        '<circle cx="' + (x1 + 3) + '" cy="97" r="1.3" fill="#9dc2e2"/><circle cx="' + (x2 + 3) + '" cy="97" r="1.3" fill="#9dc2e2"/>' +
        '</g>';
    }
    var brows = c.brows ? '<path d="' + c.brows + '" stroke="#436380" stroke-width="2.8" stroke-linecap="round" fill="none"/>' : '';
    var mouth = '<path d="' + c.mouth + '" fill="' + c.fill + '" stroke="' + INK + '" stroke-width="3.2" stroke-linejoin="round" stroke-linecap="round"/>' +
      (isCelebra ? '<path d="M77 112q13 5 26 0l-2 5q-11 4-22 0Z" fill="#fff"/>' : '') +
      (c.tongue ? '<path d="' + c.tongue + '" fill="#c68389"/>' : '');

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
      '<ellipse cx="91" cy="210" rx="61" ry="8" fill="#284761" opacity=".07"/>' +
      '<ellipse cx="91" cy="210" rx="43" ry="4" fill="#284761" opacity=".1"/>' +
      '<g class="lico-cuerpo">' +
        '<rect x="52" y="160" width="14" height="32" rx="7" fill="' + url('leg') + '"/><rect x="104" y="160" width="14" height="32" rx="7" fill="' + url('leg') + '"/>' +
        '<ellipse cx="58" cy="196" rx="18" ry="10" fill="' + url('shoe') + '" stroke="#294b68" stroke-width="1.8"/>' +
        '<ellipse cx="112" cy="196" rx="18" ry="10" fill="' + url('shoe') + '" stroke="#294b68" stroke-width="1.8"/>' +
        '<path d="M42 200q16 7 32 0M96 200q16 7 32 0" stroke="#b6c9d9" stroke-width="3" fill="none" stroke-linecap="round"/>' +
        '<path d="M48 192q9-5 18-1M102 192q9-5 18-1" stroke="#8cacc6" stroke-width="1.7" fill="none" stroke-linecap="round"/>' +
        brazo(c.armL) + mano(c.hxL, c.hyL) +
        '<rect x="21" y="24" width="140" height="150" rx="22" fill="#6888a6" opacity=".55"/>' +
        '<rect x="20" y="20" width="140" height="150" rx="22" fill="' + url('card') + '" stroke="#7693ad" stroke-width="1.8"/>' +
        '<path d="M22.5 60V46a21.5 21.5 0 0 1 21.5-21.5h92a21.5 21.5 0 0 1 21.5 21.5V60z" fill="' + url('head') + '"/>' +
        '<rect x="33" y="33" width="21" height="18" rx="4" fill="#fff" opacity=".15"/>' +
        '<path d="M37 46V40l7-4 6 4v6M40 46v-5h7v5M36 47h15" stroke="#e4f0fb" stroke-width="1.4" fill="none" stroke-linejoin="round"/>' +
        '<text x="63" y="47" font-family="Segoe UI,Arial,sans-serif" font-size="12" font-weight="600" letter-spacing="1.8" fill="#f3f8ff">LICO</text>' +
        '<path d="M121 39h21M127 46h15" stroke="#b8d0e4" stroke-width="2" stroke-linecap="round" opacity=".8"/>' +
        '<path d="M30 30q60-12 120 0" stroke="#fff" stroke-width="2" fill="none" opacity=".18" stroke-linecap="round"/>' +
        '<rect x="28" y="65" width="124" height="69" rx="19" fill="#fff" opacity=".55"/>' +
        '<path d="M24 145V45q0-21 21-21h90" stroke="#fff" stroke-width="1.7" fill="none" opacity=".65"/>' +
        shine + brows + eyes +
        '<ellipse cx="48" cy="112" rx="9" ry="5" fill="#d49498" opacity=".2"/><ellipse cx="132" cy="112" rx="9" ry="5" fill="#d49498" opacity=".2"/>' +
        mouth +
        '<rect x="40" y="143" width="58" height="4" rx="2" fill="#a9bed1"/><rect x="40" y="153" width="39" height="4" rx="2" fill="#c0d0de"/>' +
        '<rect x="113" y="141" width="25" height="19" rx="4" fill="' + url('chip') + '" stroke="#aa8c52" stroke-width="1"/>' +
        '<path d="M113 150.5h25M125.5 141v19M119 141v5h13v-5M119 160v-5h13v5" stroke="#a78b54" stroke-width=".9"/>' +
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
