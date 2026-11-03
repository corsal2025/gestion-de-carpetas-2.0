<#
Recepción adaptada del Organizador de citaciones de este equipo.
Lee adjuntos de Outlook clásico y los guarda en entrada-jefatura.
No envía, mueve, elimina ni marca mensajes. Conecte esa carpeta desde /importar.
Ejemplo: .\scripts\Recibir-AgendasJefatura.ps1 -Remitente 'jefatura@dominio.cl'
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 90)][int]$Dias = 5,
    [string]$Remitente = '',
    [string]$Subcarpeta = '',
    [string]$Destino = (Join-Path $PSScriptRoot '..\entrada-jefatura')
)
$ErrorActionPreference = 'Stop'
$Destino = [IO.Path]::GetFullPath($Destino)
[IO.Directory]::CreateDirectory($Destino) | Out-Null
try { $outlook = New-Object -ComObject Outlook.Application }
catch { throw 'Se necesita Outlook de escritorio clásico. También puede guardar los adjuntos manualmente en la carpeta de entrada.' }
$mensajes = $outlook.GetNamespace('MAPI').GetDefaultFolder(6)
if ($Subcarpeta) { $mensajes = $mensajes.Folders.Item($Subcarpeta) }
$items = $mensajes.Items
$items.Sort('[ReceivedTime]', $true)
$desde = (Get-Date).AddDays(-$Dias)
$guardados = 0; $existentes = 0
foreach ($mensaje in $items) {
    if ($mensaje.Class -ne 43) { continue }
    if ($mensaje.ReceivedTime -lt $desde) { break }
    if ($Remitente -and $mensaje.SenderEmailAddress -notlike "*$Remitente*" -and $mensaje.SenderName -notlike "*$Remitente*") { continue }
    foreach ($adjunto in $mensaje.Attachments) {
        $nombre = $adjunto.FileName
        if ($nombre -notlike 'citas_*' -or $nombre -notmatch '\.(csv|xlsx)$') { continue }
        $nombre = $nombre -replace '[\\/:*?"<>|]', '_'
        $salida = Join-Path $Destino ('{0:yyyyMMdd_HHmmss}_{1}' -f $mensaje.ReceivedTime, $nombre)
        if (Test-Path -LiteralPath $salida) { $existentes++; continue }
        $adjunto.SaveAsFile($salida)
        $guardados++
    }
}
Write-Output "Recepción finalizada: $guardados adjuntos nuevos, $existentes ya descargados."
Write-Output "Conecte esta carpeta en el sistema: $Destino"
