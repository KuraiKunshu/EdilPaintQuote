# Trello, n8n e preventivi

Il NAS copia le schede Trello in NeonDB. Nel programma l'operatore sceglie la scheda e la associa al preventivo: la sincronizzazione **non crea clienti, non cerca corrispondenze e non associa schede a preventivi**. Il numero del preventivo e l'anagrafica restano gestiti dal programma.

Una scheda può essere associata manualmente a più preventivi. Ogni preventivo conserva il proprio collegamento tramite l'ID Trello, che rimane valido dopo rinomina o spostamento della scheda. La duplicazione di un preventivo richiede una nuova scelta della scheda. I dati operativi e il riferimento Trello sono riservati alla scheda lavoro e non entrano nel PDF commerciale.

## Uso nel programma Windows

Nel preventivo premere **Scegli scheda Trello**, cercare la scheda, leggerne l'anteprima e confermare con **Associa al preventivo**. L'elenco parte senza selezione: aprire la finestra o selezionare una riga non salva alcun collegamento. Cliente, riferimento, fatturazione, cantiere, prezzi e note commerciali rimangono quelli scelti dall'operatore.

Il pulsante è disponibile anche prima di scegliere il cliente. Per un preventivo già salvato, il menu dello storico e i menu comuni delle azioni contengono **Scheda Trello**: associazione, sostituzione e rimozione si salvano con un aggiornamento dedicato, senza rigenerare il PDF. Queste modifiche a preventivi salvati richiedono il database raggiungibile e rispettano il controllo delle revisioni fra postazioni.

**Ricarica dal database** rilegge la copia sincronizzata; non forza una nuova lettura di Trello. La finestra mostra quando i dati sono stati sincronizzati e segnala l'uso della cache locale. Le nuove bozze conservano la scelta anche prima dell'assegnazione del cliente. Nuovi preventivi e duplicati partono senza collegamento; la modifica e il recupero della stessa bozza conservano il collegamento esplicito.

La scheda lavoro recupera la scheda per il solo ID associato e riporta titolo, link, bacheca/lista, appuntamento Trello, descrizione, campi personalizzati e checklist. Questi dati rimangono separati dai recapiti e dalla programmazione del lavoro già confermati nel preventivo. I commenti e i riferimenti agli allegati restano nella copia sincronizzata e non diventano automaticamente istruzioni stampate o allegati commerciali. Se il database non risponde, la stampa può utilizzare lo snapshot della scheda salvato con il preventivo, con un avviso di verifica.

Dopo una modifica del collegamento a un preventivo già salvato, la cache locale dello storico si allinea alla sincronizzazione ordinaria del programma: una riapertura immediata offline può mostrare temporaneamente il collegamento precedente. Il collegamento nel database è già salvato.

## File pronti

- `tools/trello-n8n/trello-to-neon.workflow.json`: workflow da importare, inizialmente inattivo e senza riferimenti a credenziali.
- `tools/trello-n8n/schema.sql`: creazione idempotente della sola tabella `public."TrelloCards"`.
- `tools/trello-n8n/upsert.sql`: aggiornamento parametrizzato della copia Trello.
- `tools/trello-n8n/build-workflow.mjs` e `sync-core.mjs`: sorgenti riproducibili del workflow.
- `tools/trello-n8n/sync-core.test.mjs`: verifiche offline del formato, della paginazione, dell'idempotenza e dell'assenza di associazioni automatiche.
- `tools/trello-n8n/verify-workflow.json`: verificatore manuale separato, con quattro nodi, che effettua soltanto GET su Trello e SELECT sul database.

Il workflow usa nodi standard: Manual Trigger 1, Schedule Trigger 1.2, HTTP Request 4.2, Code 2, If 2.2, Postgres 2.5. La compatibilità con la versione effettiva installata sul NAS va verificata durante l'importazione; non richiede community node, moduli JavaScript aggiuntivi o accesso HTTP dai nodi Code.

## Configurazione sul NAS

1. Eseguire `schema.sql` sul database Neon usato dal programma, oppure avviare il programma aggiornato che prepara lo schema. Il workflow non crea né modifica tabelle clienti o preventivi.
2. Nel progetto n8n importare il JSON tramite **Import from File**. L'importazione non attiva la pianificazione.
3. Nei tre nodi HTTP selezionare una credenziale **Trello API** autorizzata a leggere la bacheca. Usare le credenziali conservate in n8n, senza incollarle nel workflow o nel codice. Le richieste a Trello sono esclusivamente GET.
4. Nei due nodi Postgres selezionare una credenziale PostgreSQL per lo stesso database Neon del programma, con TLS e verifica del certificato. Dopo il bootstrap, il ruolo della sincronizzazione necessita solo di `SELECT`, `INSERT` e `UPDATE` sulla tabella `TrelloCards`; non necessita di scrittura su `Customers` o `Quotes`.
5. Nel nodo **Configurazione** controllare l'elenco `boardIds`. È già predisposto l'ID completo della bacheca **Sopralluoghi** (`68eb83dcac50df446b956713`). Per altre bacheche aggiungere gli ID completi, senza cambiare il codice di associazione nel programma.
6. Eseguire manualmente una prima sincronizzazione e controllare l'esito di entrambi i rami: schede con commenti riletti e schede con commenti conservati. Una prima importazione con molti commenti può richiedere diversi minuti.
7. Confrontare conteggi e campioni con Trello, comprese schede archiviate, checklist completate e schede senza appuntamento. Verificare che nessuna associazione sia stata creata nel programma.
8. Dopo il controllo, pubblicare/attivare il workflow per l'esecuzione ogni 15 minuti. In caso di modifica dell'intervallo, valutare la durata della prima importazione per evitare esecuzioni sovrapposte.

Il NAS deve poter raggiungere Trello e Neon in uscita. L'indirizzo Tailscale di n8n può rimanere privato: questa versione usa letture programmate e non richiede un webhook pubblico in ingresso.

## Verifica manuale indipendente

Importare `tools/trello-n8n/verify-workflow.json` come workflow separato **EdilPaint — Verifica Trello e database**. Selezionare le credenziali Trello e PostgreSQL nei suoi due nodi dedicati ed eseguirlo manualmente. Non possiede una pianificazione e non modifica schede, righe, associazioni o impostazioni.

Il verificatore legge da Trello esclusivamente ID, stato di archiviazione e contatori delle schede Sopralluoghi; gestisce anche più di 1000 schede. Confronta tutte le identità con la copia database e restituisce conteggi di schede attive/archiviate, voci checklist, allegati e commenti. Restituisce anche ID mancanti, eventuali righe aggiuntive conservate e discrepanze per singola scheda. Il database calcola i contatori dal payload, senza restituire descrizioni, commenti o contenuti completi nell'output del controllo.

Il campo `QuotesWithManualTrelloAssociation` conta i preventivi che hanno un collegamento Trello salvato. Subito dopo l'importazione iniziale deve essere zero se nessun operatore ha ancora associato schede. Le successive associazioni manuali sono previste e il relativo conteggio può crescere.

Un controllo completo richiede `MissingCards = 0`, `ArchiveMismatches = 0`, `ChecklistMismatches = 0`, `AttachmentMismatches = 0` e `CommentMismatches = 0`. `ExtraCards` può segnalare dati conservati di schede eliminate o trasferite fuori dalle bacheche sincronizzate; il controllo li segnala senza cancellarli. Se qualcuno modifica Trello fra sincronizzazione e verifica, eseguire prima la sincronizzazione e ripetere il controllo.

## Dati copiati

Ogni riga contiene ID, bacheca, lista, titolo, descrizione, URL, stato archiviato, scadenza, ultima attività Trello e momento di sincronizzazione. `PayloadJson` conserva il contenuto della scheda con:

- checklist e singole voci, con stato di completamento;
- etichette, membri e riferimenti alle liste;
- valori dei campi personalizzati e relative definizioni, inclusi nomi e opzioni;
- commenti con autore e data, recuperati separatamente per evitare i limiti dei commenti incorporati negli elenchi;
- metadati e collegamenti degli allegati, con eventuali anteprime fornite da Trello;
- i campi geografici o altri campi restituiti da Trello.

I binari di foto e documenti non vengono scaricati: i collegamenti privati continuano a richiedere accesso a Trello. Gli allegati non diventano automaticamente allegati del preventivo.

Le chiavi del payload usate dalla scheda lavoro sono quelle Trello: `checklists[].checkItems[]`, `customFieldItems[]`, `customFields[]`, `address` e `locationName` quando disponibili. Tutti i commenti restano consultabili nella copia, senza riversare automaticamente l'intera conversazione nella scheda lavoro.

## Aggiornamenti e recupero degli errori

Ogni esecuzione rilegge tutte le schede delle bacheche configurate, comprese quelle archiviate. La lettura richiede pagine da 1000 elementi ordinate per ID decrescente e procede con il cursore `before`; i commenti seguono una paginazione indipendente da 1000. Pagine ripetute, corpi di errore o raggiungimento del limite di 200 pagine interrompono la lettura invece di presentarla come completa.

I commenti vengono riletti quando la scheda cambia, quando la copia locale manca o è incompleta e comunque almeno ogni 24 ore. Per una rilettura straordinaria impostare temporaneamente `forceCommentsRefresh = true` nel nodo Configurazione, eseguire e poi ripristinare `false`. Se Trello segnala zero commenti nella scheda, l'array viene svuotato senza richieste aggiuntive. Le checklist e gli altri contenuti sono riletti a ogni esecuzione.

Le richieste sono distanziate di 500 ms; gli errori HTTP, compreso 429, vengono ritentati fino a cinque volte con attesa di cinque secondi. Se l'errore persiste il workflow fallisce e conserva i dati precedenti. Non è abilitato **Continue on fail**. È opportuno collegare il normale monitoraggio degli errori già usato sul NAS, evitando di lasciare fallimenti inosservati.

L'upsert usa l'ID come chiave e parametri SQL, così apostrofi, virgole e testo dei commenti rimangono dati. Ripetere un'esecuzione non duplica le schede. Un'esecuzione iniziata prima non può sovrascrivere una copia sincronizzata da un'esecuzione più recente; anche la data dell'ultima attività Trello viene controllata.

L'assenza di una scheda in una risposta non viene interpretata come cancellazione: può dipendere da permessi, spostamenti o errori. Le righe già copiate e i collegamenti manuali vengono conservati. Questa prima versione aggiorna lo stato di archiviazione ma **non rileva automaticamente le eliminazioni definitive o lo spostamento verso una bacheca esclusa dalla sincronizzazione**. Per distinguerli da una perdita di accesso servirebbe un flusso aggiuntivo di eventi verificati; non si cancellano righe per deduzione.

## Verifica locale dei sorgenti

Da `tools/trello-n8n`:

```powershell
node build-workflow.mjs
node --test sync-core.test.mjs
node verify-workflow.mjs
node --test verify-workflow.test.mjs
```

Questi test non contattano Trello, Neon o n8n. Provano anche pagine oltre 1000 elementi, aggiornamenti fuori ordine, schede archiviate e testo contenente frammenti SQL. Non sostituiscono la prima esecuzione controllata sul NAS con le sue credenziali.

## Riferimenti tecnici

- [Trello: risorse annidate e parametri di schede/commenti](https://developer.atlassian.com/cloud/trello/guides/rest-api/nested-resources/).
- [Trello: API e paginazione](https://developer.atlassian.com/cloud/trello/guides/rest-api/api-introduction/).
- [n8n: HTTP Request e paginazione](https://docs.n8n.io/code/cookbook/http-node/pagination/).
- [n8n: riferimento ai dati degli altri nodi](https://docs.n8n.io/code/builtin/output-other-nodes/).
- [n8n: integrazione Trello e credenziali predefinite](https://docs.n8n.io/integrations/builtin/app-nodes/n8n-nodes-base.trello/).

L'implementazione del formato esportato è stata confrontata con i sorgenti ufficiali dei nodi HTTP Request e Postgres n8n. Il workflow esportato rimane inattivo finché non viene configurato e verificato sul NAS.
