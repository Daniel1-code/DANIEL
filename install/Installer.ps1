<#
.SYNOPSIS
    Installe DanCI Structural Studio pour Revit 2026 (utilisateur courant).

.DESCRIPTION
    Copie les assemblies et le manifeste .addin dans le dossier des complements de
    Revit 2026. Aucun droit administrateur n'est necessaire. L'ancien plugin
    "Armatures de poteaux" est desinstalle automatiquement s'il est present.

.PARAMETER Source
    Dossier contenant DanCI.Structural.App.dll et DanCI.Structural.addin.
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
$pluginDir   = Join-Path $addinsRoot 'DanCI Structural Studio'
$manifestDst = Join-Path $addinsRoot 'DanCI.Structural.addin'

# Traces de la version precedente du produit, renommee en DanCI Structural Studio.
$legacyManifest = Join-Path $addinsRoot 'ArmaturesPoteaux.addin'
$legacyDir      = Join-Path $addinsRoot 'ArmaturesPoteaux'

function Remove-Legacy {
    if (Test-Path $legacyManifest) {
        Remove-Item $legacyManifest -Force
        Write-Host 'Ancien plugin "Armatures de poteaux" desinstalle.' -ForegroundColor Yellow
    }
    if (Test-Path $legacyDir) { Remove-Item $legacyDir -Recurse -Force }
}

if ($Uninstall) {
    if (Test-Path $manifestDst) { Remove-Item $manifestDst -Force }
    if (Test-Path $pluginDir)   { Remove-Item $pluginDir -Recurse -Force }
    Remove-Legacy
    Write-Host 'DanCI Structural Studio desinstalle. Redemarrez Revit.' -ForegroundColor Green
    return
}

$dllSrc      = Join-Path $Source 'DanCI.Structural.App.dll'
$manifestSrc = Join-Path $Source 'DanCI.Structural.addin'

foreach ($file in @($dllSrc, $manifestSrc)) {
    if (-not (Test-Path $file)) {
        throw "Fichier introuvable : $file. Lancez le script depuis le dossier de publication."
    }
}

Remove-Legacy
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null

# Les fichiers telecharges depuis Internet sont bloques par Windows : Revit refuserait la DLL.
Get-ChildItem -Path $Source -File | Unblock-File -ErrorAction SilentlyContinue

Copy-Item $manifestSrc $manifestDst -Force
Get-ChildItem -Path $Source -Filter '*.dll' -File |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $pluginDir $_.Name) -Force }
Get-ChildItem -Path $Source -Filter '*.deps.json' -File -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $pluginDir $_.Name) -Force }

Write-Host "DanCI Structural Studio installe dans : $pluginDir" -ForegroundColor Green
Write-Host "Manifeste : $manifestDst" -ForegroundColor Green
Write-Host 'Redemarrez Revit 2026 : l onglet "DanCI Structural Studio" apparaitra dans le ruban.' -ForegroundColor Green
