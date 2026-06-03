# EdilPaint updater

Questi script aggiornano il programma solo quando l'utente accede a Windows.

Il codice scaricato da GitHub e' uguale per tutti i PC. Il percorso di installazione invece resta locale nel file:

```text
C:\EdilPaintUpdater\updater-settings.json
```

Quel file non va committato.

Nel repository c'e' anche `updater-settings.example.json`, utile come modello da copiare e modificare su ogni PC.

## Installazione PC 1

Da PowerShell, nella cartella `tools\updater` del progetto:

```powershell
.\Install-EdilPaintUpdaterTask.ps1 -InstallPath "D:\EdilPaint Condiviso\Preventivi\Programma Preventivi" -RunTests
```

## Installazione PC 2

Da PowerShell, nella cartella `tools\updater` del progetto:

```powershell
.\Install-EdilPaintUpdaterTask.ps1 -InstallPath "$env:USERPROFILE\Documents\EdilPaint Preventivi" -RunTests
```

## Cosa fa

1. All'accesso a Windows parte l'attivita pianificata.
2. Lo script aspetta 60 secondi, salvo configurazione diversa.
3. Lo script controlla il branch `main` su GitHub.
4. Se non ci sono nuovi commit, non modifica nulla.
5. Se ci sono nuovi commit, scarica il codice, esegue build/test e pubblica l'app.
6. Prima di copiare i file controlla di nuovo che il programma sia chiuso.
7. Copia i file pubblicati nella cartella locale del PC.
8. Non sovrascrive `appsettings.json`.
9. Se il programma e' gia' aperto, salta l'aggiornamento.

Per cambiare l'attesa iniziale:

```powershell
.\Install-EdilPaintUpdaterTask.ps1 -InstallPath "$env:USERPROFILE\Documents\EdilPaint Preventivi" -StartDelaySeconds 30 -RunTests -OverwriteSettings
```

## Esecuzione manuale

Per provare subito l'aggiornamento:

```powershell
& "C:\EdilPaintUpdater\Update-EdilPaint.ps1"
```

## Log

Il log resta qui:

```text
C:\EdilPaintUpdater\logs\update.log
```
