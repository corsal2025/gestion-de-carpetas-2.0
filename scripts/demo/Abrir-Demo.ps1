param(
    [string]$Carpeta = (Join-Path $PSScriptRoot '../../temp/demo-local'),
    [ValidateRange(1024,65535)][int]$Puerto = 56552
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Esta guía de aislamiento está verificada solo para Windows y PowerShell 7.' }
$overrides = Get-ChildItem Env: | Where-Object { $_.Name -match '^(ConnectionStrings|Database|Peticion|CambioDomicilio|Certificados|Seguridad)__' }
if ($overrides) { throw 'Hay variables de configuración externas. Use una terminal limpia para la DEMO; no se mostrarán sus valores.' }
$demoRoot = (Resolve-Path -LiteralPath $Carpeta).Path
$publishRoot = Join-Path $demoRoot 'publicacion'
$dll = Join-Path $publishRoot 'Sgl.Web.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Primero publique la aplicación como indica LEEME.md.' }
if (Test-Path -LiteralPath (Join-Path $demoRoot 'appsettings.Local.json')) { throw 'La demo no admite appsettings.Local.json.' }
$config = Get-Content -LiteralPath (Join-Path $demoRoot 'appsettings.json') -Raw | ConvertFrom-Json
if ($config.Certificados.EnvioCorreoActivo -ne $false -or $config.CambioDomicilio.EnvioReal -ne $false -or $config.Peticion.Ews.Url -ne 'https://127.0.0.1:1/EWS/Exchange.asmx') { throw 'Configuración de correo no segura para DEMO.' }
$db = [IO.Path]::GetFullPath((Join-Path $demoRoot 'demo.db'))
if ($config.ConnectionStrings.Sgl -ne "Data Source=$db") { throw 'La conexión debe apuntar a demo.db dentro de la carpeta DEMO.' }
$appData = Join-Path $demoRoot 'appdata'
if (Test-Path -LiteralPath (Join-Path $appData 'Microsoft/UserSecrets')) { throw 'La carpeta appdata DEMO debe estar libre de user-secrets.' }
$previousAppData = $env:APPDATA
try {
    $env:APPDATA = $appData
    Write-Host "DEMO con datos ficticios: http://127.0.0.1:$Puerto — Ctrl+C para detener."
    & dotnet $dll --contentRoot $demoRoot --webroot (Join-Path $publishRoot 'wwwroot') --urls "http://127.0.0.1:$Puerto" --environment Production
    if ($LASTEXITCODE -ne 0) { throw "El servidor terminó con código $LASTEXITCODE." }
}
finally { $env:APPDATA = $previousAppData }
