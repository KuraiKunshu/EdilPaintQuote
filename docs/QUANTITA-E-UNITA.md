# Quantità e unità di misura

Materiali, lavorazioni e costi aziendali accettano quantità positive come `10000`, `1000000` e valori superiori, anche frazionari con fino a nove decimali. Non ci sono limiti di lunghezza nelle caselle; il limite tecnico condiviso tra applicazioni e database è 9.999.999.999.999.999.999,999999999 (19 cifre intere e 9 decimali). I valori fuori intervallo o con precisione eccessiva vengono rifiutati, senza arrotondamenti silenziosi. Digitare le quantità senza separatori delle migliaia: `10000` per diecimila. Si può digitare `2,5` oppure `2.5`; le schermate e i documenti visualizzano la virgola. Le unità disponibili sono pz, m, m², m³, kg, l, h, giorni e a corpo. Il prezzo si riferisce all'unità scelta: cambiare unità non converte automaticamente quantità o prezzo.

L'unità si imposta nel catalogo, nella riga di inserimento o nella modifica della voce. Viene conservata nel preventivo, nelle bozze, nello storico, nel calcolo del guadagno, nelle schede lavoro e negli ordini al fornitore. Le righe precedenti senza unità assumono pz e mantengono quantità e importi.

Gli automatismi per le finestre richiedono quantità intere in pz; le righe incompatibili non vengono arrotondate silenziosamente. Le quantità già quotate vengono sottratte dai materiali automatici soltanto quando l'unità corrisponde a quella del catalogo.

## Aggiornamento di un'installazione esistente

Questa modifica amplia il formato del database: Quantity nelle tabelle QuoteMaterials e QuoteLabors passa da intero a decimal/numeric(28,9); UnitOfMeasure viene aggiunta a righe e cataloghi con valore iniziale pz. L'aggiornamento è idempotente e viene eseguito dal desktop al primo avvio della nuova versione, con i permessi di modifica dello schema già richiesti dall'applicazione. I valori interi esistenti e quelli del precedente formato sperimentale (18,3) sono rappresentabili senza perdita nel nuovo formato; entrambi gli schemi vengono aggiornati.

Prima della distribuzione effettuare un backup del database, chiudere le vecchie versioni e aggiornare tutte le postazioni Windows e l'app Android che condividono l'archivio. Avviare per prima una postazione Windows aggiornata per adeguare lo schema. Le vecchie applicazioni che leggono Quantity come intero non sono compatibili con il nuovo tipo: non usare versioni miste e non ripristinare un vecchio eseguibile sul database aggiornato.

Lo sviluppo e i test non modificano il database operativo. La verifica automatica copre parser, calcoli, persistenza locale, mapping EF per entrambi i provider e compatibilità dei modelli Android; la migrazione va verificata anche su una copia del database prima dell'aggiornamento operativo.
