// Shared table view controls. Original tables stay in their Blazor-owned position.
(function () {
    'use strict';
    var registros = new Map(), encabezados = new Map(), franjas = new Map(), activa = null, registroActivo = null, ancestros = [], desplazamiento = 0;
    function medirEncabezado(encabezado) {
        var tabla = encabezado.parentElement;
        if (!tabla || !encabezado.isConnected) return;
        var contenedor = encabezados.get(encabezado);
        if (contenedor) contenedor.style.setProperty('--tabla-marco-borde', getComputedStyle(contenedor).borderTopWidth);
        // CSS custom properties need layout pixels, not the zoomed rectangle.
        // Mixing those spaces scales the gutter stripe twice at fractional zoom.
        var alto = parseFloat(getComputedStyle(encabezado).height) || encabezado.offsetHeight || 0;
        var ultimaFila = encabezado.rows[encabezado.rows.length - 1];
        var celda = ultimaFila?.cells[ultimaFila.cells.length - 1];
        // At fractional zoom the used border width can be e.g. 3.63636px,
        // even though the stylesheet requests 4px. Match the rendered border.
        var franja = alto > 0 && celda ? parseFloat(getComputedStyle(celda).borderBottomWidth) || 0 : 0;
        [['--tabla-encabezado-alto', alto], ['--tabla-franja-alto', franja]].forEach(function (dato) {
            var valor = dato[1] + 'px';
            [tabla, contenedor].forEach(function (destino) {
                if (destino && destino.style.getPropertyValue(dato[0]) !== valor)
                    destino.style.setProperty(dato[0], valor);
            });
        });
        var linea = franjas.get(encabezado);
        if (!linea) return;
        var rect = encabezado.getBoundingClientRect(), escala = alto ? rect.height / alto : 1;
        var limite = contenedor ? contenedor.getBoundingClientRect() : rect;
        var borde = contenedor ? getComputedStyle(contenedor) : null;
        var izquierda = limite.left + (borde ? parseFloat(borde.borderLeftWidth) * escala : 0);
        var ancho = limite.width - (borde ? (parseFloat(borde.borderLeftWidth) + parseFloat(borde.borderRightWidth)) * escala : 0);
        var dpr = window.devicePixelRatio || 1;
        var grosor = Math.max(1, Math.round(franja * escala * dpr)) / dpr;
        var abajo = celda ? celda.getBoundingClientRect().bottom : rect.bottom;
        var arriba = Math.round((abajo - grosor) * dpr) / dpr;
        var visible = alto > 0 && rect.height > 2 && rect.width > 2 && ancho > 0 &&
            !encabezado.classList.contains('visually-hidden') && getComputedStyle(encabezado).visibility !== 'hidden' &&
            abajo > 0 && arriba < window.innerHeight &&
            (!contenedor || arriba >= limite.top && abajo <= limite.bottom);
        linea.style.display = visible ? 'block' : 'none';
        if (!visible) return;
        // A single decorative layer spans the cells AND scrollbar channel,
        // avoiding independently rasterized cyan edges at fractional scaling.
        var zoomPagina = (parseFloat(getComputedStyle(document.documentElement).zoom) || 1) *
            (parseFloat(getComputedStyle(document.body).zoom) || 1);
        linea.style.zoom = String(1 / zoomPagina);
        linea.style.left = izquierda + 'px';
        linea.style.top = arriba + 'px';
        linea.style.width = ancho + 'px';
        linea.style.height = grosor + 'px';
        var nivel = contenedor?.hasAttribute('data-tabla-ampliada') ? 2147483550 : 90;
        for (var padre = tabla.parentElement; padre && padre !== document.body; padre = padre.parentElement) {
            var z = parseInt(getComputedStyle(padre).zIndex, 10);
            if (Number.isFinite(z)) nivel = Math.max(nivel, z + 1);
        }
        linea.style.zIndex = String(nivel);
    }
    var tamanoEncabezado = new ResizeObserver(function (entradas) {
        entradas.forEach(function (entrada) { medirEncabezado(entrada.target); });
    });
    // Header presentation is independent from expansion controls: all modules,
    // standalone tables, dialogs, details and Razor Pages share the same sizing.
    function prepararEncabezados() {
        encabezados.forEach(function (contenedor, encabezado) {
            if (!encabezado.isConnected) {
                tamanoEncabezado.unobserve(encabezado);
                encabezados.delete(encabezado);
                franjas.get(encabezado)?.remove();
                franjas.delete(encabezado);
                if (contenedor && !contenedor.querySelector('table > thead')) {
                    contenedor.removeAttribute('data-tabla-marco');
                    contenedor.removeAttribute('data-franja-continua');
                    contenedor.style.removeProperty('--tabla-encabezado-alto');
                    contenedor.style.removeProperty('--tabla-franja-alto');
                    contenedor.style.removeProperty('--tabla-marco-borde');
                }
            }
        });
        document.querySelectorAll('table > thead').forEach(function (encabezado) {
            var tabla = encabezado.parentElement, padre = tabla.parentElement;
            var contenedor = null;
            if (padre) {
                var estilo = getComputedStyle(padre);
                var soloTablaConScroll = padre.children.length === 1 &&
                    (/^(auto|scroll)$/.test(estilo.overflowX) || /^(auto|scroll)$/.test(estilo.overflowY));
                if (padre.matches('.table-container, .dup-scroll, .cajas-table-wrapper, .table-responsive') || soloTablaConScroll) {
                    contenedor = padre;
                    contenedor.setAttribute('data-tabla-marco', 'true');
                }
            }
            if (!encabezados.has(encabezado)) {
                var linea = document.createElement('span');
                linea.className = 'tabla-franja-continua';
                linea.setAttribute('aria-hidden', 'true');
                document.body.appendChild(linea);
                franjas.set(encabezado, linea);
                tabla.setAttribute('data-franja-continua', 'true');
                tamanoEncabezado.observe(encabezado);
            }
            if (contenedor) contenedor.setAttribute('data-franja-continua', 'true');
            encabezados.set(encabezado, contenedor);
            medirEncabezado(encabezado);
        });
    }
    var tamanoBarra = new ResizeObserver(function () {
        if (registroActivo) document.body.style.setProperty('--tabla-barra-altura', registroActivo.barra.offsetHeight + 'px');
    });
    function capaAbierta() {
        return Array.from(document.querySelectorAll('[role="dialog"][aria-modal="true"], .modal-overlay, .case-drawer.open'))
            .some(function (el) { return el.getClientRects().length && getComputedStyle(el).visibility !== 'hidden'; });
    }
    function contexto(registro) {
        var pagina = activa.closest('.page-container') || document.querySelector('main');
        var titulo = pagina && pagina.querySelector('h1');
        var nombre = titulo ? titulo.textContent.trim() : 'Registros';
        if (registro.titulo.textContent !== nombre) registro.titulo.textContent = nombre;
        var avisos = pagina ? Array.from(pagina.querySelectorAll('.alert, .clasif-error, .aviso-clasif'))
            .filter(function (el) { return !activa.contains(el) && !registro.barra.contains(el) && el.getClientRects().length; }) : [];
        // Mirror visible feedback and its original actions, without moving Blazor-owned nodes.
        var firma = avisos.map(function (el) { return el.className + el.textContent + Array.from(el.querySelectorAll('button')).map(function (b) { return b.disabled; }).join(); }).join('|');
        if (firma === registro.firma) return;
        registro.firma = firma;
        registro.mensajes.replaceChildren();
        avisos.forEach(function (el) {
            var aviso = document.createElement('div');
            aviso.className = 'tabla-barra-mensaje' + (el.matches('.error, .clasif-error') ? ' es-error' : el.matches('.success') ? ' es-exito' : '');
            var texto = el.cloneNode(true);
            texto.querySelectorAll('button').forEach(function (b) { b.remove(); });
            aviso.appendChild(document.createTextNode(texto.textContent.trim()));
            el.querySelectorAll('button').forEach(function (original) {
                var b = document.createElement('button'); b.type = 'button'; b.className = 'btn-secondary';
                b.textContent = original.getAttribute('aria-label') || original.textContent;
                b.disabled = original.disabled;
                b.addEventListener('click', function () { if (original.isConnected) original.click(); });
                aviso.appendChild(b);
            });
            registro.mensajes.appendChild(aviso);
        });
    }
    function restaurar() {
        if (!activa) return;
        activa.removeAttribute('data-tabla-ampliada');
        activa.scrollLeft = desplazamiento;
        ancestros.forEach(function (el) { el.removeAttribute('data-tabla-ancestro'); });
        ancestros = [];
        document.body.classList.remove('tabla-pantalla-completa');
        document.body.style.removeProperty('--tabla-barra-altura');
        var registro = registroActivo;
        activa = null; registroActivo = null;
        if (registro) {
            registro.barra.removeAttribute('data-tabla-barra-ampliada');
            tamanoBarra.unobserve(registro.barra);
            registro.boton.querySelector('span').textContent = 'Ampliar tabla';
            registro.boton.setAttribute('aria-expanded', 'false');
            if (registro.boton.isConnected) registro.boton.focus({ preventScroll: true });
        }
    }
    function alternar(tabla, registro) {
        if (activa) { restaurar(); return; }
        activa = tabla.closest('.table-container, .dup-scroll, .cajas-table-wrapper, .table-responsive') || tabla;
        desplazamiento = activa.scrollLeft;
        registroActivo = registro;
        var padre = activa.parentElement;
        while (padre && padre !== document.body) {
            padre.setAttribute('data-tabla-ancestro', 'true');
            ancestros.push(padre); padre = padre.parentElement;
        }
        activa.setAttribute('data-tabla-ampliada', 'true');
        activa.scrollLeft = 0;
        document.body.classList.add('tabla-pantalla-completa');
        registro.barra.setAttribute('data-tabla-barra-ampliada', 'true');
        registro.boton.querySelector('span').textContent = 'Cerrar vista ampliada';
        registro.boton.setAttribute('aria-expanded', 'true');
        contexto(registro);
        document.body.style.setProperty('--tabla-barra-altura', registro.barra.offsetHeight + 'px');
        tamanoBarra.observe(registro.barra);
        registro.boton.focus({ preventScroll: true });
        window.dispatchEvent(new Event('resize'));
    }
    function preparar() {
        prepararEncabezados();
        registros.forEach(function (registro, tabla) {
            if (!tabla.isConnected) {
                if (registro === registroActivo) restaurar();
                registro.barra.remove(); registros.delete(tabla);
            }
        });
        document.querySelectorAll('main .table-container > table, main .dup-scroll > table, main .cajas-table-wrapper > table, main .table-responsive > table').forEach(function (tabla) {
            if (tabla.closest('[role="dialog"], .modal-overlay, .case-drawer, .listado-documento, .print-only') || tabla.hasAttribute('data-sin-ampliar')) return;
            var anterior = registros.get(tabla);
            if (anterior && anterior.barra.isConnected) return;
            var ancla = tabla.closest('.table-container, .dup-scroll, .cajas-table-wrapper, .table-responsive') || tabla;
            var barra = document.createElement('div');
            barra.className = 'tabla-barra-vista';
            barra.setAttribute('role', 'group');
            barra.setAttribute('aria-label', 'Vista de la tabla');
            var cabecera = document.createElement('div'); cabecera.className = 'tabla-barra-contexto';
            var titulo = document.createElement('strong'); titulo.className = 'tabla-barra-titulo';
            var ayuda = document.createElement('span'); ayuda.className = 'tabla-barra-ayuda'; ayuda.textContent = 'Vista ampliada · Esc para volver';
            cabecera.append(titulo, ayuda);
            var boton = document.createElement('button');
            boton.type = 'button'; boton.className = 'tabla-maximizar';
            boton.innerHTML = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 3H3v5M16 3h5v5M21 16v5h-5M8 21H3v-5"/></svg><span>Ampliar tabla</span>';
            boton.setAttribute('aria-expanded', 'false');
            boton.title = 'Ampliar esta tabla. Para volver, pulse el botón o la tecla Esc.';
            var mensajes = document.createElement('div'); mensajes.className = 'tabla-barra-mensajes'; mensajes.setAttribute('aria-live', 'polite');
            var registro = { barra: barra, boton: boton, titulo: titulo, mensajes: mensajes, firma: null };
            boton.addEventListener('click', function () { alternar(tabla, registro); });
            barra.append(cabecera, boton, mensajes);
            var herramientas = tabla.matches('.bandeja-table, .tabla-virtual') && tabla.closest('.listado-page')?.querySelector('.panel-barra-buscador');
            if (herramientas) {
                barra.classList.add('tabla-barra-integrada');
                herramientas.appendChild(barra);
            } else ancla.parentNode.insertBefore(barra, ancla);
            registros.set(tabla, registro);
        });
        if (activa && registroActivo) contexto(registroActivo);
    }
    document.addEventListener('keydown', function (e) {
        if (e.defaultPrevented || capaAbierta()) return;
        var clase = document.querySelector('details.clase-rapida[open]');
        if (clase && e.key === 'Escape') {
            e.preventDefault(); e.stopPropagation(); clase.open = false;
            var panel = clase.querySelector('.clase-rapida-panel');
            if (panel && panel.hidePopover && panel.matches(':popover-open')) panel.hidePopover();
            clase.querySelector('summary').focus(); return;
        }
        // Inline editors own Escape: let them restore data-original before blur can save.
        if (e.key === 'Escape' && e.target.closest('.celda-editable-input, [data-original]')) return;
        if (e.key === 'Escape' && activa) { e.preventDefault(); restaurar(); }
        if (e.key === 'Tab' && activa) {
            var focos = Array.from(registroActivo.barra.querySelectorAll('button:not(:disabled)')).concat(Array.from(activa.querySelectorAll('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), summary, [tabindex="0"]')))
                .filter(function (el) { return el.getClientRects().length && getComputedStyle(el).visibility !== 'hidden'; });
            if (e.shiftKey && document.activeElement === focos[0]) { e.preventDefault(); focos[focos.length - 1].focus(); }
            else if (!e.shiftKey && document.activeElement === focos[focos.length - 1]) { e.preventDefault(); focos[0].focus(); }
        }
    }, true);
    // Native Popover puts license classes above scroll containers without reparenting components.
    document.addEventListener('click', function (e) {
        var listo = e.target.closest('.clase-rapida-listo');
        if (listo && !listo.disabled) listo.closest('details').open = false;
        var summary = e.target.closest('details.clase-rapida > summary');
        if (summary && summary.getAttribute('aria-disabled') === 'true') e.preventDefault();
    }, true);
    document.addEventListener('toggle', function (e) {
        if (e.target.matches('.clase-rapida-panel') && e.newState === 'closed') {
            e.target.closest('details').open = false; return;
        }
        if (!e.target.matches('details.clase-rapida')) return;
        var details = e.target, panel = details.querySelector('.clase-rapida-panel'), summary = details.querySelector('summary');
        if (!panel) return;
        if (!details.open) {
            if (panel.hidePopover && panel.matches(':popover-open')) panel.hidePopover();
            return;
        }
        if (summary.getAttribute('aria-disabled') === 'true') { details.open = false; return; }
        var r = summary.getBoundingClientRect();
        panel.style.setProperty('--clase-left', Math.max(12, Math.min(r.left, window.innerWidth - 312)) + 'px');
        panel.style.setProperty('--clase-top', (r.bottom + 8) + 'px');
        if (panel.showPopover) panel.showPopover();
        var h = panel.getBoundingClientRect().height;
        if (r.bottom + h + 20 > window.innerHeight) panel.style.setProperty('--clase-top', Math.max(12, r.top - h - 8) + 'px');
        panel.querySelector('input:not(:disabled)')?.focus({ preventScroll: true });
    }, true);
    function cerrarClases() {
        document.querySelectorAll('details.clase-rapida[open]').forEach(function (d) { d.open = false; });
    }
    window.addEventListener('resize', function () {
        cerrarClases();
        encabezados.forEach(function (_, encabezado) { medirEncabezado(encabezado); });
    });
    document.addEventListener('scroll', function (e) {
        if (!e.target.closest || !e.target.closest('.clase-rapida-panel')) cerrarClases();
        programar();
    }, true);
    var pendiente = false;
    function programar() {
        if (pendiente) return;
        pendiente = true;
        requestAnimationFrame(function () { pendiente = false; preparar(); });
    }
    function iniciar() {
        preparar();
        new MutationObserver(programar).observe(document.body, { childList: true, subtree: true });
        new MutationObserver(programar).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'data-design', 'data-rows'] });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', iniciar);
    else iniciar();
})();
