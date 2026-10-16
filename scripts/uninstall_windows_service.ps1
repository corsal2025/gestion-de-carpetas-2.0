<#
.SYNOPSIS
    Desinstala el Servicio de Windows de Gestión de Carpetas 2.0 (Sgl.Web).
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "SglGestionCarpetas"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Este script requiere ejecutarse con privilegios de Administrador. Abra PowerShell como Administrador."
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Yellow
Write-Host " Desinstalador de Servicio de Windows: $ServiceName" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "El servicio '$ServiceName' no se encuentra registrado en el sistema." -ForegroundColor Cyan
    exit 0
}

Write-Host "Deteniendo servicio..." -ForegroundColor Yellow
if ($existing.Status -eq 'Running') {
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
}

Write-Host "Eliminando registro del servicio..." -ForegroundColor Yellow
sc.exe delete $ServiceName | Out-Null
Start-Sleep -Seconds 1

Write-Host "Servicio '$ServiceName' desinstalado correctamente." -ForegroundColor Green
