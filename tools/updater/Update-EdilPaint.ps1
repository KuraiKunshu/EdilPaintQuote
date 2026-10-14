# EdilPaint updater protocol: 2
param(
    [string]$SettingsPath = (Join-Path $PSScriptRoot "updater-settings.json"),
    [int]$WindowCloseDelaySeconds = 0,
    [switch]$ManualUpdate,
    [ValidateRange(0, 300)][int]$WaitForApplicationExitSeconds = 60,
    [string]$ExpectedInstallPath = '',
    [int]$ApplicationProcessId = 0,
    [string]$ReadyPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:installationLock = $null
$script:copyLock = $null
$script:readySent = $false
$script:restart = $false
$script:runRoot = ''
$script:installPath = ''
$script:workRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EdilPaintUpdater\work'

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

    $expanded = [Environment]::ExpandEnvironmentVariables($Path)
    if (-not [IO.Path]::IsPathRooted($expanded)) { $expanded = Join-Path $scriptRoot $expanded }
    return [IO.Path]::GetFullPath($expanded)
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

    if ($null -ne $script:copyLock) { $script:copyLock.Dispose(); $script:copyLock = $null }
    try {
        if ($script:restart -and (Test-Path -LiteralPath $installedExe) -and
            -not (Test-Path -LiteralPath (Join-Path $installPath '.edilpaint-update-transaction.json')) -and
            -not (Test-ApplicationRunning -Name $processName)) {
            try {
                $result = Restart-Application $installedExe $installPath
                if ($result.Status -eq 'failed') {
                    Save-UpdateStatus 'failed' $result.Message
                    if (-not $result.DialogShown) { Show-UpdateFailure -Message $result.Message -LogPath $logPath }
                    $Code = 1
                } elseif ($result.Status -eq 'pending') {
                    if ($Code -eq 0) { Save-UpdateStatus 'restart-pending' $result.Message }
                    Show-UpdateFailure -Message $result.Message -LogPath $logPath -RestartPending
                    $Code = 1
                } elseif ($Code -eq 0) { Save-UpdateStatus 'complete' $result.Message }
            }
            catch {
                $message = "Il riavvio del programma non e' riuscito: $($_.Exception.Message). Percorso: $installedExe"
                Write-Log $message
                Save-UpdateStatus 'failed' $message
                if ($ManualUpdate) { Show-UpdateFailure -Message $message -LogPath $logPath }
                $Code = 1
            }
        }
    } finally {
        if ($null -ne $script:installationLock) { $script:installationLock.Dispose(); $script:installationLock = $null }
    }
    exit $Code
}

function Wait-ForRestartResult {
    param($Process, [string]$ResultPath, [bool]$RequireConfirmation, [int]$TimeoutSeconds = 120)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        if ($Process -is [Diagnostics.Process]) { $Process.Refresh() }
        if (Test-Path -LiteralPath $ResultPath) {
            $result = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($result.ProcessId -ne $Process.Id -or $result.Status -notin @('ready', 'failed')) { throw 'Conferma di avvio non valida.' }
            return $result
        }
        if ($Process.HasExited) {
            if (Test-Path -LiteralPath $ResultPath) { continue }
            throw "Il processo si e' chiuso prima di aprire la finestra principale (codice $($Process.ExitCode))."
        }
        # Previous versions restored by rollback do not support the confirmation.
        if (-not $RequireConfirmation -and $Process.MainWindowHandle -ne [IntPtr]::Zero) {
            return @{ Status = 'started'; Message = 'Versione precedente riaperta; conferma di avvio non disponibile in questa versione.'; DialogShown = $false }
        }
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            return @{ Status = 'pending'; Message = "Il processo e' avviato, ma l'apertura della finestra principale non e' stata confermata entro $TimeoutSeconds secondi. Potrebbe essere ancora in caricamento: non e' stato chiuso. Controlla la finestra di avvio e il log."; DialogShown = $false }
        }
        Start-Sleep -Milliseconds 250
    }
}

function Restart-Application {
    param([string]$ExecutablePath, [string]$Directory,
        [string]$StartupRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EdilPaintUpdater\startup'))
    $session = Get-ContainedPath $startupRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $session -Force | Out-Null
    $resultPath = Join-Path $session 'startup.json'
    $confirmation = Test-Path -LiteralPath (Join-Path $Directory 'updater-startup-protocol.txt')
    $process = $null
    try {
        $process = Start-Process -FilePath $ExecutablePath -WorkingDirectory $Directory -ArgumentList @('--update-startup-result', ('"' + $resultPath + '"')) -PassThru
        $result = Wait-ForRestartResult $process $resultPath $confirmation
        Write-Log "Riavvio: $($result.Message)"
        if ($result.Status -eq 'failed' -and $result.LogPath) { Write-Log "Errore di avvio: $($result.LogPath)" }
        if ($result.Status -in @('ready', 'started')) {
            try { Remove-ManagedDirectory $startupRoot $session } catch { Write-Log 'Conferma di avvio conservata: pulizia temporanea non completata.' }
        }
        return $result
    } finally {
        if ($null -ne $process) { $process.Dispose() }
    }
}

function Show-UpdateFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [switch]$RestartPending
    )

    $summary = "L'aggiornamento di EdilPaint non e' stato completato."
    $title = 'Aggiornamento EdilPaint non riuscito'
    $icon = 16
    if ($RestartPending) {
        $summary = "I file della nuova versione sono installati. Il riavvio e' ancora da verificare."
        $title = 'EdilPaint: riavvio da verificare'
        $icon = 48
    }
    $dialogMessage = "$summary`n`nDettagli:`n$Message`n`nLog:`n$LogPath"

    try {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show(
            $dialogMessage,
            $title,
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]$icon) | Out-Null
    }
    catch {
        try {
            $shell = New-Object -ComObject WScript.Shell
            $shell.Popup($dialogMessage, 0, $title, $icon) | Out-Null
        }
        catch {
            # Il log rimane disponibile anche sui PC dove Windows non puo' mostrare finestre.
        }
    }
}

$logPath = Join-Path $scriptRoot "logs\update.log"

function Write-Log {
    param([string]$Message)

    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch { [Console]::Error.WriteLine($line) }
}

function Write-AtomicJson {
    param([string]$Path, $Value)
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    $previous = "$temporary.previous"
    try {
        $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary -Encoding UTF8
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporary, $Path, $previous, $true) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Force }
    }
}

function Send-Ready {
    param([string]$Status, [string]$Message)
    if ($ReadyPath) { Write-AtomicJson $ReadyPath @{ Status = $Status; Message = $Message; LogPath = $logPath } }
}

function Save-UpdateStatus {
    param([string]$Status, [string]$Message)
    if ([string]::IsNullOrWhiteSpace($installPath) -or $null -eq $script:installationLock) { return }
    try { Write-AtomicJson (Join-Path $installPath '.edilpaint-update-status.json') @{ Status = $Status; Message = $Message; LogPath = $logPath; UpdatedAtUtc = [DateTime]::UtcNow.ToString('O') } }
    catch { Write-Log 'Impossibile salvare il risultato diagnostico.' }
}

function Get-ContainedPath {
    param([string]$Root, [string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) { throw 'Percorso del pacchetto non valido.' }
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath((Join-Path $parent $RelativePath))
    if (-not $target.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Il percorso esce dalla cartella prevista.' }
    return $target
}

function Remove-ManagedDirectory {
    param([string]$Root, [string]$Path)
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($Path)
    if (-not $target.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Rimozione di una cartella esterna all''area aggiornamenti rifiutata.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}

function Test-ProtectedFile {
    param([string]$RelativePath)
    $path = $RelativePath.Replace('/', '\')
    return $path -match '^(appsettings(\.[^\\]+)?\.json|company-profile\.id|updater-path\.txt|\.edilpaint-(update|install)\.lock|\.edilpaint-update-(status|transaction)\.json)$' -or
        $path -match '^Assets\\(azienda|config_fatture|dati_lavori)\.json$' -or
        $path -match '^updater\\(updater-settings\.json|state\\|logs\\)'
}

function Get-PackageFileHash {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}

function Expand-SourceArchive {
    param([string]$Archive, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.IO.Compression
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $target = Get-ContainedPath $Destination $entry.FullName
            if ($entry.FullName.EndsWith('/')) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $zip.Dispose() }
}

function Test-InstalledManifest {
    param([string]$Directory, [string]$Commit, [string]$Repository, [string]$Branch)
    $manifestPath = Join-Path $Directory 'edilpaint-update.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { return $false }
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.Version -ne 1 -or $manifest.Commit -ne $Commit -or $manifest.Repository -ne $Repository -or
            $manifest.Branch -ne $Branch -or @($manifest.Files).Count -eq 0) { return $false }
        foreach ($file in $manifest.Files) {
            if (Test-ProtectedFile $file.Path) { return $false }
            $path = Get-ContainedPath $Directory $file.Path
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-PackageFileHash $path) -ne $file.Hash) { return $false }
        }
        return @($manifest.Files | Where-Object { [IO.Path]::GetExtension($_.Path) -eq '.exe' }).Count -gt 0
    } catch { return $false }
}

function New-PackageManifest {
    param([string]$Package, [string]$Commit, [string]$Repository, [string]$Branch, [string]$ProcessName)
    foreach ($required in @("$ProcessName.exe", "$ProcessName.dll", "$ProcessName.deps.json", "$ProcessName.runtimeconfig.json")) {
        $file = Join-Path $Package $required
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw "Pacchetto incompleto: $required" }
    }
    $root = [IO.Path]::GetFullPath($Package).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $files = @(Get-ChildItem -LiteralPath $Package -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($root.Length)
        if (-not (Test-ProtectedFile $relative)) { @{ Path = $relative; Hash = (Get-PackageFileHash $_.FullName) } }
    })
    return @{ Version = 1; Commit = $Commit; Repository = $Repository; Branch = $Branch; Files = $files; InstalledAtUtc = [DateTime]::UtcNow.ToString('O') }
}

function Restore-Installation {
    param([string]$Directory, [string]$WorkRoot)
    $journalPath = Join-Path $Directory '.edilpaint-update-transaction.json'
    if (-not (Test-Path -LiteralPath $journalPath)) { return }
    $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
    if (-not [string]::Equals([IO.Path]::GetFullPath($journal.InstallPath), [IO.Path]::GetFullPath($Directory), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Il registro di ripristino appartiene a un''altra installazione.'
    }
    $backup = Get-ContainedPath $WorkRoot $journal.BackupName
    # Validate the entire journal before touching installed files.
    foreach ($file in $journal.Files) {
        $null = Get-ContainedPath $Directory $file.Path
        if (Test-ProtectedFile $file.Path) { throw 'Il registro contiene una configurazione locale protetta.' }
        if ($file.Existed -and -not (Test-Path -LiteralPath (Get-ContainedPath $backup $file.Path) -PathType Leaf)) {
            throw 'Backup incompleto. Mantieni il programma chiuso e ripristina il backup indicato nel registro.'
        }
    }
    foreach ($file in $journal.Files) {
        $target = Get-ContainedPath $Directory $file.Path
        if ($file.Existed) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath (Get-ContainedPath $backup $file.Path) -Destination $target -Force
        } elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    Remove-Item -LiteralPath $journalPath -Force
    try { Remove-ManagedDirectory $WorkRoot $backup } catch { Write-Log 'Backup residuo conservato dopo il ripristino.' }
    Write-Log 'Ripristinata la versione precedente.'
}

function Install-Package {
    param([string]$Package, [string]$Directory, [string]$WorkRoot, $Manifest)
    $manifestFile = 'edilpaint-update.json'
    $paths = @($Manifest.Files | ForEach-Object { $_.Path })
    $previous = Join-Path $Directory $manifestFile
    if (Test-Path -LiteralPath $previous) {
        $old = Get-Content -LiteralPath $previous -Raw | ConvertFrom-Json
        $paths += @($old.Files | ForEach-Object { if (-not (Test-ProtectedFile $_.Path)) { $_.Path } })
    }
    $paths = @(@($paths + $manifestFile) | Sort-Object -Unique)
    foreach ($relative in $paths) {
        if (Test-ProtectedFile $relative) { throw 'Il pacchetto contiene una configurazione locale protetta.' }
        $target = Get-ContainedPath $Directory $relative
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            $handle = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $handle.Dispose()
        }
    }
    $backupName = 'backup-' + [Guid]::NewGuid().ToString('N')
    $backup = Get-ContainedPath $WorkRoot $backupName
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $files = @()
    foreach ($relative in $paths) {
        $target = Get-ContainedPath $Directory $relative
        $existed = Test-Path -LiteralPath $target -PathType Leaf
        $files += @{ Path = $relative; Existed = [bool]$existed }
        if ($existed) {
            $saved = Get-ContainedPath $backup $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved -Force
        }
    }
    $journalPath = Join-Path $Directory '.edilpaint-update-transaction.json'
    Write-AtomicJson $journalPath @{ InstallPath = [IO.Path]::GetFullPath($Directory); BackupName = $backupName; Files = $files }
    try {
        foreach ($file in $Manifest.Files) {
            $target = Get-ContainedPath $Directory $file.Path
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath (Get-ContainedPath $Package $file.Path) -Destination $target -Force
        }
        foreach ($relative in $paths) {
            if ($relative -eq $manifestFile -or @($Manifest.Files | Where-Object { $_.Path -eq $relative }).Count -gt 0) { continue }
            $obsolete = Get-ContainedPath $Directory $relative
            if (Test-Path -LiteralPath $obsolete) { Remove-Item -LiteralPath $obsolete -Force }
        }
        Write-AtomicJson $previous $Manifest
        if (-not (Test-InstalledManifest $Directory $Manifest.Commit $Manifest.Repository $Manifest.Branch)) { throw 'Verifica dei file installati non riuscita.' }
        Remove-Item -LiteralPath $journalPath -Force
    } catch {
        $failure = $_
        try { Restore-Installation $Directory $WorkRoot }
        catch { throw "Aggiornamento interrotto; ripristino da completare: $($_.Exception.Message). Registro: $journalPath" }
        throw $failure
    }
    try { Remove-ManagedDirectory $WorkRoot $backup } catch { Write-Log 'Backup residuo conservato; installazione verificata.' }
}

try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null

    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        New-DefaultSettingsFile -Path $SettingsPath
        Write-Log "ERRORE: creato file impostazioni; configura InstallPath e rilancia lo script."
        Show-UpdateFailure -Message "Creato il file updater-settings.json. Configura InstallPath e riprova." -LogPath $logPath
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
$lastUpdateFile = Join-Path $scriptRoot "ultimo-aggiornamento.txt"
$sourceGitSafeDirectory = [System.IO.Path]::GetFullPath($sourcePath).Replace("\", "/")

New-Item -ItemType Directory -Path (Split-Path -Parent $logPath) -Force | Out-Null

function Save-LastUpdateInfo {
    param([Parameter(Mandatory = $true)][string]$Commit)

    $contents = @(
        "Ultimo aggiornamento completato: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Date),
        "Commit: $Commit"
    )
    Set-Content -LiteralPath $lastUpdateFile -Value $contents -Encoding UTF8
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    $preference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Log ([string]$_) }
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $preference }
    if ($code -ne 0) { throw "Comando $FilePath fallito (codice $code). Consulta il log per i dettagli." }
}

function Get-ExternalOutput {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    $preference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & $FilePath @Arguments 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $preference }
    if ($code -ne 0) { throw "Comando $FilePath fallito (codice $code). $output" }

    return (($output | Out-String).Trim())
}

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    Invoke-External git -c "safe.directory=$sourceGitSafeDirectory" -c core.longpaths=true -c credential.interactive=never -c http.lowSpeedLimit=1000 -c http.lowSpeedTime=60 @Arguments
}

function Get-GitOutput {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    return Get-ExternalOutput git -c "safe.directory=$sourceGitSafeDirectory" -c core.longpaths=true -c credential.interactive=never @Arguments
}

function Test-Tool {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Comando non trovato: $Name"
    }
}

function Test-ApplicationRunning {
    param([Parameter(Mandatory = $true)][string]$Name)

    foreach ($process in @(Get-Process -Name $Name -ErrorAction SilentlyContinue)) {
        try {
            if ([string]::IsNullOrWhiteSpace($process.Path)) { return $true }
            if ([string]::Equals([IO.Path]::GetFullPath($process.Path), $installedExe, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        } catch { return $true }
    }
    return $false
}

function Wait-ForApplicationExit {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    if ($TimeoutSeconds -le 0) {
        return -not (Test-ApplicationRunning -Name $Name)
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Test-ApplicationRunning -Name $Name) -or
        ($ApplicationProcessId -gt 0 -and $null -ne (Get-Process -Id $ApplicationProcessId -ErrorAction SilentlyContinue))) {
        if ((Get-Date) -ge $deadline) {
            return $false
        }

        Start-Sleep -Milliseconds 250
    }

    return $true
}

    if ([string]::IsNullOrWhiteSpace($installPath)) {
        throw "InstallPath non e' configurato in $SettingsPath"
    }
    $installPath = [IO.Path]::GetFullPath($installPath).TrimEnd('\', '/')
    $sourcePath = [IO.Path]::GetFullPath($sourcePath).TrimEnd('\', '/')
    $installedExe = Join-Path $installPath "$processName.exe"
    if ($ExpectedInstallPath -and -not [string]::Equals($installPath,
        [IO.Path]::GetFullPath($ExpectedInstallPath).TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "L'updater punta a $installPath ma il programma aperto si trova in $ExpectedInstallPath. Correggi InstallPath prima di aggiornare."
    }
    if ($sourcePath -eq $installPath -or $sourcePath.StartsWith($installPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $installPath.StartsWith($sourcePath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'SourcePath e InstallPath devono essere cartelle separate.' }
    if ($processName -notmatch '^[A-Za-z0-9_.-]+$' -or $branch.StartsWith('-') -or $repositoryUrl.StartsWith('-')) { throw 'Nome del programma, branch o repository non valido.' }
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
    try {
        $script:installationLock = [IO.File]::Open((Join-Path $installPath '.edilpaint-update.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch { throw 'Un altro aggiornamento e gia in corso oppure la cartella non e scrivibile.' }
    New-Item -ItemType Directory -Path $script:workRoot -Force | Out-Null
    if (Test-Path -LiteralPath (Join-Path $installPath '.edilpaint-update-transaction.json')) {
        if (Test-ApplicationRunning -Name $processName) { throw 'Chiudi il programma prima di ripristinare un aggiornamento interrotto.' }
        Restore-Installation $installPath $script:workRoot
    }

    Test-Tool -Name "git"

    Write-Log "Avvio controllo aggiornamenti EdilPaint."

    # Il pulsante dell'app avvia questo processo poco prima di chiudere l'EXE.
    # Aspettiamo quindi esplicitamente la sua chiusura: senza questa attesa il
    # controllo poteva vedere ancora il processo attivo e saltare l'aggiornamento.
    # WindowCloseDelaySeconds mantiene la compatibilita' con le versioni dell'app
    # precedenti a -ManualUpdate.
    $applicationExitWaitSeconds = if ($ManualUpdate) {
        $WaitForApplicationExitSeconds
    }
    else {
        [Math]::Max(0, $WindowCloseDelaySeconds)
    }

    if ($applicationExitWaitSeconds -gt 0 -and -not $ReadyPath) {
        Write-Log "Attendo fino a $applicationExitWaitSeconds secondi la chiusura del programma."
        if (-not (Wait-ForApplicationExit -Name $processName -TimeoutSeconds $applicationExitWaitSeconds)) {
            if ($ManualUpdate) {
                throw "Il programma e' ancora aperto dopo $applicationExitWaitSeconds secondi. Chiudilo completamente e riprova."
            }

            Write-Log "Programma ancora aperto: salto aggiornamento per non sovrascrivere file in uso."
            Exit-Updater 0
        }
    }

    if (-not $ManualUpdate -and $startDelaySeconds -gt 0) {
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

    $env:GIT_TERMINAL_PROMPT = '0'
    Invoke-Git -C $sourcePath fetch --no-tags -- $repositoryUrl $branch

    $remoteCommit = Get-GitOutput -C $sourcePath rev-parse FETCH_HEAD
    if ($remoteCommit -notmatch '^[0-9a-fA-F]{40,64}$') { throw 'Identificativo versione non valido.' }
    $installedExe = Join-Path $installPath "$processName.exe"
    $runtime = [string](Get-Setting -Name 'RuntimeIdentifier' -DefaultValue 'win-x64')
    $selfContained = [Convert]::ToBoolean((Get-Setting -Name 'SelfContained' -DefaultValue $true))
    if ($runtime -notmatch '^win-(x64|x86|arm64)$') { throw 'RuntimeIdentifier non valido.' }
    $configurationMatches = $false
    $manifestPath = Join-Path $installPath 'edilpaint-update.json'
    if (Test-Path -LiteralPath $manifestPath) {
        try {
            $installedManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $configurationMatches = $installedManifest.RuntimeIdentifier -eq $runtime -and $installedManifest.SelfContained -eq $selfContained
        } catch { $configurationMatches = $false }
    }
    if ($configurationMatches -and (Test-InstalledManifest $installPath $remoteCommit $repositoryUrl $branch)) {
        Write-Log "Programma gia aggiornato: tutti i file installati sono stati verificati."
        Save-UpdateStatus 'current' 'Versione installata verificata.'
        Send-Ready 'up-to-date' 'Il programma e gia aggiornato. Tutti i file sono stati verificati.'
        Exit-Updater 0
    }

    if ((Test-ApplicationRunning -Name $processName) -and -not $ReadyPath) {
        if ($ManualUpdate) {
            throw "Il programma e' ancora aperto. Chiudilo completamente e riprova l'aggiornamento."
        }

        Write-Log "Programma aperto: salto aggiornamento per non sovrascrivere file in uso."
        Save-UpdateStatus 'deferred' 'Aggiornamento rinviato: programma aperto.'
        Exit-Updater 0
    }

    Write-Log "Aggiornamento trovato: $remoteCommit"
    Test-Tool -Name 'dotnet'
    $script:runRoot = Get-ContainedPath $script:workRoot ('build-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:runRoot -Force | Out-Null
    $sourceArchive = Join-Path $script:runRoot 'source.zip'
    $buildSource = Join-Path $script:runRoot 'source'
    $publishPath = Join-Path $script:runRoot 'publish'
    Invoke-Git -C $sourcePath archive --format=zip "--output=$sourceArchive" $remoteCommit
    Expand-SourceArchive $sourceArchive $buildSource
    $projectFullPath = Get-ContainedPath $buildSource $projectPath
    $testProjectFullPath = Get-ContainedPath $buildSource $testProjectPath

    if (-not (Test-Path -LiteralPath $projectFullPath)) {
        throw "ProjectPath non trovato: $projectFullPath"
    }

    [xml]$projectXml = Get-Content -LiteralPath $projectFullPath -Raw
    $framework = @($projectXml.SelectNodes('//TargetFramework'))[0].InnerText
    if ($framework -notmatch '^net(\d+)\.') { throw 'Versione .NET del progetto non riconosciuta.' }
    $major = [int]$Matches[1]
    $sdks = Get-ExternalOutput dotnet --list-sdks
    $supported = @([regex]::Matches($sdks, '(?m)^(\d+)\.') | Where-Object { [int]$_.Groups[1].Value -ge $major }).Count -gt 0
    if (-not $supported) { throw "Serve l'SDK .NET $major o successivo per preparare questa versione. Un runtime da solo non basta. Il programma attuale rimane disponibile." }
    $containedText = $selfContained.ToString().ToLowerInvariant()
    Write-Log "Ripristino pacchetti."
    Invoke-External dotnet restore $projectFullPath --runtime $runtime "-p:SelfContained=$containedText" --disable-parallel

    if ($runTests) {
        if (Test-Path -LiteralPath $testProjectFullPath) {
            Write-Log "Eseguo test."
            Invoke-External dotnet test $testProjectFullPath --configuration $configuration
        }
        else {
            throw "TestProjectPath non trovato: $testProjectFullPath. Nessun file installato e stato modificato."
        }
    }

    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
    Write-Log "Pubblico applicazione."
    if ($runTests) { Invoke-External dotnet restore $projectFullPath --runtime $runtime "-p:SelfContained=$containedText" --disable-parallel }
    Invoke-External dotnet publish $projectFullPath --configuration $configuration --runtime $runtime --output $publishPath --no-restore --self-contained $containedText "-p:SourceRevisionId=$remoteCommit"
    $manifest = New-PackageManifest $publishPath $remoteCommit $repositoryUrl $branch $processName
    $manifest.RuntimeIdentifier = $runtime
    $manifest.SelfContained = $selfContained
    if ($ManualUpdate -and $ReadyPath) {
        Send-Ready 'ready' 'La nuova versione e pronta per essere installata.'
        $script:readySent = $true
        if (-not (Wait-ForApplicationExit -Name $processName -TimeoutSeconds $WaitForApplicationExitSeconds)) {
            throw 'Il programma non si e chiuso. Nessun file e stato sostituito.'
        }
    }

    # Block new application starts only for the replacement, not while downloading
    # or building. A user may keep working during automatic preparation.
    $script:copyLock = [IO.File]::Open((Join-Path $installPath '.edilpaint-install.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    if (Test-ApplicationRunning -Name $processName) {
        if ($ManualUpdate) {
            throw "Il programma e' stato riaperto durante l'aggiornamento. Chiudilo completamente e riprova."
        }

        Write-Log "Programma aperto dopo la build: salto copia file. Riprovero' al prossimo controllo."
        Save-UpdateStatus 'deferred' 'Il programma e stato riaperto: nessun file sostituito.'
        Exit-Updater 0
    }

    $script:restart = [bool]$ManualUpdate
    Write-Log "Copio file in $installPath"
    Install-Package $publishPath $installPath $script:workRoot $manifest

    try {
        New-Item -ItemType Directory -Path $statePath -Force | Out-Null
        Save-LastUpdateInfo -Commit $remoteCommit
        Set-Content -LiteralPath $deployedCommitFile -Value $remoteCommit -Encoding UTF8
    } catch { Write-Log 'Installazione verificata; impossibile aggiornare il vecchio registro diagnostico.' }

    Write-Log "Aggiornamento completato."
    Save-UpdateStatus 'complete' "Installazione verificata: $remoteCommit"
    Exit-Updater 0
}
catch {
    $errorMessage = $_.Exception.Message
    Write-Log "ERRORE: $errorMessage"
    Save-UpdateStatus 'failed' $errorMessage
    if ($ReadyPath -and -not $script:readySent) {
        try { Send-Ready 'failed' $errorMessage } catch { Write-Log 'Impossibile comunicare il risultato al programma.' }
    } elseif ($ManualUpdate) { Show-UpdateFailure -Message $errorMessage -LogPath $logPath }
    Exit-Updater 1
}
finally {
    if ($null -ne $script:installationLock) { $script:installationLock.Dispose() }
    if ($null -ne $script:copyLock) { $script:copyLock.Dispose() }
    if ($script:runRoot) {
        try { Remove-ManagedDirectory $script:workRoot $script:runRoot } catch { Write-Log 'Cartella di preparazione conservata per la diagnostica.' }
    }
}
