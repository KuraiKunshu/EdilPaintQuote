# Fotografie della scheda lavoro

Questo worker copia nel database le sole fotografie richieste dal programma per una scheda Trello già associata manualmente. Non collega schede, clienti o preventivi e non scarica tutti gli allegati dell'archivio.

## Configurazione

1. Applicare `photos-schema.sql` sul database Neon del programma, oppure avviare il programma aggiornato.
2. Importare `photo-workflow.json` nel progetto n8n. Il file è inizialmente inattivo e non contiene riferimenti a credenziali.
3. Nei nodi **Prendi una richiesta** e **Salva foto e stato richiesta** selezionare la connessione Postgres già configurata.
4. Nel solo nodo **Scarica da Trello con autorizzazione** selezionare una credenziale **Header Auth**, con nome dell'header `Authorization` e questo formato per il valore:

   ```text
   OAuth oauth_consumer_key="API_KEY_TRELLO", oauth_token="TOKEN_TRELLO"
   ```

   I valori reali vanno inseriti dall'utente direttamente nella schermata della credenziale n8n. Non inserirli nel workflow, nei parametri dei nodi, nelle tabelle o in chat. La credenziale Trello API standard di n8n usa parametri nella query e non è adatta al download privato degli allegati.
5. Nel programma richiedere le fotografie per una scheda associata manualmente. Eseguire una prova del worker e verificare `Status = completed`, contenuto delle foto e generazione del PDF.
6. Pubblicare il workflow. La pianificazione controlla la coda ogni minuto e prende una richiesta alla volta. Il primo caricamento può richiedere il minuto successivo e il tempo necessario a scaricare le fotografie.

Il primo GET autenticato usa esclusivamente `https://api.trello.com/1/cards/.../attachments/.../download/...` e non segue redirect. Se Trello restituisce direttamente il file, viene utilizzato. Se restituisce un collegamento firmato allo storage S3, il secondo GET non utilizza alcuna credenziale. I collegamenti firmati non vengono salvati nella cache fotografica.

### Recuperare key e token

Aprire [Amministrazione app Trello](https://trello.com/apps/admin) e selezionare l'app già usata dalla connessione n8n. Nel pannello **Trello Auth / Chiave API** copiare la API key esistente. Riutilizzare il token già conservato, oppure usare il collegamento **Token** accanto alla chiave per ottenerne uno; l'autorizzazione e la copia del token vanno completate dall'utente. Non serve rigenerare la API key esistente.

Nella credenziale n8n **Trello fotografie**, inserire `Authorization` nel campo **Name** e comporre il campo **Value** nel formato OAuth indicato sopra, mantenendo le virgolette. Impostare **Allowed HTTP Request Domains → Specific Domains → api.trello.com**, quindi salvare. Key e token rimangono nella credenziale n8n; non vanno copiati nella chat o nel codice del progetto.

La [guida ufficiale Trello](https://developer.atlassian.com/cloud/trello/guides/rest-api/api-introduction/#managing-your-api-key) descrive il recupero della chiave e il collegamento per generare il token.

## Formato e comportamento

Il programma crea `TrelloPhotoRequests` con ID della scheda, `RequestedAtUtc` e `ExpectedAttachmentIdsJson`, un array JSON di ID allegati. Il worker legge i metadati da `TrelloCards.PayloadJson`; quindi la sincronizzazione ordinaria della scheda deve essere già completata.

Sono supportati caricamenti Trello JPEG, PNG, WebP, GIF e BMP. File PDF, SVG, HEIC, video e collegamenti esterni non sono fotografie incorporabili da questo worker. Il nome canonico del download proviene da `fileName` o dal percorso dell'allegato; il nome rinominabile in Trello non viene usato per costruire il download. Ogni file deve corrispondere alla dimensione dichiarata e al formato raster riconosciuto.

Limiti dichiarati: 25 MB per fotografia, 100 fotografie e 200 MB complessivi per richiesta. Superare i limiti o non recuperare una fotografia richiesta porta lo stato a `failed`, con un messaggio per il programma. Non viene dichiarato completo un insieme parziale. Questi limiti sono costanti in `photo-core.mjs`; ogni modifica richiede la rigenerazione del JSON.

Una richiesta senza fotografie viene completata senza effettuare download. Le richieste interrotte restano `processing` e sono recuperabili dopo 15 minuti misurati da `StartedAtUtc`. La guardia su `RequestedAtUtc` conserva l'intera precisione PostgreSQL: un vecchio worker non deve chiudere una richiesta sostituita nel frattempo dal programma. La cache non viene cancellata e il programma seleziona solo gli ID attesi dalla richiesta corrente.

Il worker conserva le immagini in `TrelloPhotos.ContentBase64`, accessibili con la stessa connessione al database del programma. Le esecuzioni n8n non salvano i payload di successo o errore per evitare copie aggiuntive dei binari e dei collegamenti firmati.

## Test e rigenerazione

```powershell
node tools/trello-n8n/photo-workflow.mjs
node --test tools/trello-n8n/photo-workflow.test.mjs
```

I test sono offline e non scaricano immagini o modificano servizi reali.

## Riferimenti

- [Trello: download autenticato degli allegati](https://community.developer.atlassian.com/t/update-authenticated-access-to-s3/43681).
- [Trello: formato dell'header Authorization](https://developer.atlassian.com/cloud/trello/guides/rest-api/authorization/).
- [Sorgente ufficiale della credenziale Trello API n8n](https://github.com/n8n-io/n8n/blob/master/packages/nodes-base/credentials/TrelloApi.credentials.ts).
- [Sorgente ufficiale HTTP Request n8n: gestione binari e redirect](https://github.com/n8n-io/n8n/blob/master/packages/nodes-base/nodes/HttpRequest/V3/HttpRequestV3.node.ts).
