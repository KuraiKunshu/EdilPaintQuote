param(
    [string]$SettingsPath = (Join-Path $PSScriptRoot "updater-settings.json"),
    [int]$WindowCloseDelaySeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$defaultRepositoryUrl = "https://github.com/KuraiKunshu/EdilPaintQuote.git"
$defaultBranch = "main"
$defaultProjectPath = "EdilPaintPreventibiviGen\EdilPaintPreventibiviGen.csproj"
$defaultSolutionPath = "EdilPaintPreventibiviGen.sln"
$defaultTestProjectPath = "EdilPaintPreventibiviGen.Tests\EdilPaintPreventibiviGen.Tests.csproj"
$defaultProcessName = "EdilPaintPreventibiviGen"
$scriptRoot = Split-Path -Parent $SettingsPath
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = $PSScriptRoot
}

function Expand-ConfiguredPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    return [Environment]::ExpandEnvironmentVariables($Path)
}

function New-DefaultSettingsFile {
    param([string]$Path)

    $folder = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($folder)) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
    }

    $settings = [ordered]@{
        RepositoryUrl = $defaultRepositoryUrl
        Branch = $defaultBranch
        SourcePath = (Join-Path $scriptRoot "source")
        InstallPath = ""
        ProjectPath = $defaultProjectPath
        SolutionPath = $defaultSolutionPath
        TestProjectPath = $defaultTestProjectPath
        Configuration = "Release"
        RunTests = $true
        ProcessName = $defaultProcessName
        StartDelaySeconds = 60
        LogPath = (Join-Path $scriptRoot "logs\update.log")
    }

    $settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Exit-Updater {
    param([int]$Code)

    if ($WindowCloseDelaySeconds -gt 0) {
        Write-Host ""
        Write-Host "La finestra si chiudera' tra $WindowCloseDelaySeconds secondi..."
        Start-Sleep -Seconds $WindowCloseDelaySeconds
    }

    exit $Code
}

if (-not (Test-Path -LiteralPath $SettingsPath)) {
    New-DefaultSettingsFile -Path $SettingsPath
    Write-Host "Creato file impostazioni: $SettingsPath"
    Write-Host "Imposta InstallPath e rilancia lo script."
    Exit-Updater 1
}

$settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json

function Get-Setting {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        $DefaultValue
    )

    if ($settings.PSObject.Properties.Name -contains $Name) {
        $value = $settings.$Name
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            return $value
        }
    }

    return $DefaultValue
}

$repositoryUrl = [string](Get-Setting -Name "RepositoryUrl" -DefaultValue $defaultRepositoryUrl)
$branch = [string](Get-Setting -Name "Branch" -DefaultValue $defaultBranch)
$sourcePath = Expand-ConfiguredPath ([string](Get-Setting -Name "SourcePath" -DefaultValue (Join-Path $scriptRoot "source")))
$installPath = Expand-ConfiguredPath ([string](Get-Setting -Name "InstallPath" -DefaultValue ""))
$projectPath = [string](Get-Setting -Name "ProjectPath" -DefaultValue $defaultProjectPath)
$solutionPath = [string](Get-Setting -Name "SolutionPath" -DefaultValue $defaultSolutionPath)
$testProjectPath = [string](Get-Setting -Name "TestProjectPath" -DefaultValue $defaultTestProjectPath)
$configuration = [string](Get-Setting -Name "Configuration" -DefaultValue "Release")
$runTests = [System.Convert]::ToBoolean((Get-Setting -Name "RunTests" -DefaultValue $true))
$processName = [string](Get-Setting -Name "ProcessName" -DefaultValue $defaultProcessName)
$startDelaySeconds = [System.Convert]::ToInt32((Get-Setting -Name "StartDelaySeconds" -DefaultValue 60))
$logPath = Expand-ConfiguredPath ([string](Get-Setting -Name "LogPath" -DefaultValue (Join-Path $scriptRoot "logs\update.log")))
$statePath = Join-Path $scriptRoot "state"
$publishPath = Join-Path $scriptRoot "publish"
$deployedCommitFile = Join-Path $statePath "deployed-commit.txt"
$sourceGitSafeDirectory = [System.IO.Path]::GetFullPath($sourcePath).Replace("\", "/")

New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null

function Write-Log {
    param([string]$Message)

    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    Write-Host $line
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Comando fallito: $FilePath $($Arguments -join ' ') (exit code $LASTEXITCODE)"
    }
}

function Get-ExternalOutput {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    $output = & $FilePath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Comando fallito: $FilePath $($Arguments -join ' ')`n$output"
    }

    return (($output | Out-String).Trim())
}

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    Invoke-External git -c "safe.directory=$sourceGitSafeDirectory" @Arguments
}

function Get-GitOutput {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    return Get-ExternalOutput git -c "safe.directory=$sourceGitSafeDirectory" @Arguments
}

function Test-Tool {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Comando non trovato: $Name"
    }
}

function Test-ApplicationRunning {
    param([Parameter(Mandatory = $true)][string]$Name)

    return $null -ne (Get-Process -Name $Name -ErrorAction SilentlyContinue)
}

function Copy-PublishedFiles {
    param(
        [Parameter(Mandatory = $true)][string]$From,
        [Parameter(Mandatory = $true)][string]$To
    )

    New-Item -ItemType Directory -Path $To -Force | Out-Null

    $excludedRootFiles = @(
        "appsettings.json",
        "appsettings.Development.json"
    )

    $root = (Resolve-Path -LiteralPath $From).Path.TrimEnd("\", "/")
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $relativePath = $file.FullName.Substring($root.Length).TrimStart("\", "/")
        if ($excludedRootFiles -contains $relativePath) {
            Write-Log "Mantengo configurazione locale: $relativePath"
            continue
        }

        $destination = Join-Path $To $relativePath
        $destinationFolder = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $destinationFolder)) {
            New-Item -ItemType Directory -Path $destinationFolder -Force | Out-Null
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($installPath)) {
        throw "InstallPath non e' configurato in $SettingsPath"
    }

    Test-Tool -Name "git"
    Test-Tool -Name "dotnet"

    Write-Log "Avvio controllo aggiornamenti EdilPaint."

    if ($startDelaySeconds -gt 0) {
        Write-Log "Attendo $startDelaySeconds secondi prima del controllo."
        Start-Sleep -Seconds $startDelaySeconds
    }

    $sourceGitFolder = Join-Path $sourcePath ".git"
    if (-not (Test-Path -LiteralPath $sourceGitFolder)) {
        if ((Test-Path -LiteralPath $sourcePath) -and ((Get-ChildItem -LiteralPath $sourcePath -Force | Select-Object -First 1) -ne $null)) {
            throw "SourcePath esiste ma non e' una cartella git vuota: $sourcePath"
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $sourcePath) -Force | Out-Null
        Write-Log "Clono repository in $sourcePath"
        Invoke-Git clone --branch $branch --single-branch $repositoryUrl $sourcePath
    }

    Invoke-Git -C $sourcePath remote set-url origin $repositoryUrl
    Invoke-Git -C $sourcePath fetch origin $branch

    $remoteCommit = Get-GitOutput -C $sourcePath rev-parse "origin/$branch"
    $installedExe = Join-Path $installPath "$processName.exe"
    $deployedCommit = ""
    if (Test-Path -LiteralPath $deployedCommitFile) {
        $deployedCommit = (Get-Content -LiteralPath $deployedCommitFile -Raw).Trim()
    }

    if ($deployedCommit -eq $remoteCommit -and (Test-Path -LiteralPath $installedExe)) {
        Write-Log "Nessun aggiornamento disponibile."
        Exit-Updater 0
    }

    if (Test-ApplicationRunning -Name $processName) {
        Write-Log "Programma aperto: salto aggiornamento per non sovrascrivere file in uso."
        Exit-Updater 0
    }

    Write-Log "Aggiornamento trovato: $remoteCommit"
    $currentBranch = Get-GitOutput -C $sourcePath rev-parse --abbrev-ref HEAD
    if ($currentBranch -ne $branch) {
        Invoke-Git -C $sourcePath switch $branch
    }

    Invoke-Git -C $sourcePath pull --ff-only origin $branch

    $projectFullPath = Join-Path $sourcePath $projectPath
    $testProjectFullPath = Join-Path $sourcePath $testProjectPath

    if (-not (Test-Path -LiteralPath $projectFullPath)) {
        throw "ProjectPath non trovato: $projectFullPath"
    }

    Write-Log "Ripristino pacchetti."
    Invoke-External dotnet restore $projectFullPath

    if ($runTests) {
        if (Test-Path -LiteralPath $testProjectFullPath) {
            Write-Log "Eseguo test."
            Invoke-External dotnet test $testProjectFullPath --configuration $configuration
        }
        else {
            Write-Log "TestProjectPath non trovato: salto test ($testProjectFullPath)."
        }
    }

    if (Test-Path -LiteralPath $publishPath) {
        Remove-Item -LiteralPath $publishPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
    Write-Log "Pubblico applicazione."
    Invoke-External dotnet publish $projectFullPath --configuration $configuration --output $publishPath --no-restore --self-contained false

    if (Test-ApplicationRunning -Name $processName) {
        Write-Log "Programma aperto dopo la build: salto copia file. Riprovero' al prossimo login."
        Exit-Updater 0
    }

    Write-Log "Copio file in $installPath"
    Copy-PublishedFiles -From $publishPath -To $installPath

    New-Item -ItemType Directory -Path $statePath -Force | Out-Null
    Set-Content -LiteralPath $deployedCommitFile -Value $remoteCommit -Encoding UTF8

    Write-Log "Aggiornamento completato."
    Exit-Updater 0
}
catch {
    Write-Log "ERRORE: $($_.Exception.Message)"
    Exit-Updater 1
}
