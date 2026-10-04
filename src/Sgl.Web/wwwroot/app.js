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
