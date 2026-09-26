# IVA preferita del cliente

In Nuovo cliente e Modifica cliente, su Windows e Android, il campo facoltativo IVA preferita permette di scegliere 22%, 10%, RC 10%+22% oppure esclusa. Nessuna preferenza rimuove l'impostazione dal cliente; i clienti già presenti partono senza preferenza.

La selezione di un cliente propone la sua IVA nel preventivo. Se è selezionato un intestatario di fatturazione separato, prevale la sua preferenza; il referente non modifica l'IVA. Senza preferenza si usa il valore predefinito dell'applicazione (configurabile nelle impostazioni Windows; attualmente esclusa su Android). L'IVA resta modificabile manualmente nel preventivo.

La riapertura di un preventivo o di una bozza mantiene l'IVA salvata, anche se la preferenza del cliente è cambiata. Modificare l'anagrafica o aggiornarla dalla sincronizzazione non riscrive l'IVA dei preventivi esistenti. La nuova preferenza viene applicata quando si seleziona il cliente.

Il campo PreferredVatType è conservato in anagrafica, copie locali e sincronizzazione. L'avvio del desktop aggiornato aggiunge la colonna Customers.PreferredVatType, con valore iniziale vuoto, a SQL Server e PostgreSQL; avviare il desktop aggiornato prima di usare il nuovo client Android. Il database operativo non viene modificato durante lo sviluppo. La migrazione va verificata su una copia prima della distribuzione.
