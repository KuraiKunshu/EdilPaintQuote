# Catalogo aziendale indipendente

La ricerca materiali consulta esclusivamente il catalogo dell'azienda, per nome o descrizione, anche con ricerche di un solo carattere. Ogni risultato riporta prezzo e unità di misura. La selezione mantiene l'identità del materiale, la descrizione, il prezzo, l'unità e l'indicazione di bene significativo. I materiali aziendali riservati al calcolo dei costi restano separati.

Non sono richiesti accessi a portali di produttori, sessioni di navigazione o un browser integrato. I prezzi si gestiscono nel catalogo e non vengono aggiornati da servizi esterni.

Gli automatismi generici per lavorazione rimangono disponibili. Quelli facoltativi per finestre leggono misure esplicite in centimetri nel nome del prodotto, per esempio `FINESTRA 78x98`, `FINESTRA (78×98)` o `FINESTRA 078/098`. Non ricavano più dimensioni da codici articolo del produttore; misure assenti o contraddittorie vengono segnalate. I prefissi sono configurabili, con `FINESTRA` come valore iniziale.

I cataloghi, i prezzi, i preventivi e le impostazioni già salvati dall'azienda non vengono cancellati. Per le vecchie regole basate esclusivamente su codici articolo, aggiungere le misure esplicite ai prodotti utilizzati nel calcolo. I prefissi personalizzati restano validi.

Per una nuova azienda usare `tools/Publish-Company.ps1` come descritto in [NUOVA-AZIENDA.md](NUOVA-AZIENDA.md): il pacchetto parte senza dati, loghi, cataloghi o credenziali della precedente installazione. Le regole e i documenti specifici dei serramenti rimangono opzionali.
