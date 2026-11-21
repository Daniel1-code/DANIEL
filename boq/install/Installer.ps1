<#
.SYNOPSIS
    Installe DanCI - Metre & BOQ pour l'utilisateur courant (aucun droit administrateur).

.DESCRIPTION
    A lancer depuis le dossier decompresse du paquet (DanCI-BOQ-<version>-Revit<annee>.zip).
    L'annee de Revit est lue dans le manifeste DanCI.BOQ.addin du paquet. Copie :
      DanCI.BOQ.addin  -> %AppData%\Autodesk\Revit\Addins\<annee>\
      DanCI.BOQ\       -> %AppData%\Autodesk\Revit\Addins\<annee>\DanCI.BOQ\
    Les fichiers telecharges sont debloques (sinon Revit refuse de charger les DLL).

.PARAMETER Uninstall
    Desinstalle l'extension pour l'annee du paquet.

.EXAMPLE
    Double-cliquer sur Installer.cmd
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Installer.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Source,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

# Dossier du paquet. $PSScriptRoot n'est pas fiable en valeur par defaut d'un parametre sous
# Windows PowerShell 5.1 (il peut y etre vide) : on le resout ici, avec deux replis.
if ([string]::IsNullOrWhiteSpace($Source)) { $Source = $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($Source) -and $MyInvocation.MyCommand.Path) {
    $Source = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrWhiteSpace($Source)) { $Source = (Get-Location).Path }
$Source = $Source.TrimEnd('\', '/')
Write-Host "Paquet : $Source"

$manifestSrc = Join-Path -Path $Source -ChildPath 'DanCI.BOQ.addin'
$payloadSrc  = Join-Path -Path $Source -ChildPath 'DanCI.BOQ'
if (-not (Test-Path $manifestSrc)) {
    throw "DanCI.BOQ.addin introuvable dans $Source. Decompressez d'abord le .zip, puis lancez l'installeur depuis le dossier obtenu."
}

$match = Select-String -LiteralPath $manifestSrc -Pattern 'manifeste pour Revit (\d{4})' | Select-Object -First 1
if (-not $match) { throw "Annee de Revit introuvable dans $manifestSrc." }
$year = $match.Matches[0].Groups[1].Value

$appData = $env:APPDATA
if ([string]::IsNullOrWhiteSpace($appData)) { $appData = [Environment]::GetFolderPath('ApplicationData') }
if ([string]::IsNullOrWhiteSpace($appData)) { throw "Dossier AppData de l'utilisateur introuvable." }
$addinsRoot  = Join-Path -Path $appData -ChildPath "Autodesk\Revit\Addins\$year"
$manifestDst = Join-Path $addinsRoot 'DanCI.BOQ.addin'
$payloadDst  = Join-Path $addinsRoot 'DanCI.BOQ'

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    throw "Revit est ouvert : fermez Revit (toutes les versions) puis relancez l'installeur."
}

if ($Uninstall) {
    if (Test-Path $manifestDst) { Remove-Item $manifestDst -Force }
    if (Test-Path $payloadDst)  { Remove-Item $payloadDst -Recurse -Force }
    Write-Host "DanCI - Metre & BOQ desinstalle de Revit $year." -ForegroundColor Green
    return
}

if (-not (Test-Path (Join-Path $payloadSrc 'DanCI.BOQ.Addin.dll'))) {
    throw "Paquet incomplet : DanCI.BOQ\DanCI.BOQ.Addin.dll manquant."
}

# Fichiers venant d'Internet : Windows les marque, et Revit refuse alors de charger les DLL.
Get-ChildItem -LiteralPath $Source -Recurse -File | Unblock-File

New-Item -ItemType Directory -Path $addinsRoot -Force | Out-Null
if (Test-Path $payloadDst) { Remove-Item $payloadDst -Recurse -Force }
Copy-Item $payloadSrc $payloadDst -Recurse -Force
Copy-Item $manifestSrc $manifestDst -Force

Write-Host ''
Write-Host "DanCI - Metre & BOQ installe pour Revit $year :" -ForegroundColor Green
Write-Host "  $manifestDst"
Write-Host "  $payloadDst"
Write-Host ''
Write-Host "Demarrez Revit $year : onglet DanCI > panneau Metre & BOQ > A propos."
Write-Host "Si Revit demande s'il faut charger l'extension, choisissez 'Toujours charger'."
