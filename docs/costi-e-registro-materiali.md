# Percentuali dei costi e registro materiali

## Finito e costi

Lo sconto di acquisto è letto dal **fornitore indicato nell'ordine**, nell'anagrafica fornitori; non dal cliente del preventivo. Il confronto tollera maiuscole e spazi iniziali/finali. Se il fornitore manca o non è riconosciuto, lo sconto automatico è zero: verificare **Dettagli materiali** e l'anagrafica.

Nella finestra **Costi e guadagno**, le opzioni **Da anagrafica fornitore** e **Dalle impostazioni** collegano rispettivamente sconto e riduzione prudenziale alle loro sorgenti. Per un valore particolare, anche zero, togliere la relativa selezione, inserire il valore e salvare il calcolo. Le scelte vengono conservate nel calcolo condiviso.

Le vecchie registrazioni a zero, senza indicazione della sorgente, usano i valori correnti di anagrafica e impostazioni. I vecchi valori non zero vengono conservati come manuali. I collegamenti espliciti seguono i valori correnti anche quando si genera **Finito e costi** o si rigenera **PDF costi**, senza alterare il calcolo salvato soltanto per averlo aperto. La finestra distingue un risultato appena aggiornato da un calcolo già salvato.

Il costo squadra continua a usare gli interventi del calendario. Lo sconto riduce il costo d'acquisto dei materiali; la percentuale prudenziale riduce solo il guadagno positivo. I valori vengono applicati prima di generare il PDF dei costi.

## Registro ordini materiali

Da **Ordini → Registro ordini materiali** si apre una tabella con data ordine, fornitore, cliente/riferimento/cantiere, stato materiali, consegna prevista, stato lavoro e numero ordine. Comprende anche i lavori **Finito**, **Archiviato** e gli altri stati che hanno dati d'ordine: il registro non usa il filtro degli ordini attivi del calendario.

La tabella è di sola consultazione e si aggiorna dal database ogni 30 secondi, senza interrompere la compilazione dei filtri. È possibile cercare per testo, filtrare per fornitore e intervallo della **data ordine**, aggiornare manualmente e copiare le righe selezionate. Le date mancanti rimangono visibili come **Non indicata** quando non è impostato un intervallo.

Il registro mostra i dati già compilati in **Dettagli materiali**, una riga per preventivo: non ricostruisce acquisti o date che non sono mai stati registrati e non è un elenco di più acquisti dello stesso preventivo. Gli ordini gestiti dal cliente sono identificati separatamente dai fornitori. Lo storico dei preventivi conserva la sua interfaccia.
