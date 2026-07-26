# EdilPaint - Smart App Control su Windows 11

Questa procedura serve quando Windows blocca `EdilPaintPreventibiviGen.exe` o gli script
updater con il messaggio:

> Controllo intelligente delle app ha bloccato un'app che potrebbe non essere sicura

Il problema non indica per forza un virus: Smart App Control blocca anche programmi interni
non firmati o senza reputazione Microsoft sufficiente.

## Soluzione stabile scelta

Sui PC aziendali che usano EdilPaint, disattivare Smart App Control.

Non disattivare:
- Microsoft Defender
- Firewall
- Windows Update
- Protezione in tempo reale

Disattiviamo solo Smart App Control, perche' e' il controllo che blocca l'app interna.

## Procedura su ogni PC

1. Chiudere EdilPaint e PowerShell.
2. Aprire `Impostazioni`.
3. Andare in `Privacy e sicurezza`.
4. Aprire `Sicurezza di Windows`.
5. Aprire `Controllo app e browser`.
6. Aprire `Impostazioni Controllo intelligente app`.
7. Selezionare `Disattivato`.
8. Riavviare il PC.
9. Avviare l'updater EdilPaint.
10. Avviare EdilPaint.

## Se PowerShell blocca ancora gli script updater

Aprire PowerShell normale, non per forza amministratore, nella cartella updater e lanciare:

```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

Se chiede conferma, premere `T` oppure `S`.

Poi, se i file updater sono stati scaricati o copiati da internet/chat/mail, si puo' rimuovere
il blocco file con:

```powershell
Unblock-File -Path ".\Update-EdilPaint.ps1"
Unblock-File -Path ".\Install-EdilPaintUpdaterTask.ps1"
```

Nota: `Unblock-File` aiuta con il blocco dei file scaricati, ma non sostituisce la
disattivazione di Smart App Control.

## Test finale

Dalla cartella updater:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Update-EdilPaint.ps1
```

Poi aprire:

```text
<InstallPath>\EdilPaintPreventibiviGen.exe
```

Se l'updater funziona e l'exe si apre, il PC e' pronto.

## Nota importante

Su alcune versioni di Windows 11, una volta disattivato Smart App Control potrebbe non essere
semplice riattivarlo senza aggiornamenti/reset del sistema. La scelta e' intenzionale per i PC
che devono usare EdilPaint come programma interno aziendale.

La soluzione definitiva alternativa resta firmare digitalmente il programma con un certificato
di code signing.
