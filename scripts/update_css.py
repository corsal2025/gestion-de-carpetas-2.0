import re

with open('src/Sgl.Web/wwwroot/app.css', 'r', encoding='utf-8') as f:
    css = f.read()

# 1. Update .data-table, .data-table th, .data-table td, and sticky columns
css = re.sub(
    r'\.data-table\s*\{[^}]*width:\s*100%;[^}]*font-size:\s*0\.88rem;[^}]*\}',
    '''.data-table {
    width: 100%;
    border-collapse: separate;
    border-spacing: 0;
    font-size: 0.74rem;
    white-space: nowrap;
}''',
    css
)

css = re.sub(
    r'\.data-table\s+th\s*\{[^}]*position:\s*sticky;[^}]*padding:\s*0\.75rem\s+1rem;[^}]*\}',
    '''.data-table th {
    position: sticky !important;
    top: 0 !important;
    z-index: 45 !important;
    background: #f1f5f9 !important;
    color: #1e293b !important;
    font-weight: 700;
    text-align: center !important;
    vertical-align: middle;
    padding: 0.32rem 0.38rem !important;
    border-top: none;
    border-bottom: 2px solid #cbd5e1 !important;
    font-size: 0.72rem !important;
    text-transform: uppercase !important;
    letter-spacing: 0.02em;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.08);
}''',
    css
)

css = re.sub(
    r'\.data-table\s+td\s*\{[^}]*padding:\s*0\.75rem\s+1rem;[^}]*\}',
    '''.data-table td {
    padding: 0.25rem 0.32rem !important;
    border-bottom: 1px solid var(--border-color);
    color: var(--text-primary);
    background-color: #ffffff;
    font-size: 0.74rem !important;
    font-weight: 600 !important;
    text-transform: uppercase !important;
}''',
    css
)

# Sticky columns coordinates
css = re.sub(
    r'\.col-sticky-check\s*\{[^}]*width:\s*44px[^}]*\}',
    '''.col-sticky-check {
    position: sticky !important;
    left: 0 !important;
    width: 32px !important;
    min-width: 32px !important;
    max-width: 32px !important;
    text-align: center !important;
    box-sizing: border-box;
}''',
    css
)

css = re.sub(
    r'\.col-sticky-rut\s*\{[^}]*width:\s*145px[^}]*\}',
    '''.col-sticky-rut {
    position: sticky !important;
    left: 32px !important;
    width: 102px !important;
    min-width: 102px !important;
    max-width: 102px !important;
    text-align: center !important;
    box-sizing: border-box;
}''',
    css
)

css = re.sub(
    r'\.col-sticky-nombre\s*\{[^}]*width:\s*290px[^}]*\}',
    '''.col-sticky-nombre {
    position: sticky !important;
    left: 134px !important; /* 32px + 102px */
    width: 185px !important;
    min-width: 170px !important;
    max-width: 200px !important;
    box-sizing: border-box;
    border-right: 2px solid #cbd5e1 !important;
    box-shadow: 4px 0 8px -2px rgba(0, 0, 0, 0.15) !important;
}''',
    css
)

# Sticky headers intersection priority
css = re.sub(
    r'\.data-table\s+th\.col-sticky-check,\s*\.data-table\s+th\.col-sticky-rut,\s*\.data-table\s+th\.col-sticky-nombre\s*\{[^}]*z-index:\s*60\s*!important;[^}]*\}',
    '''.data-table th.col-sticky-check,
.data-table th.col-sticky-rut,
.data-table th.col-sticky-nombre {
    position: sticky !important;
    top: 0 !important;
    z-index: 75 !important;
    background: #e2e8f0 !important;
    color: #1e293b !important;
    border-bottom: 2px solid #94a3b8 !important;
}''',
    css
)

# Global uppercase rule
css = re.sub(
    r'/\* 1\. Fuente y tamaño uniforme en toda la tabla \*/\s*\.data-table,\s*\.data-table\s+th,\s*\.data-table\s+td,\s*\.data-table\s+td\s+\*,\s*\.data-table\s+input,\s*\.data-table\s+select,\s*\.data-table\s+span\s*\{[^}]*\}',
    '''/* 1. Fuente y tamaño uniforme en toda la tabla (SIEMPRE EN MAYÚSCULAS) */
.data-table,
.data-table th,
.data-table td,
.data-table td *,
.data-table input,
.data-table select,
.data-table option,
.data-table span,
.data-table button,
.data-table a {
    font-family: 'Inter', -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important;
    text-transform: uppercase !important;
}''',
    css
)

# Inputs, names, dates, selects compactness
css = re.sub(
    r'\.name-cell\s*\{[^}]*min-width:\s*230px;[^}]*\}',
    '''.name-cell {
    font-weight: 600 !important;
    font-size: 0.74rem !important;
    color: inherit !important;
    white-space: normal;
    min-width: 150px;
    max-width: 165px;
    line-height: 1.25;
}''',
    css
)

css = re.sub(
    r'\.sede-tag\s*\{[^}]*font-size:\s*0\.88rem\s*!important;[^}]*\}',
    '''.sede-tag {
    font-size: 0.72rem !important;
    font-weight: 600 !important;
    color: inherit !important;
    white-space: nowrap;
}''',
    css
)

css = re.sub(
    r'\.date-cell\s*\{[^}]*font-size:\s*0\.88rem\s*!important;[^}]*\}',
    '''.date-cell {
    font-size: 0.72rem !important;
    font-weight: 600 !important;
    font-variant-numeric: tabular-nums;
    color: inherit !important;
    white-space: nowrap;
}''',
    css
)

css = re.sub(
    r'\.excel-rut-input\s*\{[^}]*width:\s*105px;[^}]*\}',
    '''.excel-rut-input {
    background: transparent !important;
    color: inherit !important;
    font-family: inherit !important;
    font-size: 0.74rem !important;
    font-weight: 700 !important;
    border: 1px solid transparent !important;
    border-radius: 4px;
    padding: 2px 4px;
    width: 88px;
    transition: all 0.15s ease;
}''',
    css
)

css = re.sub(
    r'\.date-input-inline\s*\{[^}]*width:\s*140px;[^}]*\}',
    '''.date-input-inline {
    background: transparent !important;
    color: inherit !important;
    -webkit-text-fill-color: inherit !important;
    font-family: inherit !important;
    font-size: 0.72rem !important;
    font-weight: 600 !important;
    border: 1px solid transparent !important;
    border-radius: 4px;
    padding: 2px 4px;
    outline: none;
    cursor: pointer;
    transition: all 0.15s ease;
    width: 90px;
}''',
    css
)

css = re.sub(
    r'\.comuna-input-inline\s*\{[^}]*width:\s*130px;[^}]*\}',
    '''.comuna-input-inline {
    background: transparent !important;
    color: inherit !important;
    -webkit-text-fill-color: inherit !important;
    font-family: inherit !important;
    font-size: 0.72rem !important;
    font-weight: 600 !important;
    border: 1px solid transparent !important;
    border-radius: 4px;
    padding: 2px 4px;
    outline: none;
    width: 76px;
    transition: all 0.15s ease;
}''',
    css
)

css = re.sub(
    r'\.data-table\s+tbody\s+td\s+select\.excel-select\s*\{[^}]*font-size:\s*0\.84rem;[^}]*\}',
    '''.data-table tbody td select.excel-select {
    background: #ffffff !important;
    color: #000000 !important;
    font-weight: 700 !important;
    border: 1px solid rgba(0, 0, 0, 0.25) !important;
    border-radius: 4px;
    padding: 2px 4px;
    font-size: 0.72rem !important;
    cursor: pointer;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.08);
    transition: border-color 0.2s, box-shadow 0.2s;
    width: 135px;
    max-width: 140px;
}''',
    css
)

css = re.sub(
    r'\.sector-header\s*\{[^}]*min-width:\s*95px;[^}]*\}',
    '''.sector-header {
    min-width: 75px;
    text-align: center;
}''',
    css
)

css = re.sub(
    r'\.data-table\s+tbody\s+tr\s+td\s+\.sector-cell,\s*\.data-table\s+tbody\s+tr\s+td\s+span\.sector-cell,\s*\.sector-cell\s*\{[^}]*font-size:\s*0\.84rem;[^}]*\}',
    '''.data-table tbody tr td .sector-cell,
.data-table tbody tr td span.sector-cell,
.sector-cell {
    display: inline-block;
    padding: 0.15rem 0.45rem;
    border-radius: 4px;
    font-size: 0.70rem !important;
    font-weight: 800 !important;
    letter-spacing: 0.02em;
    text-align: center;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.12);
}''',
    css
)

with open('src/Sgl.Web/wwwroot/app.css', 'w', encoding='utf-8') as f:
    f.write(css)

print('Successfully applied all CSS updates!')
