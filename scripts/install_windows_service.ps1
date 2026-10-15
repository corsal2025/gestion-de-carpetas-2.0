<#
.SYNOPSIS
    Instala Sgl.Web (Gestión de Carpetas 2.0) como Servicio de Windows.
.DESCRIPTION
    Publica la aplicación en modo Release y registra el servicio usando New-Service / sc.exe
    con inicio automático y recuperación ante fallos.
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "SglGestionCarpetas",
    [string]$DisplayName = "Gestión de Carpetas 2.0 (SGL)",
    [string]$PublishDir = "$PSScriptRoot\..\publish",
    [int]$Port = 5000
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

# 3. Registrar el servicio
Write-Host "[3/4] Registrando servicio en Windows..." -ForegroundColor Green
New-Service -Name $ServiceName `
            -BinaryPathName "`"$exePath`" --urls=http://*:$Port" `
            -DisplayName $DisplayName `
            -Description "Servicio web autónomo para la Gestión de Carpetas 2.0 (Dirección de Tránsito)" `
            -StartupType Automatic

# Configurar recuperación ante fallos (reinicio automático a los 5 segundos)
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

# 4. Iniciar servicio
Write-Host "[4/4] Iniciando servicio..." -ForegroundColor Green
Start-Service -Name $ServiceName

$status = (Get-Service -Name $ServiceName).Status
Write-Host "Servicio '$ServiceName' instalado e iniciado con estado: $status" -ForegroundColor Cyan
Write-Host "Acceso web disponible en: http://localhost:$Port" -ForegroundColor Green
