/**
 * Gestión de Carpetas 2.0 - Utilidades de RUT y Portapapeles para CAS Chile
 */

// Formato de autocompletado en tiempo real para inputs de RUT (puntos y guion)
window.formatRutInput = function (input) {
    if (!input) return;
    var raw = input.value || '';
    var clean = raw.replace(/[^0-9kK]/g, '');

    if (!clean) {
        input.value = '';
        return;
    }

    if (clean.length === 1) {
        input.value = clean.toUpperCase();
        return;
    }

    // Máximo 9 caracteres (8 cuerpo + 1 DV)
    if (clean.length > 9) {
        clean = clean.slice(0, 9);
    }

    var dv = clean.slice(-1).toUpperCase();
    var cuerpo = clean.slice(0, -1);
    var formattedCuerpo = cuerpo.replace(/\B(?=(\d{3})+(?!\d))/g, '.');

    input.value = formattedCuerpo + '-' + dv;
};

// Formato para portapapeles / CAS Chile: sin puntos, con ceros a la izquierda (8 dígitos) y guion
window.formatRutForClipboard = function (text) {
    if (!text) return '';
    var clean = text.replace(/[\.\s]/g, '').trim();
    if (!clean) return text;

    var match = clean.match(/^(\d{1,8})-?([0-9kK])$/i);
    if (match) {
        var cuerpo = match[1];
        var dv = match[2].toUpperCase();
        cuerpo = cuerpo.padStart(8, '0');
        return cuerpo + '-' + dv;
    }
    return clean;
};

// Copia directa al portapapeles en formato CAS Chile con notificación visual
window.copyRutCasChile = function (rutText) {
    if (!rutText) return;
    var formatted = window.formatRutForClipboard(rutText);

    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(formatted).then(function () {
            window.showRutToast('RUT copiado para CAS-Chile: ' + formatted);
        }).catch(function () {
            window.fallbackCopyText(formatted);
        });
    } else {
        window.fallbackCopyText(formatted);
    }
};

window.fallbackCopyText = function (text) {
    var textArea = document.createElement('textarea');
    textArea.value = text;
    textArea.style.position = 'fixed';
    textArea.style.left = '-9999px';
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();
    try {
        document.execCommand('copy');
        window.showRutToast('RUT copiado para CAS-Chile: ' + text);
    } catch (err) {
        console.error('Error al copiar RUT', err);
    }
    document.body.removeChild(textArea);
};

// Toast flotante no intrusivo para confirmar copiado
window.showRutToast = function (msg) {
    var existing = document.getElementById('rut-copy-toast');
    if (existing) {
        existing.remove();
    }

    var toast = document.createElement('div');
    toast.id = 'rut-copy-toast';
    toast.textContent = msg;
    toast.style.position = 'fixed';
    toast.style.bottom = '24px';
    toast.style.right = '24px';
    toast.style.background = '#0f172a';
    toast.style.color = '#38bdf8';
    toast.style.padding = '10px 18px';
    toast.style.borderRadius = '8px';
    toast.style.fontSize = '0.88rem';
    toast.style.fontWeight = '600';
    toast.style.boxShadow = '0 6px 20px rgba(0,0,0,0.3)';
    toast.style.zIndex = '99999';
    toast.style.transition = 'opacity 0.25s ease, transform 0.25s ease';
    toast.style.transform = 'translateY(10px)';
    toast.style.opacity = '0';

    document.body.appendChild(toast);

    requestAnimationFrame(function () {
        toast.style.transform = 'translateY(0)';
        toast.style.opacity = '1';
    });

    setTimeout(function () {
        toast.style.opacity = '0';
        toast.style.transform = 'translateY(10px)';
        setTimeout(function () {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, 250);
    }, 2400);
};

// Interceptor global del evento copy (Ctrl+C o botón derecho 'Copiar')
document.addEventListener('copy', function (event) {
    var active = document.activeElement;
    var isRutInput = active && (
        active.classList.contains('rut-input') ||
        active.classList.contains('excel-rut-input') ||
        active.name === 'rut' ||
        active.id === 'drawer-rut' ||
        (active.closest && (active.closest('.rut-wrapper-cell') || active.closest('.rut-cell')))
    );

    var textToFormat = null;
    if (isRutInput && active.value) {
        textToFormat = active.value.substring(active.selectionStart, active.selectionEnd) || active.value;
    } else {
        var selection = window.getSelection ? window.getSelection().toString().trim() : '';
        var cleanSel = selection.replace(/[\.\s]/g, '');
        if (/^\d{1,8}-?[\dkK]$/i.test(cleanSel)) {
            textToFormat = selection;
        }
    }

    if (textToFormat) {
        var formatted = window.formatRutForClipboard(textToFormat);
        if (formatted) {
            event.clipboardData.setData('text/plain', formatted);
            event.preventDefault();
            window.showRutToast('RUT copiado para CAS-Chile: ' + formatted);
        }
    }
});

// Control de tamaño de hoja para documentos PDF imprimibles
window.updatePdfPageSize = function (size) {
    var style = document.getElementById('pdf-page-size-style');
    if (!style) {
        style = document.createElement('style');
        style.id = 'pdf-page-size-style';
        document.head.appendChild(style);
    }
    style.textContent = '@page { size: ' + size + '; margin: 12mm; }';
};

// Disparador de impresión del navegador para PDF
window.printPdfDocument = function () {
    window.print();
};


/* ==========================================================================
   SIMULADOR INTERACTIVO CON SPOTLIGHT Y GUÍA CON FLECHA EXPLICATIVA (SGL)
   ========================================================================== */
(function () {
    let tourPasoActual = 0;
    let tourKeyHandler = null;

    const PASOS_TOUR = [
        {
            target: '.brand, .brand-group',
            titulo: '1. Sistema de Gestión de Carpetas 2.0',
            icono: '🏛️',
            pose: 'saluda',
            descripcion: 'Plataforma oficial del SGL para el control integral de expedientes y carpetas físicas de licencias de conducir en la Municipalidad de Valparaíso.'
        },
        {
            target: '.user-block, .autor',
            titulo: '2. Identificación del Funcionario',
            icono: '👤',
            pose: 'explica',
            descripcion: 'Ingresa tu nombre en esta casilla. Cada acción, cambio de estado, edición o embalaje en caja quedará registrado con tu autoría para máxima trazabilidad y auditoría.'
        },
        {
            target: '.sede-pills, .sede-tabs-container',
            titulo: '3. Sedes y Conteo en Vivo',
            icono: '📍',
            pose: 'explica',
            descripcion: 'Filtra al instante las carpetas por sede: <b>Todas</b>, <b>Av. Argentina</b>, <b>Placilla</b> o <b>Mercado Puerto</b>, visualizando los totales de expedientes actualizados en tiempo real.'
        },
        {
            target: '.pdf-sector-pills',
            titulo: '4. Listados PDF Oficiales por Sector',
            icono: '📄',
            pose: 'alerta',
            descripcion: 'Genera las nóminas oficiales para los estanteros según fecha: <b>PDF Archivo</b> (anteriores a julio 2023) y <b>PDF Oficina 43</b> (julio 2023 en adelante) con las carpetas seleccionadas.'
        },
        {
            target: '.btn-toggle-filters, .quick-actions',
            titulo: '5. Búsqueda y Filtros Avanzados',
            icono: '🔍',
            pose: 'explica',
            descripcion: 'Despliega el panel para buscar por <b>RUT</b> o nombre, filtrar por estado o fecha, facilitando la ubicación inmediata de cualquier contribuyente en segundos.'
        },
        {
            target: '.data-table, .table-container, main',
            titulo: '6. Grilla de Carpetas y CAS Chile',
            icono: '📋',
            pose: 'explica',
            descripcion: 'Visualiza y gestiona las carpetas del sistema. Incluye copiado automático de <b>RUT al portapapeles con formato CAS Chile</b> (8 dígitos y guion) con un solo clic.'
        },
        {
            target: 'a[href="cajas"], .main-nav',
            titulo: '7. Cajas de Archivo y Embalaje',
            icono: '📦',
            pose: 'celebra',
            descripcion: 'Accede al módulo de <b>Cajas de Archivo</b> para agrupar expedientes físicos en cajas foliadas oficiales con rotulado estándar para entrega segura a bodega.'
        }
    ];

    function iniciarGuiaInteractiva() {
        const tip = document.getElementById('tooltip-flotante');
        if (tip) tip.classList.remove('visible');

        cerrarGuiaInteractiva();
        tourPasoActual = 0;

        const overlay = document.createElement('div');
        overlay.id = 'tour-overlay';
        overlay.className = 'tour-overlay';
        overlay.innerHTML = `
            <button class="tour-btn-salir-flotante" id="tour-salir-flotante" title="Terminar y cerrar la guía">
                <span>✕</span> Cerrar guía
            </button>
            <div id="tour-spotlight" class="tour-spotlight"></div>
            <div id="tour-card" class="tour-card" role="dialog" aria-modal="true" aria-labelledby="tour-titulo">
                <div class="tour-card-header">
                    <span class="tour-paso-badge" id="tour-badge">Paso 1 de ${PASOS_TOUR.length}</span>
                    <button class="tour-btn-cerrar" id="tour-cerrar" title="Cerrar guía" aria-label="Cerrar guía">&times;</button>
                </div>
                <div class="tour-card-body">
                    <div class="tour-mascota" data-lico="explica" aria-hidden="true"></div>
                    <div class="tour-card-texto">
                        <h3 id="tour-titulo" class="tour-card-titulo"></h3>
                        <p id="tour-desc" class="tour-card-desc"></p>
                    </div>
                </div>
                <div class="tour-card-footer">
                    <button class="tour-btn-nav" id="tour-prev">Anterior</button>
                    <div class="tour-dots" id="tour-dots"></div>
                    <button class="tour-btn-nav tour-btn-primary" id="tour-next">Siguiente</button>
                </div>
                <div id="tour-flecha" class="tour-flecha"></div>
            </div>`;
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
                tourPasoActual++;
                renderPasoTour();
            } else if (e.key === 'ArrowLeft' && tourPasoActual > 0) {
                tourPasoActual--;
                renderPasoTour();
            }
        };
        window.addEventListener('keydown', tourKeyHandler);

        document.getElementById('tour-prev').onclick = function () {
            if (tourPasoActual > 0) {
                tourPasoActual--;
                renderPasoTour();
            }
        };

        document.getElementById('tour-next').onclick = function () {
            if (tourPasoActual < PASOS_TOUR.length - 1) {
                tourPasoActual++;
                renderPasoTour();
            } else {
                cerrarGuiaInteractiva();
                mostrarToastFinal();
            }
        };

        renderPasoTour(true);
    }

    function renderPasoTour(esPrimerRender = false) {
        const paso = PASOS_TOUR[tourPasoActual];
        let target = null;

        const selectores = paso.target.split(',');
        for (let s of selectores) {
            const found = document.querySelector(s.trim());
            if (found && found.offsetParent !== null) {
                target = found;
                break;
            }
        }

        if (!target) {
            target = document.querySelector('.topbar') || document.body;
        }
        if (!target) return;

        document.getElementById('tour-badge').textContent = `Paso ${tourPasoActual + 1} de ${PASOS_TOUR.length}`;
        document.getElementById('tour-titulo').innerHTML = `<span class="tour-ico">${paso.icono}</span> ${paso.titulo}`;
        document.getElementById('tour-desc').innerHTML = paso.descripcion;
        const mascota = document.querySelector('#tour-overlay .tour-mascota');
        if (mascota && window.Lico && typeof window.Lico.svg === 'function') mascota.innerHTML = window.Lico.svg(paso.pose || 'explica');
        document.getElementById('tour-prev').disabled = tourPasoActual === 0;

        const esUltimo = tourPasoActual === PASOS_TOUR.length - 1;
        const btnNext = document.getElementById('tour-next');
        if (esUltimo) {
            btnNext.textContent = '✔ ¡Finalizar!';
            btnNext.classList.add('tour-btn-final');
        } else {
            btnNext.textContent = 'Siguiente';
            btnNext.classList.remove('tour-btn-final');
        }

        // Dots
        document.getElementById('tour-dots').innerHTML = PASOS_TOUR.map((_, i) =>
            `<span class="tour-dot ${i === tourPasoActual ? 'activo' : ''}"></span>`
        ).join('');

        const posicionar = () => {
            const r = target.getBoundingClientRect();
            const spot = document.getElementById('tour-spotlight');
            const card = document.getElementById('tour-card');
            const flecha = document.getElementById('tour-flecha');
            if (!spot || !card) return;

            const cardW = Math.min(480, window.innerWidth - 32);
            const cardH = 300;
            const pad = 6;
            const esGrilla = r.height > window.innerHeight * 0.55 || r.width > window.innerWidth * 0.85;

            if (esPrimerRender) {
                spot.style.transition = 'none';
                card.style.transition = 'none';
            }

            if (esGrilla) {
                const spotTop = Math.max(12, Math.round(r.top));
                const spotH = Math.min(Math.round(r.height), window.innerHeight - spotTop - 24);
                spot.style.left = `${Math.max(10, Math.round(r.left - pad))}px`;
                spot.style.top = `${spotTop}px`;
                spot.style.width = `${Math.min(window.innerWidth - 20, Math.round(r.width + pad * 2))}px`;
                spot.style.height = `${Math.max(220, spotH)}px`;

                const cardLeft = Math.round((window.innerWidth - cardW) / 2);
                const cardTop = Math.round(Math.max(80, (window.innerHeight - cardH) / 2));
                card.style.left = `${cardLeft}px`;
                card.style.top = `${cardTop}px`;
                if (flecha) flecha.style.display = 'none';
            } else {
                if (flecha) flecha.style.display = 'block';

                spot.style.left = `${Math.max(0, Math.round(r.left - pad))}px`;
                spot.style.top = `${Math.max(0, Math.round(r.top - pad))}px`;
                spot.style.width = `${Math.round(r.width + pad * 2)}px`;
                spot.style.height = `${Math.round(r.height + pad * 2)}px`;

                let cardLeft = Math.round(r.left + (r.width / 2) - (cardW / 2));
                if (cardLeft < 16) cardLeft = 16;
                if (cardLeft + cardW > window.innerWidth - 16) cardLeft = window.innerWidth - cardW - 16;

                let cardTop = Math.round(r.bottom + 14);
                let flechaArriba = true;

                if (cardTop + cardH > window.innerHeight - 16) {
                    cardTop = Math.round(r.top - cardH - 14);
                    flechaArriba = false;
                }

                if (cardTop < 16) cardTop = 16;
                if (cardTop + cardH > window.innerHeight - 16) cardTop = window.innerHeight - cardH - 16;

                card.style.left = `${cardLeft}px`;
                card.style.top = `${cardTop}px`;

                if (flecha) {
                    flecha.className = `tour-flecha ${flechaArriba ? 'flecha-arriba' : 'flecha-abajo'}`;
                    const flechaX = Math.max(24, Math.min(cardW - 36, (r.left + r.width / 2) - cardLeft));
                    flecha.style.left = `${Math.round(flechaX)}px`;
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

        const rect = target.getBoundingClientRect();
        const yaVisible = rect.top >= 0 && rect.bottom <= window.innerHeight;

        if (yaVisible || esPrimerRender) {
            posicionar();
        } else {
            target.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            setTimeout(posicionar, 100);
        }
    }

    function cerrarGuiaInteractiva() {
        if (tourKeyHandler) {
            window.removeEventListener('keydown', tourKeyHandler);
            tourKeyHandler = null;
        }
        const o = document.getElementById('tour-overlay');
        if (o) o.remove();
    }

    function mostrarToastFinal() {
        const toast = document.createElement('div');
        toast.style.cssText = `
            position: fixed;
            bottom: 24px;
            right: 24px;
            background: #0f172a;
            color: #ffffff;
            border-left: 4px solid #10b981;
            border-radius: 8px;
            padding: 12px 20px;
            font-size: 14px;
            font-weight: 600;
            box-shadow: 0 10px 25px rgba(0,0,0,0.3);
            z-index: 100000;
            opacity: 0;
            transform: translateY(10px);
            transition: all 0.3s ease;
        `;
        toast.innerHTML = '🎉 ¡Guía interactiva completada! Ya conoces el funcionamiento de Gestión de Carpetas 2.0.';
        document.body.appendChild(toast);
        requestAnimationFrame(() => {
            toast.style.opacity = '1';
            toast.style.transform = 'translateY(0)';
        });
        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(10px)';
            setTimeout(() => toast.remove(), 300);
        }, 4000);
    }

    function iniciarTooltipsGlobales() {
        let tip = document.getElementById('tooltip-flotante');
        if (!tip) {
            tip = document.createElement('div');
            tip.id = 'tooltip-flotante';
            tip.className = 'tooltip-flotante';
            document.body.appendChild(tip);
        }

        document.addEventListener('mouseover', function (e) {
            const el = e.target.closest('[data-tooltip]');
            if (!el) {
                tip.classList.remove('visible');
                return;
            }
            const texto = el.getAttribute('data-tooltip');
            if (!texto) return;
            tip.textContent = texto;
            tip.classList.add('visible');

            const rect = el.getBoundingClientRect();
            const tipRect = tip.getBoundingClientRect();
            let left = rect.left + rect.width / 2 - tipRect.width / 2;
            if (left < 10) left = 10;
            if (left + tipRect.width > window.innerWidth - 10) left = window.innerWidth - tipRect.width - 10;
            let top = rect.bottom + 8;
            if (top + tipRect.height > window.innerHeight - 8) {
                top = rect.top - tipRect.height - 8;
                tip.classList.add('pos-arriba');
            } else {
                tip.classList.remove('pos-arriba');
            }
            tip.style.left = `${Math.round(left)}px`;
            tip.style.top = `${Math.round(top)}px`;
        });

        document.addEventListener('mouseout', function (e) {
            const el = e.target.closest('[data-tooltip]');
            if (el && !e.relatedTarget?.closest('[data-tooltip]')) {
                tip.classList.remove('visible');
            }
        });
    }

    // Inicializar listeners al cargar el DOM
    document.addEventListener('DOMContentLoaded', function () {
        iniciarTooltipsGlobales();

        // Delegado: sobrevive a los re-renders de Blazor (un solo disparo; el boton ya no tiene onclick inline).
        document.addEventListener('click', function (e) {
            if (!e.target.closest || !e.target.closest('#btn-guia-global, .btn-guia-accion')) return;
            e.preventDefault();
            const path = window.location.pathname.toLowerCase();
            if (!path.includes('carpetas') && path !== '/') {
                window.location.href = '/carpetas?simulador=1';
            } else {
                iniciarGuiaInteractiva();
            }
        });

        const params = new URLSearchParams(window.location.search);
        if (params.has('simulador') || params.has('tour') || params.has('guia')) {
            params.delete('simulador');
            params.delete('tour');
            params.delete('guia');
            const newSearch = params.toString();
            const newUrl = window.location.pathname + (newSearch ? '?' + newSearch : '') + window.location.hash;
            window.history.replaceState({}, '', newUrl);

            setTimeout(iniciarGuiaInteractiva, 300);
        }
    });

    window.iniciarGuiaInteractiva = iniciarGuiaInteractiva;
    window.cerrarGuiaInteractiva = cerrarGuiaInteractiva;
})();

/* ==========================================================================
   Preferencias de interfaz: tema (claro/oscuro) y estilo de filas (suaves/Excel).
   Se guardan por navegador; el estado inicial lo aplica un script en App.razor.
   ========================================================================== */
(function () {
    var root = document.documentElement;
    function save(k, v) { try { localStorage.setItem(k, v); } catch (e) { } }
    function syncRowsButton() {
        var b = document.getElementById('btn-filas');
        if (b) b.setAttribute('aria-pressed', root.getAttribute('data-rows') === 'classic' ? 'true' : 'false');
    }
    window.sglUi = {
        toggleTheme: function () {
            var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
            root.setAttribute('data-theme', next);
            save('sgl-theme', next);
        },
        toggleRows: function () {
            var next = root.getAttribute('data-rows') === 'classic' ? 'soft' : 'classic';
            root.setAttribute('data-rows', next);
            save('sgl-rows', next);
            syncRowsButton();
        },
    };
    // El botón se vuelve a renderizar con Blazor: sincronizar su estado al cambiar el DOM.
    new MutationObserver(syncRowsButton).observe(document.body, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', syncRowsButton);
})();
