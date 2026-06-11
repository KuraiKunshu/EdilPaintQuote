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
        TestProjectPath = "EdilPaintPreventibiviGen.Tests\EdilPaintPreventibiviGen.Tests.csproj"
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

function Get-StartupShortcutPath {
    $startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
    New-Item -ItemType Directory -Path $startupFolder -Force | Out-Null
    return Join-Path $startupFolder "$TaskName.lnk"
}

function Remove-StartupFallback {
    $shortcutPath = Get-StartupShortcutPath
    $cmdPath = [System.IO.Path]::ChangeExtension($shortcutPath, ".cmd")

    foreach ($path in @($shortcutPath, $cmdPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

function New-StartupFallback {
    $shortcutPath = Get-StartupShortcutPath
    $cmdPath = [System.IO.Path]::ChangeExtension($shortcutPath, ".cmd")

    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = "powershell.exe"
        $shortcut.Arguments = $actionArgument
        $shortcut.WorkingDirectory = $UpdaterPath
        $shortcut.WindowStyle = 7
        $shortcut.Description = "Controlla GitHub e aggiorna EdilPaint Preventivi quando l'utente accede a Windows."
        $shortcut.Save()

        if (Test-Path -LiteralPath $cmdPath) {
            Remove-Item -LiteralPath $cmdPath -Force
        }

        Write-Host "Accesso task pianificata negato: creato avvio automatico utente: $shortcutPath"
        return
    }
    catch {
        $command = "@echo off`r`npowershell.exe $actionArgument`r`n"
        Set-Content -LiteralPath $cmdPath -Value $command -Encoding ASCII
        Write-Host "Accesso task pianificata negato: creato avvio automatico utente: $cmdPath"
    }
}

try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $taskSettings `
        -Description "Controlla GitHub e aggiorna EdilPaint Preventivi quando l'utente accede a Windows." `
        -Force `
        -ErrorAction Stop | Out-Null

    Remove-StartupFallback
    Write-Host "Attivita pianificata creata: $TaskName"
}
catch {
    Write-Host "Impossibile creare la task pianificata: $($_.Exception.Message)"
    New-StartupFallback
}

Write-Host "Script: $targetScript"
Write-Host "Impostazioni: $settingsPath"
