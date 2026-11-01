// Shared table view controls. Original tables stay in their Blazor-owned position.
(function () {
    'use strict';
    var registros = new Map(), activa = null, registroActivo = null, ancestros = [], desplazamiento = 0;
    function restaurar() {
        if (!activa) return;
        activa.removeAttribute('data-tabla-ampliada');
        activa.scrollLeft = desplazamiento;
        ancestros.forEach(function (el) { el.removeAttribute('data-tabla-ancestro'); });
        ancestros = [];
        document.body.classList.remove('tabla-pantalla-completa');
        var registro = registroActivo;
        activa = null; registroActivo = null;
        if (registro) {
            registro.barra.removeAttribute('data-tabla-barra-ampliada');
            registro.boton.textContent = 'Maximizar tabla';
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
        registro.boton.textContent = 'Volver al tamaño normal';
        registro.boton.setAttribute('aria-expanded', 'true');
        registro.boton.focus({ preventScroll: true });
        window.dispatchEvent(new Event('resize'));
    }
    function preparar() {
        registros.forEach(function (registro, tabla) {
            if (!tabla.isConnected) {
                if (registro === registroActivo) restaurar();
                registro.barra.remove(); registros.delete(tabla);
            }
        });
        document.querySelectorAll('main table').forEach(function (tabla) {
            var anterior = registros.get(tabla);
            if (anterior && anterior.barra.isConnected) return;
            var ancla = tabla.closest('.table-container, .dup-scroll, .cajas-table-wrapper, .table-responsive') || tabla;
            var barra = document.createElement('div');
            barra.className = 'tabla-barra-vista';
            barra.setAttribute('role', 'group');
            barra.setAttribute('aria-label', 'Vista de la tabla');
            var boton = document.createElement('button');
            boton.type = 'button'; boton.className = 'tabla-maximizar';
            boton.textContent = 'Maximizar tabla';
            boton.setAttribute('aria-expanded', 'false');
            boton.title = 'Ampliar esta tabla. Para volver, pulse el botón o la tecla Esc.';
            var registro = { barra: barra, boton: boton };
            boton.addEventListener('click', function () { alternar(tabla, registro); });
            barra.appendChild(boton);
            ancla.parentNode.insertBefore(barra, ancla);
            registros.set(tabla, registro);
        });
    }
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && activa) { e.preventDefault(); restaurar(); }
        if (e.key === 'Tab' && activa) {
            var focos = [registroActivo.boton].concat(Array.from(activa.querySelectorAll('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), summary, [tabindex="0"]')))
                .filter(function (el) { return el.getClientRects().length && getComputedStyle(el).visibility !== 'hidden'; });
            if (e.shiftKey && document.activeElement === focos[0]) { e.preventDefault(); focos[focos.length - 1].focus(); }
            else if (!e.shiftKey && document.activeElement === focos[focos.length - 1]) { e.preventDefault(); focos[0].focus(); }
        }
    });
    var pendiente = false;
    function programar() {
        if (pendiente) return;
        pendiente = true;
        requestAnimationFrame(function () { pendiente = false; preparar(); });
    }
    function iniciar() { preparar(); new MutationObserver(programar).observe(document.body, { childList: true, subtree: true }); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', iniciar);
    else iniciar();
})();
