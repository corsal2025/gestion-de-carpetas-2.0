// Folder reception adapted from Organizador de citaciones. Read permission only.
const DB = 'sgl-agendas-jefatura-v1';
async function abrirDb() {
    return new Promise((ok, no) => { const r = indexedDB.open(DB, 1); r.onupgradeneeded = () => r.result.createObjectStore('recepcion'); r.onsuccess = () => ok(r.result); r.onerror = () => no(r.error); });
}
async function leer(clave) {
    const db = await abrirDb();
    try { return await new Promise((ok, no) => { const r = db.transaction('recepcion').objectStore('recepcion').get(clave); r.onsuccess = () => ok(r.result); r.onerror = () => no(r.error); }); }
    finally { db.close(); }
}
async function guardar(clave, valor) {
    const db = await abrirDb();
    try { await new Promise((ok, no) => { const t = db.transaction('recepcion', 'readwrite'); t.objectStore('recepcion').put(valor, clave); t.oncomplete = ok; t.onerror = () => no(t.error); }); }
    finally { db.close(); }
}
function sello(archivo) {
    const m = archivo.name.match(/citas_(\d{8})_(\d{6})/i);
    if (m) return m[1] + m[2];
    const d = new Date(archivo.lastModified), pad = n => String(n).padStart(2, '0');
    return String(d.getFullYear()) + pad(d.getMonth()+1) + pad(d.getDate()) + pad(d.getHours()) + pad(d.getMinutes()) + pad(d.getSeconds());
}
export function crearRecepcion(dotnet) {
    let carpeta = null, timer = null, leyendo = false, cerrada = false;
    const informar = (mensaje, conectada = !!carpeta, ocupado = false) => cerrada ? Promise.resolve() : dotnet.invokeMethodAsync('EstadoRecepcion', mensaje, conectada, ocupado);
    const detener = () => { clearInterval(timer); timer = null; };
    const iniciar = () => { detener(); timer = setInterval(() => sincronizar(false).catch(() => {}), 60000); };
    async function sincronizar(manual = false) {
        if (!carpeta || leyendo || cerrada) return;
        leyendo = true;
        let procesados = 0, creadas = 0, actualizadas = 0, errores = 0;
        try {
            if (await carpeta.queryPermission({mode: 'read'}) !== 'granted') { detener(); return await informar('Lectura en pausa. Pulse Reanudar para autorizar la carpeta.'); }
            await informar('Revisando ' + carpeta.name + '…', true, true);
            const archivos = [];
            for await (const handle of carpeta.values()) if (handle.kind === 'file' && /\.(csv|xlsx)$/i.test(handle.name)) archivos.push(await handle.getFile());
            archivos.sort((a,b) => sello(a).localeCompare(sello(b)) || a.name.localeCompare(b.name));
            for (const archivo of archivos) {
                if (cerrada) break;
                try {
                if (archivo.size > 50 * 1024 * 1024) { errores++; await dotnet.invokeMethodAsync('AvisoArchivo', archivo.name, 'El archivo supera 50 MB.'); continue; }
                const bytes = new Uint8Array(await archivo.arrayBuffer());
                const digest = await crypto.subtle.digest('SHA-256', bytes);
                const hash = Array.from(new Uint8Array(digest), n => n.toString(16).padStart(2, '0')).join('');
                if (!manual && await leer('archivo:' + hash)) continue;
                const stream = DotNet.createJSStreamReference(bytes);
                try {
                    const r = await dotnet.invokeMethodAsync('RecibirAgenda', archivo.name, stream);
                    if (r.correcto) { await guardar('archivo:' + hash, {nombre: archivo.name, recibido: Date.now()}); procesados++; creadas += r.creadas; actualizadas += r.actualizadas; }
                    else errores++;
                } finally { DotNet.disposeJSObjectReference(stream); }
                } catch (e) {
                    errores++;
                    if (!cerrada) await dotnet.invokeMethodAsync('AvisoArchivo', archivo.name, 'No se pudo recibir el archivo. Se volverá a intentar en la próxima revisión.');
                }
            }
            await informar(`${carpeta.name}: ${procesados} archivo(s) leído(s), ${creadas} carpetas nuevas, ${actualizadas} actualizadas${errores ? ', ' + errores + ' archivo(s) con error: revise el detalle' : ''}. Última revisión ${new Date().toLocaleTimeString('es-CL')}. Revisión automática cada minuto.`);
        } catch (e) { await informar('No se pudo leer la carpeta. Reanude la conexión o use el selector de archivos.'); }
        finally { leyendo = false; }
    }
    return {
        async restaurar() {
            if (!window.showDirectoryPicker) return informar('Este navegador no permite conectar carpetas. Use Chrome o Edge, o el selector de archivos de abajo.', false);
            try { carpeta = await leer('carpeta'); if (!carpeta) return; if (await carpeta.queryPermission({mode:'read'}) === 'granted') { iniciar(); void sincronizar(false); } else await informar('Carpeta guardada. Pulse Reanudar para continuar la recepción.'); }
            catch { await informar('Seleccione la carpeta entrada para iniciar la recepción.', false); }
        },
        async conectar(cambiar = false) {
            if (!window.showDirectoryPicker) return informar('Use Chrome o Edge para conectar una carpeta, o el selector de archivos de abajo.', false);
            try {
                if (!cambiar && carpeta && await carpeta.requestPermission({mode:'read'}) === 'granted') { iniciar(); void sincronizar(false); return; }
                carpeta = await window.showDirectoryPicker({id:'sgl-jefatura',mode:'read'});
                await guardar('carpeta', carpeta); iniciar(); void sincronizar(false);
            } catch (e) { if (e.name !== 'AbortError') await informar('No se pudo conectar la carpeta. Puede importar los archivos con el selector de abajo.'); }
        },
        sincronizar(manual = false) { void sincronizar(manual); },
        async desconectar() { detener(); carpeta = null; await guardar('carpeta', null); await informar('Recepción desconectada.', false); },
        dispose() { cerrada = true; detener(); }
    };
}
