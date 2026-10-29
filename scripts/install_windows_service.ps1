<#
.SYNOPSIS
    Instala Sgl.Web (Gestión de Carpetas 2.0) como Servicio de Windows.
.DESCRIPTION
    Publica la aplicación en modo Release y registra el servicio usando New-Service / sc.exe
    con inicio automático y recuperación ante fallos.
    Por defecto el servicio escucha SOLO en localhost; use -BindTodas para publicarlo en la red (LAN).
    Sin -Credential el servicio corre como LocalSystem (cuenta con privilegios altos): se recomienda
    una cuenta de servicio dedicada con permisos solo sobre la carpeta publicada y sus respaldos.
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "SglGestionCarpetas",
    [string]$DisplayName = "Gestión de Carpetas 2.0 (SGL)",
    [string]$PublishDir = "$PSScriptRoot\..\publish",
    [int]$Port = 5000,
    # Escucha en todas las interfaces (http://*:puerto). Sin esto, solo http://localhost:puerto.
    [switch]$BindTodas,
    # Cuenta con la que corre el servicio (recomendado: una cuenta dedicada, no LocalSystem).
    [System.Management.Automation.PSCredential]$Credential,
    # Continuar sin preguntar aunque no exista sgl.db en la carpeta publicada.
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Requiere privilegios de Administrador
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Este script requiere ejecutarse con privilegios de Administrador. Abra PowerShell como Administrador."
    exit 1
}

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$publishPath = [System.IO.Path]::GetFullPath($PublishDir)
$webProj = Join-Path $repoRoot "src\Sgl.Web\Sgl.Web.csproj"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Instalador de Servicio de Windows: $DisplayName" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Detener servicio si ya existe
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[1/4] Deteniendo y eliminando servicio previo '$ServiceName'..." -ForegroundColor Yellow
    if ($existing.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 2. Publicar binarios
Write-Host "[2/4] Publicando aplicación en $publishPath..." -ForegroundColor Green
dotnet publish "$webProj" -c Release -o "$publishPath" --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Error "Fallo la publicación de dotnet publish."
    exit $LASTEXITCODE
}

$exePath = Join-Path $publishPath "Sgl.Web.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "No se encontró el ejecutable en $exePath"
    exit 1
}

# 2b. Verificar la base de datos: el servicio usa sgl.db junto al ejecutable. Si no está, arrancaría
# con una base NUEVA y vacía y el equipo creería que se perdieron las carpetas.
$dbPublicada = Join-Path $publishPath "sgl.db"
if (-not (Test-Path $dbPublicada)) {
    Write-Warning "No existe $dbPublicada. El servicio crearía una base de datos NUEVA y vacía."
    Write-Warning "Detenga el sistema actual y copie su sgl.db a esa carpeta antes de continuar."
    if (-not $Force) {
        $respuesta = Read-Host "¿Continuar de todos modos? (s/N)"
        if ($respuesta -notin @("s", "S")) {
            Write-Host "Instalación cancelada." -ForegroundColor Yellow
            exit 1
        }
    }
}

# 3. Registrar el servicio
$bind = if ($BindTodas) { "*" } else { "localhost" }
if ($BindTodas) {
    Write-Warning "El servicio quedará accesible desde toda la red (http://*:$Port) y la aplicación no tiene login: restrinja el acceso con el firewall."
}
if (-not $Credential) {
    Write-Warning "El servicio correrá como LocalSystem. Use -Credential con una cuenta de servicio dedicada para reducir privilegios."
}
Write-Host "[3/4] Registrando servicio en Windows (escucha en ${bind}:$Port)..." -ForegroundColor Green
$serviceArgs = @{
    Name           = $ServiceName
    BinaryPathName = "`"$exePath`" --urls=http://${bind}:$Port"
    DisplayName    = $DisplayName
    Description    = "Servicio web autónomo para la Gestión de Carpetas 2.0 (Dirección de Tránsito)"
    StartupType    = "Automatic"
}
if ($Credential) { $serviceArgs.Credential = $Credential }
New-Service @serviceArgs | Out-Null

# Configurar recuperación ante fallos (reinicio automático a los 5 segundos)
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

# 4. Iniciar servicio
Write-Host "[4/4] Iniciando servicio..." -ForegroundColor Green
Start-Service -Name $ServiceName

$status = (Get-Service -Name $ServiceName).Status
Write-Host "Servicio '$ServiceName' instalado e iniciado con estado: $status" -ForegroundColor Cyan
Write-Host "Acceso web disponible en: http://localhost:$Port" -ForegroundColor Green
