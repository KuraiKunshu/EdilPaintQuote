param(
    [string]$Adb = "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe",
    [string]$Serial = "emulator-5554",
    [string]$OutputDirectory = "$PSScriptRoot/../../EdilPaintPreventibiviGen.Android/bin/mobile-qa"
)
$ErrorActionPreference = 'Stop'
$package = 'it.edilpaint.preventivi.smoketest'
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

function Invoke-Adb([string[]]$Arguments) {
    $output = & $Adb -s $Serial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB: $output" }
    return $output
}

function Read-Ui {
    $null = Invoke-Adb @('shell', 'uiautomator', 'dump', '/sdcard/edilpaint-smoke.xml')
    $xml = Invoke-Adb @('shell', 'cat', '/sdcard/edilpaint-smoke.xml')
    return [xml]($xml -join "`n")
}

function Tap-Text([string]$Text) {
    $node = $null
    for ($attempt = 0; $attempt -lt 4 -and !$node; $attempt++) {
        $ui = Read-Ui
        $node = @($ui.SelectNodes('//node')) | Where-Object { $_.text -eq $Text -and $_.clickable -eq 'true' } | Select-Object -First 1
    }
    if (!$node) { throw "Controllo non trovato: $Text" }
    $coordinates = [regex]::Matches($node.bounds, '\d+') | ForEach-Object { [int]$_.Value }
    $x = [int](($coordinates[0] + $coordinates[2]) / 2)
    $y = [int](($coordinates[1] + $coordinates[3]) / 2)
    $null = Invoke-Adb @('shell', 'input', 'tap', "$x", "$y")
}

function Capture([string]$Name) {
    $ui = Read-Ui
    if (!(@($ui.SelectNodes('//node')) | Where-Object { $_.package -eq $package })) { throw "App non visibile: $Name" }
    $ui.Save((Join-Path $OutputDirectory "$Name.xml"))
    $null = Invoke-Adb @('shell', 'screencap', '-p', '/sdcard/edilpaint-smoke.png')
    $null = Invoke-Adb @('pull', '/sdcard/edilpaint-smoke.png', (Join-Path $OutputDirectory "$Name.png"))
    Write-Output "OK $Name"
}

try {
    foreach ($size in @('2076x2152', '1080x2400')) {
        $null = Invoke-Adb @('shell', 'wm', 'size', $size)
        $null = Invoke-Adb @('shell', 'wm', 'density', '420')
        $null = Invoke-Adb @('shell', 'am', 'force-stop', $package)
        $null = Invoke-Adb @('shell', 'monkey', '-p', $package, '-c', 'android.intent.category.LAUNCHER', '1')
        foreach ($screen in @('Dettaglio', 'Guadagno reale', 'Collaborazione', 'Catalogo', 'Voce preventivo', 'Impostazioni email')) {
            Tap-Text $screen
            Capture "$size-$($screen.Replace(' ', '-'))"
            $null = Invoke-Adb @('shell', 'input', 'keyevent', '4')
        }
    }
    $null = Invoke-Adb @('shell', 'wm', 'size', '2076x2152')
    $null = Invoke-Adb @('shell', 'am', 'force-stop', $package)
    $null = Invoke-Adb @('shell', 'monkey', '-p', $package, '-c', 'android.intent.category.LAUNCHER', '1')
    Tap-Text 'PDF di prova'
    $ui = Read-Ui
    if (!(@($ui.SelectNodes('//node')) | Where-Object { $_.text -eq 'PDF creato' })) { throw 'Generazione PDF non completata.' }
    Capture 'pdf-created'
    Write-Output 'PDF generato correttamente'
}
finally {
    $null = Invoke-Adb @('shell', 'wm', 'size', 'reset')
    $null = Invoke-Adb @('shell', 'wm', 'density', 'reset')
}
