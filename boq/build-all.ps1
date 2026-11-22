<#
.SYNOPSIS
    Compile DanCI - Metre & BOQ pour Revit 2025, 2026 et 2027, lance les tests et produit
    un dossier de deploiement (et un .zip) par annee.

.DESCRIPTION
    Pour chaque configuration (R2025, R2026, R2027) :
      1. dotnet build DanCI.BOQ.sln -c <config>
      2. dotnet test (tests du Core sur le framework de cette annee : net8.0 ou net10.0)
      3. le paquet est assemble par la compilation dans boq\deploy\Revit<annee>\ :
           DanCI.BOQ.addin
           DanCI.BOQ\*.dll
      4. boq\artifacts\DanCI-BOQ-<version>-Revit<annee>.zip

    S'arrete a la premiere erreur de compilation ou de test : un paquet n'est jamais produit
    a partir d'un build rouge.

    Prerequis : SDK .NET 10 (voir global.json ; il compile aussi les cibles net8.0).

.PARAMETER Versions
    Annees a compiler. Par defaut : 2025, 2026, 2027.

.PARAMETER NoDeploy
    Ne copie pas dans %AppData%\Autodesk\Revit\Addins\<annee>\ (CI, poste sans Revit).

.PARAMETER SkipTests
    Ne lance pas les tests (deconseille).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-all.ps1
.EXAMPLE
    .\build-all.ps1 -Versions 2026,2027 -NoDeploy
#>

[CmdletBinding()]
param(
    [ValidateSet('2025', '2026', '2027')]
    [string[]]$Versions = @('2025', '2026', '2027'),
    [switch]$NoDeploy,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'DanCI.BOQ.sln'
$tests = Join-Path $root 'tests\DanCI.BOQ.Tests\DanCI.BOQ.Tests.csproj'
$artifacts = Join-Path $root 'artifacts'

function Invoke-Step([string]$label, [scriptblock]$action) {
    Write-Host ''
    Write-Host "==> $label" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        throw "Echec : $label (code $LASTEXITCODE)."
    }
}

Push-Location $root
try {
    Invoke-Step 'SDK .NET disponibles' { dotnet --list-sdks }
    Invoke-Step 'SDK retenu par global.json' { dotnet --version }

    $props = [xml](Get-Content (Join-Path $root 'Directory.Build.props'))
    $version = ($props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

    $deployFlag = if ($NoDeploy) { 'false' } else { 'true' }
    $summary = @()

    foreach ($year in $Versions) {
        $config = "R$year"

        Invoke-Step "Compilation $config" {
            dotnet build $solution -c $config -nologo "-p:DanCIDeploy=$deployFlag"
        }

        if (-not $SkipTests) {
            Invoke-Step "Tests $config" {
                dotnet test $tests -c $config --no-build -nologo --verbosity normal
            }
        }

        $stage = Join-Path $root "deploy\Revit$year"
        foreach ($required in @('DanCI.BOQ.addin', 'DanCI.BOQ\DanCI.BOQ.Addin.dll', 'DanCI.BOQ\DanCI.BOQ.Core.dll',
                                'DanCI.BOQ\ClosedXML.dll', 'DanCI.BOQ\QuestPDF.dll', 'DanCI.BOQ\questpdf_skia.dll', 'DanCI.BOQ\QRCoder.dll',
                                'DanCI.BOQ\questpdf_fonts_lato.br')) {
            if (-not (Test-Path (Join-Path $stage $required))) {
                throw "Paquet $config incomplet : $required manquant dans $stage."
            }
        }
        $revitApi = Get-ChildItem $stage -Recurse -Filter 'RevitAPI*.dll'
        if ($revitApi) {
            throw "Paquet $config invalide : RevitAPI ne doit pas etre embarquee ($($revitApi.Name -join ', '))."
        }

        # ETAPE1 : extension SANS les composants PDF (distribues a part, en deux parties < 4 Mo).
        $zip = Join-Path $artifacts "ETAPE1_Extension_DanCI-BOQ_$($version)_Revit$year.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        $light = Join-Path $artifacts "_stage_$year"
        if (Test-Path $light) { Remove-Item $light -Recurse -Force }
        Copy-Item $stage $light -Recurse
        Remove-Item (Join-Path $light 'DanCI.BOQ\questpdf_skia.dll'), (Join-Path $light 'DanCI.BOQ\questpdf_fonts_lato.br') -Force
        Compress-Archive -Path (Join-Path $light '*') -DestinationPath $zip -CompressionLevel Optimal
        Remove-Item $light -Recurse -Force

        # ETAPE2 / ETAPE3 : composants PDF, une partie par fichier (memes pour toutes les annees).
        $parts = @(@{ Name = 'ETAPE2_Composants-PDF_partie1.zip'; File = 'questpdf_skia.dll'; Readme = 'LISEZMOI_partie1.txt' },
                   @{ Name = 'ETAPE3_Composants-PDF_partie2.zip'; File = 'questpdf_fonts_lato.br'; Readme = 'LISEZMOI_partie2.txt' })
        foreach ($part in $parts) {
            $pdfZip = Join-Path $artifacts $part.Name
            if (Test-Path $pdfZip) { continue }
            $pdfStage = Join-Path $artifacts '_stage_pdf'
            if (Test-Path $pdfStage) { Remove-Item $pdfStage -Recurse -Force }
            New-Item -ItemType Directory -Path $pdfStage -Force | Out-Null
            Copy-Item (Join-Path $stage ('DanCI.BOQ\' + $part.File)) $pdfStage
            Copy-Item (Join-Path $root 'install\pdf\Installer.ps1'), (Join-Path $root 'install\pdf\Installer.cmd'), (Join-Path $root ('install\pdf\' + $part.Readme)) $pdfStage
            Compress-Archive -Path (Join-Path $pdfStage '*') -DestinationPath $pdfZip -CompressionLevel Optimal
            Remove-Item $pdfStage -Recurse -Force
        }
        $summary += [pscustomobject]@{ Revit = $year; Paquet = $stage; Zip = $zip }
    }

    Write-Host ''
    Write-Host 'Termine.' -ForegroundColor Green
    $summary | Format-Table -AutoSize
    if (-not $NoDeploy) {
        Write-Host 'Installe dans %AppData%\Autodesk\Revit\Addins\<annee>\ : redemarrez Revit.' -ForegroundColor Green
        Write-Host '(Si Revit etait ouvert, les DLL verrouillees n''ont pas ete remplacees : voir les avertissements.)'
    }
}
finally {
    Pop-Location
}
