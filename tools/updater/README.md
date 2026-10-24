# Aggiornamenti EdilPaint

L’updater aggiorna un’installazione EdilPaint già configurata dal branch indicato
nelle impostazioni locali. Il database e i dati personali del PC rimangono separati.

## Aggiorna ora

Da **Impostazioni → Aggiornamenti → Configura aggiornamenti** è possibile
creare o modificare la configurazione senza compilare JSON a mano. La finestra
mostra versione e cartella della copia aperta; propone il repository EdilPaint
e il branch `main`. Una configurazione esistente mantiene la propria sorgente,
il branch, i percorsi personalizzati e gli altri parametri. Salvare non avvia
un aggiornamento e non abilita gli aggiornamenti automatici. Se erano già attivi,
la loro registrazione viene riallineata alla configurazione salvata.

Se manca la configurazione, **Aggiorna ora** e l’attivazione degli aggiornamenti
automatici aprono questa finestra. I nuovi file vengono conservati sotto
`%LOCALAPPDATA%\EdilPaintUpdater\installations\<identità della cartella>`,
separati per installazione e utente, senza richiedere scrittura nella cartella
del programma. Una copia inclusa dello script priva di impostazioni non ha
priorità su un updater già configurato per la stessa installazione.

Nella schermata delle impostazioni sono visibili versione, repository e branch.
Per l’uso quotidiano pubblicare il programma in una cartella dedicata e avviarlo
da lì: se viene eseguito da `bin` o `obj` del progetto, la configurazione segnala
che si sta aggiornando una copia di sviluppo. Non impostare `main` per una
distribuzione che deve ricevere aggiornamenti da un altro branch.

Il programma prepara la nuova versione mentre rimane aperto. Scarica il commit,
controlla l’SDK, esegue i test configurati e pubblica il pacchetto in una cartella
temporanea. Chiude l’app solo quando il pacchetto è completo. Se non ci sono
aggiornamenti, oppure la preparazione fallisce, l’app rimane aperta.

Prima di installare verifica che `InstallPath` coincida con la cartella del
programma aperto, che nessun altro updater possieda il blocco dell’installazione
e che i file da sostituire non siano in uso. Un’altra copia dell’app avviata da
una cartella diversa non impedisce l’aggiornamento.

L’installazione conserva un backup e un registro della sostituzione. Verifica
con SHA-256 tutti i file installati prima di dichiarare riuscito l’aggiornamento.
Se la copia fallisce, ripristina la versione precedente. Dopo un’interruzione del
PC o del processo, il prossimo avvio dell’updater recupera il backup prima di
tentare un altro aggiornamento. Finché un ripristino è incompleto, l’app evita di
avviarsi con file appartenenti a versioni diverse.

Dopo un aggiornamento manuale l’updater riapre il programma, anche se ha dovuto
ripristinare la versione precedente. La nuova app conferma il riavvio solo dopo
aver aperto la finestra principale. Un processo che si chiude subito viene
segnalato come errore, anche se Windows aveva accettato di avviarlo. Gli errori
di avvio dopo un aggiornamento vengono mostrati anche con avvio silenzioso
abilitato; le preferenze salvate rimangono invariate. I dettagli dell’eccezione
sono conservati in `%LOCALAPPDATA%\EdilPaintUpdater\startup-errors`.

Se dopo due minuti il processo è ancora in caricamento, l’esito indica un
riavvio da verificare: il programma non viene terminato e i file verificati
restano installati. Un errore di avvio può dipendere anche dalla configurazione
o dal database: non viene annullata automaticamente un’installazione completa
per un errore applicativo. Le vecchie versioni recuperate dal backup possono
confermare solo la presenza di una finestra, senza il nuovo protocollo di avvio.

## Aggiornamenti automatici

La task esegue il controllo al login dell’utente configurato e ogni 30 minuti.
Non installa mentre quella copia del programma è aperta. Un aggiornamento
rinviato verrà tentato di nuovo, senza aspettare il login successivo. La task
ignora avvii sovrapposti e ogni installazione ha anche un blocco esclusivo su file.

Se Windows non permette di registrare la task, resta il collegamento di avvio
automatico dell’utente: in questo caso il controllo avviene solo al login.
Le impostazioni del programma indicano quale modalità è disponibile.

Gli errori automatici vengono registrati, senza mostrare una finestra bloccante a
ogni tentativo. Il pulsante **Aggiorna ora** e la finestra **Diagnostica** permettono
di vedere l’errore e il percorso del log.

La registrazione passa a Windows l’oggetto che definisce la task; l’API richiede
questo argomento prima dei flag, come nella
[documentazione Microsoft](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskfolder-registertaskdefinition).
La ripetizione usa il
[modello del Task Scheduler](https://learn.microsoft.com/en-us/windows/win32/taskschd/repetitionpattern).

## Requisiti e impostazioni

Questa distribuzione prepara ancora il pacchetto sul PC: servono Git, un SDK
.NET compatibile con il progetto (attualmente .NET 10 o successivo), accesso al
repository e a NuGet. L’SDK è necessario per compilare; installare solo il runtime
non basta. Il pacchetto predefinito include invece il runtime Windows, così
l’eseguibile distribuito non dipende da un runtime installato separatamente.

Le impostazioni sono in `updater-settings.json`, accanto allo script. Sono locali
e non vanno committate. Il modello è `updater-settings.example.json`.

- `InstallPath`: cartella contenente l’eseguibile che si vuole aggiornare.
- `SourcePath`: cache Git, distinta dalla cartella dell’app. La compilazione usa
  un archivio del commit scaricato e non esegue `pull`, `reset` o `switch` nel checkout.
- `Branch` e `RepositoryUrl`: sorgente della nuova versione.
- `RuntimeIdentifier`: `win-x64` predefinito; disponibili anche `win-x86` e `win-arm64`.
- `SelfContained`: `true` predefinito, per includere il runtime nel pacchetto.
- `RunTests`: se attivo, i test devono esistere e passare; non vengono saltati in silenzio.
- `LogPath`: percorso del log, con gli output di Git, restore, test e publish.

Vengono conservati `appsettings*.json`, l’identità aziendale, il collegamento
all’updater, le impostazioni dell’updater e i JSON aziendali già presenti in
`Assets`. I file utente estranei al pacchetto non vengono rimossi. I file
obsoleti vengono eliminati solo se erano elencati dal manifesto precedente.

## Riparare un updater già installato

Sul PC con il problema, dalla versione aggiornata del progetto:

```powershell
.\tools\updater\Install-EdilPaintUpdaterTask.ps1 -InstallPath "D:\Cartella programma" -UpdaterPath "C:\EdilPaintUpdater"
```

Usare gli stessi percorsi dell’installazione reale. Senza `-OverwriteSettings`
rimangono le impostazioni esistenti; `InstallPath` viene riallineato al percorso
richiesto. Branch e repository cambiano solo se passati esplicitamente.

Questa operazione iniziale serve sui PC il cui updater precedente non riesce più
a completare un aggiornamento. Quando la nuova app si avvia, aggiorna anche la
copia esterna dello script. Se gli aggiornamenti automatici erano già abilitati,
aggiorna la loro registrazione; se erano disabilitati, mantiene quella scelta.
Una copia vecchia inclusa nell’app non può sovrascrivere un updater con protocollo
più recente.

Per lanciare manualmente lo script con il programma già chiuso:

```powershell
& "C:\EdilPaintUpdater\Update-EdilPaint.ps1" -ManualUpdate
```

## Stato e verifiche

`edilpaint-update.json`, nella cartella dell’app, identifica commit, configurazione
del pacchetto e hash dei file installati. Il vecchio `deployed-commit.txt` rimane
solo diagnostico: la sua presenza non permette di saltare un aggiornamento.

`.edilpaint-update-status.json` contiene il risultato dell’ultimo tentativo.
`.edilpaint-update-transaction.json` esiste solo finché una sostituzione o il suo
ripristino non sono completati: **non eliminarlo per aggirare il blocco di avvio**.
Le copie di ripristino sono in `%LOCALAPPDATA%\EdilPaintUpdater\work` del PC che
ha eseguito l’aggiornamento. Dopo un’interruzione su una cartella condivisa,
eseguire il recupero dallo stesso PC, dove è disponibile il backup.

La suite .NET include prove su Windows PowerShell 5.1 delle funzioni effettive
dell’updater: configurazioni preservate, hash, rollback, file in uso, recupero
dopo interruzione e rifiuto di percorsi fuori dall’installazione. I test usano
cartelle temporanee e non aggiornano un’installazione reale o task di Windows.
