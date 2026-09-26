param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Guid]$CompanyId = [Guid]::NewGuid()
)

$ErrorActionPreference = 'Stop'
if ($CompanyId -eq [Guid]::Empty) { throw 'CompanyId non può essere vuoto.' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) {
    throw 'Scegli una cartella nuova: il pacchetto aziendale non deve mescolarsi con una pubblicazione precedente.'
}
$project = Join-Path $PSScriptRoot '../EdilPaintPreventibiviGen/EdilPaintPreventibiviGen.csproj'
dotnet publish $project -c Release -p:GenericCompanyPackage=true --self-contained false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Pubblicazione non riuscita. Non distribuire la cartella.' }

# Fail closed if a future project change accidentally includes company data or credentials.
$forbidden = Get-ChildItem -LiteralPath $destination -File -Recurse | Where-Object {
    $_.Name -eq 'appsettings.json' -or
    $_.Name -match '^(azienda|clienti|history|dati_lavori|materiali_personali|config_fatture)\.json$' -or
    $_.Name -match '^(Edilpaint|Timbro|velux|roto).*\.(png|jpe?g)$' -or
    $_.Name -eq 'Update-EdilPaint.ps1'
}
if ($forbidden) { throw 'Il pacchetto contiene dati riservati o risorse aziendali. Non distribuirlo.' }
Set-Content -LiteralPath (Join-Path $destination 'company-profile.id') -Value $CompanyId.ToString('N') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../docs/NUOVA-AZIENDA.md') -Destination $destination
Write-Output "Pacchetto pronto: $destination"
Write-Output "Identificativo azienda da conservare per gli aggiornamenti: $CompanyId"
