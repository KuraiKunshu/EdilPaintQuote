param([Parameter(Mandatory = $true)][string]$TestRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Update-EdilPaint.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
# Load actual production functions, without executing the updater entry point.
foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([ScriptBlock]::Create($function.Extent.Text))
}
$root = [IO.Path]::GetFullPath($TestRoot)
if (Test-Path -LiteralPath $root) { throw 'Choose a new test directory.' }
New-Item -ItemType Directory -Path $root -Force | Out-Null
$logPath = Join-Path $root 'test.log'
$script:checks = 0
function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message }; $script:checks++; Write-Output "PASS $Message" }
function Assert-Throws { param([ScriptBlock]$Action, [string]$Message) $thrown = $false; try { & $Action } catch { $thrown = $true }; Assert-True $thrown $Message }
function New-Fixture {
    $case = Join-Path $root ([Guid]::NewGuid().ToString('N'))
    $package = Join-Path $case 'package'; $install = Join-Path $case 'install'; $work = Join-Path $case 'work'
    foreach ($directory in @($package, $install, $work)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    foreach ($file in @('App.exe', 'App.dll', 'App.deps.json', 'App.runtimeconfig.json')) {
        Set-Content -LiteralPath (Join-Path $package $file) -Value "new-$file"
        Set-Content -LiteralPath (Join-Path $install $file) -Value "old-$file"
    }
    return @{ Package = $package; Install = $install; Work = $work; Manifest = (New-PackageManifest $package ('a' * 40) 'test-repository' 'main' 'App') }
}

$fixture = New-Fixture
foreach ($file in @('appsettings.json', 'appsettings.Office.json', 'company-profile.id', 'updater-path.txt', 'Assets\azienda.json', 'Assets\dati_lavori.json', 'Assets\config_fatture.json', 'updater\updater-settings.json')) {
    foreach ($directory in @($fixture.Package, $fixture.Install)) {
        $path = Join-Path $directory $file
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        Set-Content -LiteralPath $path -Value $(if ($directory -eq $fixture.Package) { 'default' } else { 'personalized' })
    }
}
Set-Content -LiteralPath (Join-Path $fixture.Install 'user-data.json') -Value 'user data'
$fixture.Manifest = New-PackageManifest $fixture.Package ('a' * 40) 'test-repository' 'main' 'App'
Assert-True (-not (Test-InstalledManifest $fixture.Install ('a' * 40) 'test-repository' 'main')) 'An existing exe is insufficient to claim the update succeeded'
Install-Package $fixture.Package $fixture.Install $fixture.Work $fixture.Manifest
Assert-True (Test-InstalledManifest $fixture.Install ('a' * 40) 'test-repository' 'main') 'A complete installed package is verified by its hashes'
foreach ($file in @('appsettings.json', 'appsettings.Office.json', 'company-profile.id', 'updater-path.txt', 'Assets\azienda.json', 'Assets\dati_lavori.json', 'Assets\config_fatture.json', 'updater\updater-settings.json')) {
    Assert-True ((Get-Content -LiteralPath (Join-Path $fixture.Install $file) -Raw).Trim() -eq 'personalized') "Local data preserved: $file"
}
Assert-True ((Get-Content -LiteralPath (Join-Path $fixture.Install 'user-data.json') -Raw).Trim() -eq 'user data') 'Unmanaged user files are preserved'
Set-Content -LiteralPath (Join-Path $fixture.Install 'App.dll') -Value 'corrupted'
Assert-True (-not (Test-InstalledManifest $fixture.Install ('a' * 40) 'test-repository' 'main')) 'Missing or corrupted files force an update even for the same commit'
Assert-True (-not (Test-InstalledManifest $fixture.Install ('a' * 40) 'test-repository' 'other')) 'A manifest for another branch cannot skip an update'

$fixture = New-Fixture
Set-Content -LiteralPath (Join-Path $fixture.Package 'Added.dll') -Value 'new dependency'
$fixture.Manifest = New-PackageManifest $fixture.Package ('a' * 40) 'test-repository' 'main' 'App'
$fixture.Manifest.Files[0].Hash = 'incorrect hash'
Assert-Throws { Install-Package $fixture.Package $fixture.Install $fixture.Work $fixture.Manifest } 'Verification failure rolls back the entire installation'
Assert-True ((Get-Content -LiteralPath (Join-Path $fixture.Install 'App.exe') -Raw).Trim() -eq 'old-App.exe') 'Rollback restores the old exe'
Assert-True ((Get-Content -LiteralPath (Join-Path $fixture.Install 'App.dll') -Raw).Trim() -eq 'old-App.dll') 'Rollback restores old dependencies'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Install 'Added.dll'))) 'Rollback removes newly installed dependencies'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Install 'edilpaint-update.json'))) 'Failed installation never leaves a success manifest'

$fixture = New-Fixture
$locked = [IO.File]::Open((Join-Path $fixture.Install 'App.dll'), 'Open', 'Read', 'None')
try { Assert-Throws { Install-Package $fixture.Package $fixture.Install $fixture.Work $fixture.Manifest } 'Files in use prevent replacement before any file is changed' }
finally { $locked.Dispose() }
Assert-True ((Get-Content -LiteralPath (Join-Path $fixture.Install 'App.exe') -Raw).Trim() -eq 'old-App.exe') 'An in-use dependency leaves the original exe intact'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Install '.edilpaint-update-transaction.json'))) 'Lock failure never starts an installation transaction'

$fixture = New-Fixture
Set-Content -LiteralPath (Join-Path $fixture.Package 'Obsolete.dll') -Value 'old dependency'
$oldManifest = New-PackageManifest $fixture.Package ('b' * 40) 'test-repository' 'main' 'App'
Install-Package $fixture.Package $fixture.Install $fixture.Work $oldManifest
Remove-Item -LiteralPath (Join-Path $fixture.Package 'Obsolete.dll')
$newManifest = New-PackageManifest $fixture.Package ('a' * 40) 'test-repository' 'main' 'App'
Install-Package $fixture.Package $fixture.Install $fixture.Work $newManifest
Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Install 'Obsolete.dll'))) 'Only obsolete files owned by the previous package are removed'

$fixture = New-Fixture
$backup = Join-Path $fixture.Work 'backup-interrupted'
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $fixture.Install 'App.exe') -Destination $backup
Set-Content -LiteralPath (Join-Path $fixture.Install 'App.exe') -Value 'partial installation'
Set-Content -LiteralPath (Join-Path $fixture.Install 'Added.dll') -Value 'partial installation'
Write-AtomicJson (Join-Path $fixture.Install '.edilpaint-update-transaction.json') @{
    InstallPath = $fixture.Install; BackupName = 'backup-interrupted'; Files = @(@{ Path = 'App.exe'; Existed = $true }, @{ Path = 'Added.dll'; Existed = $false })
}
Restore-Installation $fixture.Install $fixture.Work
Assert-True ((Get-Content -LiteralPath (Join-Path $fixture.Install 'App.exe') -Raw).Trim() -eq 'old-App.exe') 'A later updater run recovers an interrupted replacement'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Install 'Added.dll'))) 'Recovery removes files created by an interrupted replacement'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Install '.edilpaint-update-transaction.json'))) 'Recovery clears the transaction only after restoring all files'

Assert-Throws { Get-ContainedPath $root '..\outside.txt' } 'Relative paths cannot escape the installation'
Assert-Throws { Get-ContainedPath $root 'C:\outside.txt' } 'Absolute package paths are rejected'
Assert-Throws { Remove-ManagedDirectory $root (Split-Path -Parent $root) } 'Recursive cleanup cannot delete outside the test workspace'
$fixture = New-Fixture
$fixture.Manifest.Files += @{ Path = '..\outside.txt'; Hash = 'bad' }
Assert-Throws { Install-Package $fixture.Package $fixture.Install $fixture.Work $fixture.Manifest } 'Malformed package paths are rejected before copying'
Assert-True ((Get-Content -LiteralPath (Join-Path $fixture.Install 'App.exe') -Raw).Trim() -eq 'old-App.exe') 'Malformed packages leave the installed app intact'

$output = Get-ExternalOutput cmd.exe /d /c 'echo normal output & echo progress 1>&2 & exit /b 0'
Assert-True ($output.Contains('progress')) 'Native stderr progress with exit zero is not an updater failure'
Assert-Throws { Get-ExternalOutput cmd.exe /d /c 'exit /b 7' } 'Nonzero native exit codes stop the update'

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$archivePath = Join-Path $root 'source.zip'
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = $archive.CreateEntry('project/source.txt')
    $writer = New-Object IO.StreamWriter($entry.Open())
    try { $writer.Write('exact commit content') } finally { $writer.Dispose() }
} finally { $archive.Dispose() }
$extracted = Join-Path $root 'extracted'
Expand-SourceArchive $archivePath $extracted
Assert-True ((Get-Content -LiteralPath (Join-Path $extracted 'project/source.txt') -Raw) -eq 'exact commit content') 'The immutable source archive is extracted without PowerShell module dependencies'
$maliciousPath = Join-Path $root 'malformed.zip'
$archive = [IO.Compression.ZipFile]::Open($maliciousPath, [IO.Compression.ZipArchiveMode]::Create)
try { $null = $archive.CreateEntry('../escaped.txt') } finally { $archive.Dispose() }
Assert-Throws { Expand-SourceArchive $maliciousPath (Join-Path $root 'malformed-source') } 'Source archive entries cannot escape the build directory'
Assert-True (-not (Test-Path -LiteralPath (Join-Path $root 'escaped.txt'))) 'Malformed archives cannot create files outside the build directory'
$restartResult = Join-Path $root 'startup.json'
$running = [pscustomobject]@{ Id = 42; HasExited = $false; ExitCode = 0; MainWindowHandle = [IntPtr]1 }
$exited = [pscustomobject]@{ Id = 42; HasExited = $true; ExitCode = 7; MainWindowHandle = [IntPtr]::Zero }
Assert-Throws { Wait-ForRestartResult $exited $restartResult $true 0 } 'A process that exits immediately is a restart failure'
$result = Wait-ForRestartResult $running $restartResult $true 0
Assert-True ($result.Status -eq 'pending') 'A running process without main-window confirmation is not reported as a successful restart'
$result = Wait-ForRestartResult $running $restartResult $false 0
Assert-True ($result.Status -eq 'started') 'Rollback to a legacy version can reopen without the new startup protocol'
Write-AtomicJson $restartResult @{ Status = 'ready'; ProcessId = 42; Message = 'main window shown'; DialogShown = $false; LogPath = '' }
$result = Wait-ForRestartResult $running $restartResult $true 0
Assert-True ($result.Status -eq 'ready') 'Main-window confirmation completes the restart'
Write-AtomicJson $restartResult @{ Status = 'ready'; ProcessId = 999; Message = 'wrong process'; DialogShown = $false; LogPath = '' }
Assert-Throws { Wait-ForRestartResult $running $restartResult $true 0 } 'Another process cannot confirm the restart'
Write-AtomicJson $restartResult @{ Status = 'failed'; ProcessId = 42; Message = 'startup error'; DialogShown = $true; LogPath = 'startup-error.log' }
$result = Wait-ForRestartResult $exited $restartResult $true 0
Assert-True ($result.Status -eq 'failed' -and $result.Message -eq 'startup error' -and $result.DialogShown) 'Startup failures keep their explanation even after the process exits'

# Exercise the actual Windows launch and argument quoting, without opening the app or its database.
$launchDirectory = Join-Path $root 'restart app with spaces'
New-Item -ItemType Directory -Path $launchDirectory -Force | Out-Null
$launchExe = Join-Path $launchDirectory 'StartupFixture.exe'
Add-Type -OutputAssembly $launchExe -OutputType WindowsApplication -TypeDefinition @'
using System;
using System.IO;
using System.Diagnostics;
public static class StartupFixture {
    public static int Main(string[] args) {
        if (args.Length != 2 || args[0] != "--update-startup-result") return 9;
        string mode = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mode.txt"));
        if (mode == "exit") return 7;
        string status = mode == "failure" ? "failed" : "ready";
        string json = "{\"Status\":\"" + status + "\",\"ProcessId\":" + Process.GetCurrentProcess().Id + ",\"Message\":\"fixture startup\",\"DialogShown\":false,\"LogPath\":\"\"}";
        File.WriteAllText(args[1] + ".tmp", json);
        File.Move(args[1] + ".tmp", args[1]);
        return 0;
    }
}
'@
Set-Content -LiteralPath (Join-Path $launchDirectory 'updater-startup-protocol.txt') -Value '1'
$startupSessions = Join-Path $root 'startup sessions with spaces'
[IO.File]::WriteAllText((Join-Path $launchDirectory 'mode.txt'), 'ready')
$result = Restart-Application $launchExe $launchDirectory $startupSessions
Assert-True ($result.Status -eq 'ready') 'The real Windows process receives the confirmation path with spaces correctly'
Assert-True (@(Get-ChildItem -LiteralPath $startupSessions).Count -eq 0) 'Successful restart sessions are cleaned up'
[IO.File]::WriteAllText((Join-Path $launchDirectory 'mode.txt'), 'failure')
$result = Restart-Application $launchExe $launchDirectory $startupSessions
Assert-True ($result.Status -eq 'failed') 'A real process startup failure is preserved for diagnostics'
[IO.File]::WriteAllText((Join-Path $launchDirectory 'mode.txt'), 'exit')
Assert-Throws { Restart-Application $launchExe $launchDirectory $startupSessions } 'A real Windows process that exits without confirmation is rejected'
Write-Output "Updater safety checks passed: $script:checks. No real installation, repository, scheduler or network was accessed."
