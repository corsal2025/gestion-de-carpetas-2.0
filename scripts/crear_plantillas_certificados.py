"""
Derives the three clean certificate templates from the department's Word models.

Sources (read only, never modified or copied; they hold a real person's sample data):
  - "Plantilla Certificados 2026 (sin carpeta).docx"
  - "Plantilla Certificados 2026 (Registro Civil).docx"
  - "Plantilla Oficio 2026.docx"

Output: src/Sgl.Infrastructure/Documentos/Plantillas/{sin-carpeta,registro-civil,oficio}.docx
(embedded resources of Sgl.Infrastructure). Header, footer, styles, fonts, images and fixed text are kept; only the
sample values are replaced by {{PLACEHOLDERS}}. Word splits a sentence across many runs, so each replacement works on
the paragraph text and maps it back onto the runs: the new text goes into the first run the match touched (keeping
its formatting) and the other touched runs lose only the matched characters.

The sample values are NOT written here: they are found by the structure around them (anchors such as "Don ... , RUN"),
so this file holds no personal data. Usage:

    python -I scripts/crear_plantillas_certificados.py <folder-with-the-three-models> [output-folder]

Afterwards run the template tests (PlantillasLimpiasTests) which scan the outputs for leftover RUT/RUN patterns.
"""
import re
import sys
import zipfile
from pathlib import Path

from lxml import etree

W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space"
NS = {"w": W}

SOURCES = {
    "sin-carpeta.docx": "Plantilla Certificados 2026 (sin carpeta).docx",
    "registro-civil.docx": "Plantilla Certificados 2026 (Registro Civil).docx",
    "oficio.docx": "Plantilla Oficio 2026.docx",
}

NAME = r"[A-ZÁÉÍÓÚÜÑ][A-ZÁÉÍÓÚÜÑ .'-]+?"
RUT = r"\d{1,2}\.\d{3}\.\d{3}-[\dkK]"


def para_runs(p):
    """The w:t elements of a paragraph in document order."""
    return p.xpath(".//w:t", namespaces=NS)


def text_of(p):
    return "".join(t.text or "" for t in para_runs(p)).strip()


def raw_text_of(p):
    return "".join(t.text or "" for t in para_runs(p))


def replace_in_paragraph(p, pattern, replacement, group=0, flags=0):
    """Replaces regex group `group` of the first match on the paragraph text. Returns True when it matched."""
    ts = para_runs(p)
    text = "".join(t.text or "" for t in ts)
    m = re.search(pattern, text, flags)
    if not m:
        return False
    start, end = m.span(group)
    pos = 0
    first = True
    for t in ts:
        s = t.text or ""
        lo, hi = pos, pos + len(s)
        pos = hi
        a, b = max(start, lo), min(end, hi)
        if a >= b:
            continue
        t.text = s[: a - lo] + (replacement if first else "") + s[b - lo:]
        t.set(XML_SPACE, "preserve")
        first = False
    return not first


def set_paragraph_text(p, text):
    """Whole paragraph text := text, written in the first run (keeps its formatting), the rest emptied."""
    for i, t in enumerate(para_runs(p)):
        t.text = text if i == 0 else ""
        t.set(XML_SPACE, "preserve")


def must(ok, what):
    if not ok:
        raise SystemExit(f"Template anchor not found: {what}. The Word model changed; review this script.")


def body_paragraphs(root):
    return root.xpath("//w:body//w:p", namespaces=NS)


def sin_carpeta(root):
    ps = body_paragraphs(root)
    main = next(p for p in ps if "otorgó la licencia" in raw_text_of(p))
    must(replace_in_paragraph(main, rf"^\s*({NAME})\s*,\s*director", "{{DIRECTOR_NOMBRE}}", 1), "director name")
    must(replace_in_paragraph(main, r"\bdirector\s*\(\s*[sS]\s*\)", "{{DIRECTOR_CARGO}}"), "director cargo")
    must(replace_in_paragraph(main, rf"\bDon\s+({NAME})\s*,\s*RUN", "{{NOMBRE}}", 1), "citizen name")
    must(replace_in_paragraph(main, rf"RUN\s*({RUT})", "{{RUN}}", 1), "citizen RUN")
    must(replace_in_paragraph(main, r"en el año\s+(\d{4})", "{{ANIO_RENOVACION}}", 1), "renewal year")
    i = ps.index(next(p for p in ps if text_of(p).startswith("Firma como ministro")))
    after = [p for p in ps[i + 1:] if text_of(p)]
    must(len(after) >= 2, "secretaria name and cargo")
    set_paragraph_text(after[0], "{{SECRETARIA_NOMBRE}}")
    set_paragraph_text(after[1], "{{SECRETARIA_CARGO}}")


def registro_civil(root):
    ps = body_paragraphs(root)
    main = next(p for p in ps if "registra en el sistema" in raw_text_of(p))
    must(replace_in_paragraph(main, rf"^\s*({NAME})\s*,\s*director", "{{DIRECTOR_NOMBRE}}", 1), "director name")
    must(replace_in_paragraph(main, r"\bdirector\s*\(\s*[sS]\s*\)", "{{DIRECTOR_CARGO}}"), "director cargo")
    must(replace_in_paragraph(main, rf"\bDON\s+({NAME})\s*RUN", "{{NOMBRE}}", 1), "citizen name")
    must(replace_in_paragraph(main, rf"RUN\s*({RUT})", "{{RUN}}", 1), "citizen RUN")
    must(replace_in_paragraph(main, r"Clase\s+(\S+)\s*\(", "{{CLASE}}", 1), "class")
    must(replace_in_paragraph(main, r"Ley N°\s*([\d.]+)\)", "{{LEY}}", 1), "law")
    must(replace_in_paragraph(main, r"con fecha\s+(.+?)\.\s*$", "{{FECHA_OTORGAMIENTO}}", 1), "grant date")
    fecha = next(p for p in ps if re.fullmatch(r"\d{1,2} de [a-záéíóú]+ del \d{4}", text_of(p)))
    set_paragraph_text(fecha, "{{FECHA_EMISION}}")


def oficio(root):
    ps = body_paragraphs(root)
    head = next(p for p in ps if "RDINARIO" in raw_text_of(p))
    must(replace_in_paragraph(head, r"Nº\s*(\d+\s*/\s*\d{4})", "{{NUMERO_OFICIO}}", 1), "oficio number")
    fecha = next(p for p in ps if text_of(p).startswith("Valpa"))
    must(replace_in_paragraph(fecha, r"Valparaíso,\s*(_+\s*/?)", "{{FECHA}}", 1), "date")
    body = next(p for p in ps if "se solicita" in raw_text_of(p) and "contribuyente" in raw_text_of(p))
    must(replace_in_paragraph(body, r"licencia clase\s+(\S+?)\s+que", "{{CLASE}}", 1), "class")
    must(replace_in_paragraph(body, rf"contribuyente\s+({NAME})\s*RUT", "{{NOMBRE}}", 1), "citizen name")
    must(replace_in_paragraph(body, rf"RUT\.?\s*({RUT})", "{{RUT}}", 1), "citizen RUT")
    motivo = next(p for p in ps if "fue denegada" in raw_text_of(p))
    must(replace_in_paragraph(motivo, r"fue denegada por\s+(.+?)\.\s*Por ende", "{{MOTIVO}}", 1), "motive")
    j = next(i for i, p in enumerate(ps) if text_of(p).upper().startswith("DIRECTOR(S)"))
    set_paragraph_text(ps[j], "{{DIRECTOR_CARGO}}")
    k = j - 1
    while not text_of(ps[k]):
        k -= 1
    set_paragraph_text(ps[k], "{{DIRECTOR_NOMBRE}}")
    # Staff initials line ("AAA/BBB/ccc"): tied to the people of the model, left empty on purpose.
    for p in ps:
        if re.fullmatch(r"[A-Z]{2,4}/[A-Z]{2,4}/[a-z]{2,4}", text_of(p)):
            set_paragraph_text(p, "")


PARTS = {
    "sin-carpeta.docx": sin_carpeta,
    "registro-civil.docx": registro_civil,
    "oficio.docx": oficio,
}


def build(src: Path, dst: Path, transform):
    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == "word/document.xml":
                root = etree.fromstring(data)
                transform(root)
                data = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
            elif item.filename == "docProps/core.xml":
                root = etree.fromstring(data)
                for el in root:
                    if etree.QName(el).localname in ("creator", "lastModifiedBy"):
                        el.text = ""
                data = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
            zout.writestr(item, data)


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    folder = Path(sys.argv[1])
    default_out = Path(__file__).resolve().parent.parent / "src" / "Sgl.Infrastructure" / "Documentos" / "Plantillas"
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else default_out
    out.mkdir(parents=True, exist_ok=True)
    for name, source in SOURCES.items():
        build(folder / source, out / name, PARTS[name])
        print("written", out / name)


if __name__ == "__main__":
    main()
