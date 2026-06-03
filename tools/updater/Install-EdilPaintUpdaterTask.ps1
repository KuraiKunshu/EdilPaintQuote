param(
    [Parameter(Mandatory = $true)]
    [string]$InstallPath,

    [string]$UpdaterPath = "C:\EdilPaintUpdater",
    [string]$RepositoryUrl = "https://github.com/KuraiKunshu/EdilPaintQuote.git",
    [string]$Branch = "main",
    [string]$TaskName = "EdilPaint Auto Update",
    [int]$StartDelaySeconds = 60,
    [switch]$RunTests,
    [switch]$OverwriteSettings
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$targetScript = Join-Path $UpdaterPath "Update-EdilPaint.ps1"
$settingsPath = Join-Path $UpdaterPath "updater-settings.json"
$sourceScript = Join-Path $PSScriptRoot "Update-EdilPaint.ps1"

if (-not (Test-Path -LiteralPath $sourceScript)) {
    throw "Update-EdilPaint.ps1 non trovato in $PSScriptRoot"
}

New-Item -ItemType Directory -Path $UpdaterPath -Force | Out-Null

$resolvedSourceScript = [System.IO.Path]::GetFullPath($sourceScript)
$resolvedTargetScript = [System.IO.Path]::GetFullPath($targetScript)
if (-not [string]::Equals($resolvedSourceScript, $resolvedTargetScript, [System.StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $sourceScript -Destination $targetScript -Force
}
else {
    Write-Host "Script updater gia' nella cartella corretta: $targetScript"
}

if (-not (Test-Path -LiteralPath $settingsPath) -or $OverwriteSettings) {
    $settings = [ordered]@{
        RepositoryUrl = $RepositoryUrl
        Branch = $Branch
        SourcePath = (Join-Path $UpdaterPath "source")
        InstallPath = $InstallPath
        ProjectPath = "EdilPaintPreventibiviGen\EdilPaintPreventibiviGen.csproj"
        SolutionPath = "EdilPaintPreventibiviGen.sln"
        Configuration = "Release"
        RunTests = [bool]$RunTests
        ProcessName = "EdilPaintPreventibiviGen"
        StartDelaySeconds = $StartDelaySeconds
        LogPath = (Join-Path $UpdaterPath "logs\update.log")
    }

    $settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    Write-Host "Creato file impostazioni: $settingsPath"
}
else {
    Write-Host "File impostazioni gia' presente, lo mantengo: $settingsPath"
}

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$actionArgument = "-NoProfile -ExecutionPolicy Bypass -File `"$targetScript`""
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $actionArgument
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 30)

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $taskSettings `
    -Description "Controlla GitHub e aggiorna EdilPaint Preventivi quando l'utente accede a Windows." `
    -Force | Out-Null

Write-Host "Attivita pianificata creata: $TaskName"
Write-Host "Script: $targetScript"
Write-Host "Impostazioni: $settingsPath"
