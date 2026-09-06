<#
.SYNOPSIS
    Installe le plugin "Armatures de poteaux" pour Revit 2026 (utilisateur courant).

.DESCRIPTION
    Copie ArmaturesPoteaux.dll et le manifeste .addin dans le dossier des complements
    de Revit 2026. Aucun droit administrateur n'est necessaire.

.PARAMETER Source
    Dossier contenant ArmaturesPoteaux.dll et ArmaturesPoteaux.addin.
    Par defaut : le dossier du script.

.PARAMETER Uninstall
    Supprime le plugin au lieu de l'installer.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Installer.ps1
#>

[CmdletBinding()]
param(
    [string]$Source = $PSScriptRoot,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$addinsRoot  = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
$pluginDir   = Join-Path $addinsRoot 'ArmaturesPoteaux'
$manifestDst = Join-Path $addinsRoot 'ArmaturesPoteaux.addin'

if ($Uninstall) {
    if (Test-Path $manifestDst) { Remove-Item $manifestDst -Force }
    if (Test-Path $pluginDir)   { Remove-Item $pluginDir -Recurse -Force }
    Write-Host 'Plugin desinstalle. Redemarrez Revit.' -ForegroundColor Green
    return
}

$dllSrc      = Join-Path $Source 'ArmaturesPoteaux.dll'
$manifestSrc = Join-Path $Source 'ArmaturesPoteaux.addin'

foreach ($file in @($dllSrc, $manifestSrc)) {
    if (-not (Test-Path $file)) {
        throw "Fichier introuvable : $file. Lancez le script depuis le dossier de publication."
    }
}

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null

# Les fichiers telecharges depuis Internet sont bloques par Windows : Revit refuserait la DLL.
Get-ChildItem -Path $Source -File | Unblock-File -ErrorAction SilentlyContinue

Copy-Item $manifestSrc $manifestDst -Force
Get-ChildItem -Path $Source -Filter '*.dll' -File |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $pluginDir $_.Name) -Force }

$json = Join-Path $Source 'ArmaturesPoteaux.deps.json'
if (Test-Path $json) { Copy-Item $json (Join-Path $pluginDir 'ArmaturesPoteaux.deps.json') -Force }

Write-Host "Plugin installe dans : $pluginDir" -ForegroundColor Green
Write-Host "Manifeste : $manifestDst" -ForegroundColor Green
Write-Host 'Redemarrez Revit 2026, l onglet "Beton arme" apparaitra dans le ruban.' -ForegroundColor Green
