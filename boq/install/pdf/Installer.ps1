<#
.SYNOPSIS
    Installe les composants PDF de DanCI - Metre & BOQ (une seule fois, toutes versions de Revit).

.DESCRIPTION
    Copie questpdf_skia.dll et questpdf_fonts_lato.br dans le dossier DanCI.BOQ de chaque
    version de Revit ou l'extension est deja installee (%AppData%\Autodesk\Revit\Addins\<annee>).
    A lancer APRES l'installation de l'extension. Aucun droit administrateur necessaire.
#>
[CmdletBinding()]
param([string]$Source)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Source)) { $Source = $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($Source) -and $MyInvocation.MyCommand.Path) { $Source = Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrWhiteSpace($Source)) { $Source = (Get-Location).Path }
$Source = $Source.TrimEnd('\', '/')
Write-Host "Paquet : $Source"

# Les composants PDF sont livres en deux parties (fichiers plus legers a telecharger) :
# chaque partie installe les fichiers qu'elle contient.
$all = @('questpdf_skia.dll', 'questpdf_fonts_lato.br')
$files = @($all | Where-Object { Test-Path -LiteralPath (Join-Path $Source $_) })
if ($files.Count -eq 0) {
    throw "Aucun composant PDF dans $Source. Decompressez d'abord le .zip, puis lancez l'installeur depuis le dossier obtenu."
}
if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    throw "Revit est ouvert : fermez Revit puis relancez l'installeur."
}

Get-ChildItem -LiteralPath $Source -File | Unblock-File

$appData = $env:APPDATA
if ([string]::IsNullOrWhiteSpace($appData)) { $appData = [Environment]::GetFolderPath('ApplicationData') }
$installed = 0
foreach ($year in @('2025', '2026', '2027')) {
    $target = Join-Path -Path $appData -ChildPath "Autodesk\Revit\Addins\$year\DanCI.BOQ"
    if (Test-Path -LiteralPath (Join-Path $target 'DanCI.BOQ.Addin.dll')) {
        foreach ($f in $files) { Copy-Item -LiteralPath (Join-Path $Source $f) $target -Force }
        $missing = @($all | Where-Object { -not (Test-Path -LiteralPath (Join-Path $target $_)) })
        if ($missing.Count -eq 0) {
            Write-Host "Revit $year : composants PDF COMPLETS, l'export PDF est disponible." -ForegroundColor Green
        } else {
            Write-Host "Revit $year : partie installee. Il manque encore : $($missing -join ', ') (installez l'autre partie)." -ForegroundColor Yellow
        }
        $installed++
    }
}
if ($installed -eq 0) {
    Write-Host "Aucune installation de DanCI - Metre & BOQ trouvee : installez d'abord l'extension, puis relancez cet installeur." -ForegroundColor Yellow
}
