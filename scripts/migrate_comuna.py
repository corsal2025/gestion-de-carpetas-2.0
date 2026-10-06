import sqlite3
import datetime
import re

DB_PATH = r"src\Sgl.Web\sgl.db"

def parse_date_to_iso(val):
    if not val:
        return None
    val = val.strip()
    for fmt in ("%d-%m-%Y", "%d/%m/%Y", "%Y-%m-%d", "%Y/%m/%d", "%d-%m-%y", "%d/%m/%y"):
        try:
            return datetime.datetime.strptime(val, fmt).strftime("%Y-%m-%d")
        except ValueError:
            pass
    return None

def clean_comuna_name(raw):
    if not raw:
        return None
    c = raw.strip().upper()
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
        "EST. CENTRAL": "ESTACIÓN CENTRAL",
        "ESTACION CENTRAL": "ESTACIÓN CENTRAL",
        "SAN JOAQUIN": "SAN JOAQUÍN",
        "CONCEPCION": "CONCEPCIÓN",
        "CURICO": "CURICÓ",
        "CHILLAN": "CHILLÁN",
    }
    for bad, good in replacements.items():
        if bad in c:
            c = c.replace(bad, good)
    return c

def migrate():
    con = sqlite3.connect(DB_PATH)
    cur = con.cursor()

    # 1. Add Comuna column if not exists
    cur.execute("PRAGMA table_info(Carpetas)")
    cols = [col[1] for col in cur.fetchall()]
    if "Comuna" not in cols:
        print("Agregando columna Comuna a tabla Carpetas...")
        cur.execute("ALTER TABLE Carpetas ADD COLUMN Comuna TEXT NULL")
        con.commit()
    else:
        print("Columna Comuna ya existe.")

    # 2. Inspect all rows in Carpetas with FechaUltimaCarpeta
    cur.execute("SELECT Id, FechaUltimaCarpeta, Comuna FROM Carpetas WHERE FechaUltimaCarpeta IS NOT NULL")
    rows = cur.fetchall()
    print(f"Total registros con FechaUltimaCarpeta: {len(rows)}")

    updates = []
    for cid, f_ult, existing_comuna in rows:
        iso_date = parse_date_to_iso(f_ult)
        if iso_date:
            # It is a valid date! Store in ISO format YYYY-MM-DD
            updates.append((cid, iso_date, existing_comuna))
        else:
            # It is a Comuna!
            clean_com = clean_comuna_name(f_ult)
            updates.append((cid, None, clean_com))

    print(f"Procesando {len(updates)} actualizaciones...")
    for cid, new_date, new_comuna in updates:
        cur.execute("UPDATE Carpetas SET FechaUltimaCarpeta = ?, Comuna = ? WHERE Id = ?", (new_date, new_comuna, cid))

    # 3. Ensure all GUIDs in database are UPPERCASE
    cur.execute("UPDATE Carpetas SET Id = UPPER(Id), CiudadanoId = UPPER(CiudadanoId)")
    cur.execute("UPDATE Ciudadanos SET Id = UPPER(Id)")
    cur.execute("UPDATE HistorialCambios SET CarpetaId = UPPER(CarpetaId)")

    con.commit()
    print("Migración completada exitosamente.")

    # Verification
    cur.execute("SELECT COUNT(*) FROM Carpetas WHERE FechaUltimaCarpeta IS NOT NULL")
    total_dates = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM Carpetas WHERE Comuna IS NOT NULL")
    total_comunas = cur.fetchone()[0]
    cur.execute("SELECT FechaUltimaCarpeta, Comuna FROM Carpetas WHERE FechaUltimaCarpeta IS NOT NULL LIMIT 5")
    sample_dates = cur.fetchall()
    cur.execute("SELECT Comuna, FechaUltimaCarpeta FROM Carpetas WHERE Comuna IS NOT NULL LIMIT 5")
    sample_comunas = cur.fetchall()

    print(f"Resultados finales:\n  - Carpetas con Fecha Última: {total_dates}\n  - Carpetas con Comuna: {total_comunas}")
    print("Muestras Fechas:", sample_dates)
    print("Muestras Comunas:", sample_comunas)

    con.close()

if __name__ == "__main__":
    migrate()
