import os
import re
import sys
import uuid
import sqlite3
import unicodedata
import datetime
import openpyxl

EXCEL_PATH = r"C:\Users\raul.salazar\Desktop\GESTION DE LICENCIAS\DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR 2026.xlsx"
DB_PATH = r"src\Sgl.Web\sgl.db"

def normalize_text_for_search(text: str) -> str:
    decomposed = unicodedata.normalize('NFD', text.strip())
    without_accents = ''.join(c for c in decomposed if unicodedata.category(c) != 'Mn')
    return unicodedata.normalize('NFC', without_accents).lower()

def compute_check_digit(body: str) -> str:
    s = 0
    w = 2
    for c in reversed(body):
        s += int(c) * w
        w = 2 if w == 7 else w + 1
    r = 11 - (s % 11)
    if r == 11:
        return '0'
    if r == 10:
        return 'K'
    return str(r)

def normalize_rut(raw_val) -> str | None:
    if not raw_val:
        return None
    val = str(raw_val).strip().replace('.', '').replace('-', '').upper()
    if len(val) < 2:
        return None
    body = val[:-1].lstrip('0')
    dv = val[-1]
    if not body or not body.isdigit() or len(body) > 9:
        return None
    
    # Auto-correct check digit if slight typo
    expected_dv = compute_check_digit(body)
    return f"{body}-{expected_dv}"

def parse_date(raw_val, default_date: str = "2026-01-02") -> str | None:
    if not raw_val:
        return None
    if isinstance(raw_val, (datetime.datetime, datetime.date)):
        return raw_val.strftime("%Y-%m-%d")
    s = str(raw_val).strip()
    if not s:
        return None
    for fmt in ("%d-%m-%Y", "%d/%m/%Y", "%Y-%m-%d", "%Y/%m/%d", "%d-%m-%y", "%d/%m/%y"):
        try:
            return datetime.datetime.strptime(s, fmt).strftime("%Y-%m-%d")
        except ValueError:
            pass
    return default_date

def map_estado_and_tramite(col_estado, col_decision):
    estado_raw = str(col_estado).strip().upper() if col_estado else ""
    decision_raw = str(col_decision).strip().upper() if col_decision else ""

    # Determinar Decisión
    if "OTORGADO" in decision_raw:
        decision = "Otorgado"
    elif "DENEGADO" in decision_raw:
        decision = "Denegado"
    else:
        decision = "Pendiente"

    # Determinar Tipo Tramite (para filtros y estadísticas)
    tipo_tramite = None
    if "1" in estado_raw and "LIC" in estado_raw:
        tipo_tramite = "1° LICENCIA"
        estado = "PrimeraLicencia"
    elif "CONASET" in estado_raw:
        if "DOM" in estado_raw:
            tipo_tramite = "CAMBIO DE DOMICILIO"
            estado = "CambioDomicilio"
        else:
            tipo_tramite = "SUBIDA A CONASET"
            estado = "SubidaConaset"
    elif "F8" in estado_raw:
        tipo_tramite = "SUBIDA CON F8"
        estado = "SubidaConaset"
    elif "OFICIO" in estado_raw:
        tipo_tramite = "SUBIDA CON OFICIO"
        estado = "SubidaConaset"
    elif "DOMICILIO" in estado_raw or "DOM" in estado_raw:
        tipo_tramite = "CAMBIO DE DOMICILIO"
        estado = "CambioDomicilio"
    elif "CANJE" in estado_raw:
        tipo_tramite = "CANJE LIC. EXTRANJERA"
        estado = "Revision"
    elif "NO EXISTE" in estado_raw:
        tipo_tramite = "NO EXISTE CARPETA"
        estado = "Alertada"
    elif "OF. 43" in estado_raw or "OF.43" in estado_raw:
        tipo_tramite = "SE ENCUENTRA EN OF. 43"
        estado = "Revision"
    elif "ARCHIVOS" in estado_raw:
        tipo_tramite = "SE ENCUENTRA EN ARCHIVOS"
        estado = "Revision"
    elif "CERTIFICADO" in estado_raw:
        tipo_tramite = "CREAR CERTIFICADO"
        estado = "Revision"
    elif estado_raw:
        tipo_tramite = estado_raw[:100]
        estado = "Revision"
    else:
        # Sin columna 9, deducir por decisión o citación
        if "OTORGADO" in decision_raw:
            estado = "Otorgado"
        elif "DENEGADO" in decision_raw:
            estado = "Denegado"
        elif "ESPERA EX" in decision_raw:
            estado = "EsperaExamen"
        elif "CLASE PENDIENTE" in decision_raw:
            estado = "ClasePendiente"
        elif "PARA DENEGAR" in decision_raw:
            estado = "ParaDenegar"
        else:
            estado = "Citada"

    return estado, decision, tipo_tramite

def main():
    if not os.path.exists(EXCEL_PATH):
        print(f"Error: no se encuentra el Excel en {EXCEL_PATH}")
        sys.exit(1)
    if not os.path.exists(DB_PATH):
        print(f"Error: no se encuentra la base de datos SQLite en {DB_PATH}")
        sys.exit(1)

    print(f"Abriendo libro Excel: {EXCEL_PATH} ...")
    wb = openpyxl.load_workbook(EXCEL_PATH, data_only=True)

    sheet_names = [
        s for s in wb.sheetnames 
        if any(s.endswith(x) for x in ['AV. ARGENTINA', 'PLACILLA', 'MERC. PUERTO']) 
        and not s.startswith('PLANTILLA')
    ]

    print(f"Hojas a procesar ({len(sheet_names)}): {', '.join(sheet_names)}")

    ciudadanos_map = {}  # rut -> dict con datos de Ciudadano
    carpetas_list = []
    historial_list = []

    now_iso = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")

    for sheet in sheet_names:
        if "AV. ARGENTINA" in sheet:
            sede = "AvArgentina"
        elif "PLACILLA" in sheet:
            sede = "Placilla"
        elif "MERC. PUERTO" in sheet:
            sede = "MercadoPuerto"
        else:
            sede = "AvArgentina"

        ws = wb[sheet]
        rows = list(ws.iter_rows(values_only=True))[2:]
        sheet_count = 0

        for r in rows:
            if not any(r):
                continue

            raw_rut = r[6] if len(r) > 6 else None
            rut = normalize_rut(raw_rut)
            if not rut:
                continue

            raw_nombre = str(r[3]).strip() if len(r) > 3 and r[3] else ""
            raw_apellido = str(r[4]).strip() if len(r) > 4 and r[4] else ""
            raw_completo = str(r[5]).strip() if len(r) > 5 and r[5] else ""

            if not raw_nombre and not raw_apellido:
                if raw_completo:
                    parts = raw_completo.split()
                    raw_nombre = parts[0] if parts else "Sin Nombre"
                    raw_apellido = " ".join(parts[1:]) if len(parts) > 1 else "Sin Apellido"
                else:
                    raw_nombre = "Sin Nombre"
                    raw_apellido = "Sin Apellido"
            elif not raw_nombre:
                raw_nombre = "Vecino"
            elif not raw_apellido:
                raw_apellido = "Vecino"

            raw_nombre = raw_nombre[:100]
            raw_apellido = raw_apellido[:100]

            if rut not in ciudadanos_map:
                cid = str(uuid.uuid4()).upper()
                nombre_busqueda = normalize_text_for_search(f"{raw_nombre} {raw_apellido}")[:201]
                ciudadanos_map[rut] = {
                    "Id": cid,
                    "Rut": rut,
                    "Nombre": raw_nombre,
                    "Apellido": raw_apellido,
                    "NombreBusqueda": nombre_busqueda,
                }
            else:
                cid = ciudadanos_map[rut]["Id"]

            # Fechas y Comuna
            fecha_citacion = parse_date(r[0] if len(r) > 0 else None, "2026-01-02") or "2026-01-02"
            fecha_subida = parse_date(r[1] if len(r) > 1 else None)
            
            raw_ult_carp = r[2] if len(r) > 2 and r[2] is not None else None
            fecha_ultima = None
            comuna_origen = None
            if isinstance(raw_ult_carp, (datetime.datetime, datetime.date)):
                fecha_ultima = raw_ult_carp.strftime("%Y-%m-%d")
            elif raw_ult_carp:
                s_ult = str(raw_ult_carp).strip()
                iso_parsed = parse_date(s_ult)
                if iso_parsed:
                    fecha_ultima = iso_parsed
                else:
                    comuna_clean = s_ult.upper()
                    replacements = {
                        "VIA DEL MAR": "VIÑA DEL MAR",
                        "QUILPU": "QUILPUÉ",
                        "VILLA ALEMANA": "VILLA ALEMANA",
                        "PEALOLEN": "PEÑALOLÉN",
                        "UOA": "ÑUÑOA",
                        "MAIP": "MAIPÚ",
                        "CON CON": "CONCÓN",
                        "CONCON": "CONCÓN",
                        "VALPARASO": "VALPARAÍSO",
                        "VALPARAISO": "VALPARAÍSO",
                    }
                    for bad, good in replacements.items():
                        if bad in comuna_clean:
                            comuna_clean = comuna_clean.replace(bad, good)
                    comuna_origen = comuna_clean[:100]

            raw_idoneidad = str(r[8]).strip()[:200] if len(r) > 8 and r[8] else None
            col_estado = r[9] if len(r) > 9 else None
            col_decision = r[10] if len(r) > 10 else None

            estado, decision, tipo_tramite = map_estado_and_tramite(col_estado, col_decision)

            carpeta_id = str(uuid.uuid4()).upper()
            carpetas_list.append((
                carpeta_id,
                cid,
                sede,
                fecha_citacion,
                fecha_subida,
                estado,
                decision,
                raw_idoneidad,
                None, # CajaArchivo
                fecha_ultima,
                tipo_tramite,
                comuna_origen
            ))

            historial_list.append((
                carpeta_id,
                now_iso,
                "Sistema Importador",
                "Importacion",
                None,
                f"{sede} - {tipo_tramite or estado}"
            ))

            sheet_count += 1

        print(f"  -> {sheet}: {sheet_count} carpetas leídas.")

    print(f"\nTotal ciudadanos únicos: {len(ciudadanos_map)}")
    print(f"Total carpetas a insertar: {len(carpetas_list)}")

    # Conectar a SQLite e insertar
    con = sqlite3.connect(DB_PATH)
    try:
        cur = con.cursor()
        cur.execute("PRAGMA foreign_keys = OFF;")
        
        print("Limpiando tablas existentes...")
        cur.execute("DELETE FROM HistorialCambios;")
        cur.execute("DELETE FROM Carpetas;")
        cur.execute("DELETE FROM Ciudadanos;")

        print("Insertando ciudadanos...")
        ciudadanos_rows = [
            (c["Id"], c["Rut"], c["Nombre"], c["Apellido"], c["NombreBusqueda"])
            for c in ciudadanos_map.values()
        ]
        cur.executemany(
            "INSERT INTO Ciudadanos (Id, Rut, Nombre, Apellido, NombreBusqueda) VALUES (?, ?, ?, ?, ?);",
            ciudadanos_rows
        )

        print("Insertando carpetas...")
        cur.executemany(
            """INSERT INTO Carpetas (
                Id, CiudadanoId, Sede, FechaCitacion, FechaSubida, 
                Estado, Decision, IdoneidadMoral, CajaArchivo, FechaUltimaCarpeta, TipoTramite, Comuna
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);""",
            carpetas_list
        )

        print("Insertando historial...")
        cur.executemany(
            "INSERT INTO HistorialCambios (CarpetaId, Fecha, Usuario, Campo, Anterior, Nuevo) VALUES (?, ?, ?, ?, ?, ?);",
            historial_list
        )

        cur.execute("PRAGMA foreign_keys = ON;")
        con.commit()
        print("¡Transacción completada exitosamente!")
    finally:
        con.close()

if __name__ == "__main__":
    main()
