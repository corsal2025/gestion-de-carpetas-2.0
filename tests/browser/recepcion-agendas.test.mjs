import test from 'node:test';
import assert from 'node:assert/strict';
import { crearRecepcion } from '../../src/Sgl.Web/wwwroot/agenda-entrada.js';

// Native folder permissions are supplied by the browser. These tests exercise
// reception orchestration without opening Outlook or touching user files.
function entorno(files = [], respuesta = () => ({ correcto: true, creadas: 1, actualizadas: 0 })) {
    const guardados = new Map(), recibidos = [], estados = [], avisos = [], timers = new Map();
    let permiso = 'granted', id = 0;
    const originales = new Map(['window', 'indexedDB', 'DotNet', 'setInterval', 'clearInterval'].map(k => [k, Object.getOwnPropertyDescriptor(globalThis, k)]));
    const carpeta = {
        name: 'entrada ficticia',
        queryPermission: async o => { assert.equal(o.mode, 'read'); return permiso; },
        requestPermission: async o => { assert.equal(o.mode, 'read'); permiso = 'granted'; return permiso; },
        async *values() { for (const f of files) yield { kind: 'file', name: f.name, getFile: async () => f }; }
    };
    const db = {
        close() {},
        transaction(_, mode) {
            const tx = { objectStore: () => ({
                get(key) { const r = {}; queueMicrotask(() => { r.result = guardados.get(key); r.onsuccess?.(); }); return r; },
                put(value, key) { assert.equal(mode, 'readwrite'); guardados.set(key, value); queueMicrotask(() => tx.oncomplete?.()); }
            }) };
            return tx;
        }
    };
    Object.assign(globalThis, {
        window: { showDirectoryPicker: async o => { assert.equal(o.mode, 'read'); return carpeta; } },
        indexedDB: { open() { const r = {}; queueMicrotask(() => { r.result = db; r.onsuccess?.(); }); return r; } },
        DotNet: { createJSStreamReference: bytes => bytes, disposeJSObjectReference() {} },
        setInterval(fn, ms) { assert.equal(ms, 60000); timers.set(++id, fn); return id; },
        clearInterval(i) { timers.delete(i); }
    });
    const dotnet = { async invokeMethodAsync(method, ...args) {
        if (method === 'EstadoRecepcion') { estados.push(args); return; }
        if (method === 'AvisoArchivo') { avisos.push(args); return; }
        assert.equal(method, 'RecibirAgenda');
        recibidos.push(args[0]);
        return respuesta(args[0], recibidos.length);
    } };
    return {
        files, guardados, recibidos, estados, avisos, timers,
        crear: () => crearRecepcion(dotnet),
        permiso: valor => { permiso = valor; },
        tick: async () => { assert.equal(timers.size, 1); await [...timers.values()][0](); },
        async terminado() {
            for (let i = 0; i < 250; i++) {
                if (estados.some(s => s[0].includes('Última revisión'))) return;
                await new Promise(r => setTimeout(r, 2));
            }
            assert.fail('La recepción no terminó.');
        },
        limpiarEstado() { estados.length = 0; },
        dispose() {
            for (const [k, descriptor] of originales) {
                if (descriptor) Object.defineProperty(globalThis, k, descriptor);
                else delete globalThis[k];
            }
        }
    };
}
const archivo = (name, texto) => ({ name, size: Buffer.byteLength(texto), lastModified: 0, arrayBuffer: async () => Buffer.from(texto) });

test('ordena por la fecha del archivo de citas, aunque el correo viejo se haya recibido después', async () => {
    const e = entorno([archivo('citas_20261103_120000.csv', 'tarde'), archivo('20261103_190000_citas_20261103_090000.csv', 'mañana')]);
    const s = e.crear();
    try { await s.conectar(); await e.terminado(); assert.deepEqual(e.recibidos, [e.files[1].name, e.files[0].name]); }
    finally { s.dispose(); e.dispose(); }
});
test('omite el mismo contenido aunque llegue con otro nombre', async () => {
    const e = entorno([archivo('citas_20261103_090000.csv', 'igual'), archivo('citas_20261103_120000.csv', 'igual')]), s = e.crear();
    try { await s.conectar(); await e.terminado(); assert.equal(e.recibidos.length, 1); await e.tick(); assert.equal(e.recibidos.length, 1); }
    finally { s.dispose(); e.dispose(); }
});
test('detecta contenido corregido del mismo tamaño y nombre', async () => {
    const e = entorno([archivo('citas_20261103_090000.csv', 'AA')]), s = e.crear();
    try { await s.conectar(); await e.terminado(); e.files[0] = archivo(e.files[0].name, 'BB'); await e.tick(); assert.equal(e.recibidos.length, 2); }
    finally { s.dispose(); e.dispose(); }
});
test('un archivo rechazado no se marca recibido y se vuelve a intentar', async () => {
    const e = entorno([archivo('citas_20261103_090000.csv', 'datos')], (_, n) => ({ correcto: n > 1, creadas: 1, actualizadas: 0 })), s = e.crear();
    try { await s.conectar(); await e.terminado(); await e.tick(); await e.tick(); assert.equal(e.recibidos.length, 2); }
    finally { s.dispose(); e.dispose(); }
});
test('recupera la carpeta y evita repetir lo recibido al abrir otra sesión', async () => {
    const e = entorno([archivo('citas_20261103_090000.csv', 'datos')]), s = e.crear(); let otra;
    try { await s.conectar(); await e.terminado(); s.dispose(); e.limpiarEstado(); otra = e.crear(); await otra.restaurar(); await e.terminado(); assert.equal(e.recibidos.length, 1); assert.equal(e.timers.size, 1); }
    finally { otra?.dispose(); s.dispose(); e.dispose(); }
});
test('perder permiso pausa la lectura y permite reanudar en modo de solo lectura', async () => {
    const e = entorno([archivo('citas_20261103_090000.csv', 'datos')]), s = e.crear();
    try { await s.conectar(); await e.terminado(); e.permiso('prompt'); await e.tick(); assert.equal(e.timers.size, 0); assert.ok(e.estados.some(s => s[0].includes('pausa'))); e.limpiarEstado(); await s.conectar(); await e.terminado(); assert.equal(e.timers.size, 1); }
    finally { s.dispose(); e.dispose(); }
});
test('sincronizar ahora revalida archivos ya recibidos', async () => {
    const e = entorno([archivo('citas_20261103_090000.csv', 'datos')]), s = e.crear();
    try { await s.conectar(); await e.terminado(); e.limpiarEstado(); s.sincronizar(true); await e.terminado(); assert.equal(e.recibidos.length, 2); }
    finally { s.dispose(); e.dispose(); }
});
test('informa archivos demasiado grandes y continúa con los demás', async () => {
    const grande = { name: 'citas_20261103_090000.csv', size: 51*1024*1024, lastModified: 0, arrayBuffer: () => { throw Error('No debe leer el archivo grande'); } };
    const e = entorno([grande, archivo('citas_20261103_120000.csv', 'válido')]), s = e.crear();
    try { await s.conectar(); await e.terminado(); assert.equal(e.avisos.length, 1); assert.equal(e.recibidos.length, 1); }
    finally { s.dispose(); e.dispose(); }
});
test('desconectar elimina la conexión persistida y detiene el temporizador', async () => {
    const e = entorno([archivo('citas_20261103_090000.csv', 'datos')]), s = e.crear();
    try { await s.conectar(); await e.terminado(); await s.desconectar(); assert.equal(e.guardados.get('carpeta'), null); assert.equal(e.timers.size, 0); }
    finally { s.dispose(); e.dispose(); }
});
