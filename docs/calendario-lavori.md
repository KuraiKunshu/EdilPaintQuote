# Calendario lavori

Apri **Calendario** nella barra superiore del programma. La vista settimanale mostra i cantieri con gli orari e la squadra assegnata. Nell'elenco a sinistra trovi gli stessi ordini della finestra **Ordini**, senza il limite di risultati dello storico: seleziona un ordine e premi **Programma intervento**. Sono inclusi i preventivi confermati con dati d'ordine registrati e quelli con materiali ordinati dal cliente, secondo il criterio già usato nella finestra Ordini.

Puoi programmare più interventi per lo stesso ordine, anche su giornate diverse e con squadre diverse. Scegli uno o più dipendenti e indica giornata intera, mezza giornata mattina o pomeriggio, oppure un orario libero dalle ore X alle ore Y. Il comando **Segna assenza** registra ferie o altre indisponibilità.

In **Impostazioni → Dipendenti** gestisci l'elenco condiviso. Il nome è obbligatorio; cognome e sigla sono facoltativi. Le sigle vengono salvate in maiuscolo e devono essere diverse tra i dipendenti attivi. Puoi usare, per esempio, `MR` per Mario Rossi.

In **Impostazioni → Calendario** definisci gli orari standard nel formato `08:00` e scegli se mostrare sigle o nomi. Quando manca una sigla viene mostrato il nome. Conferma con **Salva**: questi dati vengono salvati nel database condiviso, insieme alla programmazione e alle assegnazioni.

Cambiare gli orari standard aggiorna anche gli interventi e le assenze già programmati **da oggi in avanti**, se usano giornata intera, mattina o pomeriggio. Gli orari liberi e le attività passate o concluse mantengono gli orari precedenti.

Il programma controlla che una persona non abbia due impegni sovrapposti, comprese le assenze. Se il cambio degli orari crea un conflitto, l'intera modifica degli orari e degli interventi coinvolti viene rifiutata. Anche una programmazione su più giorni viene salvata interamente oppure rifiutata. Se un altro PC ha già modificato gli stessi dati, ricarica e ripeti la modifica per evitare sovrascritture.

Gli interventi di ordini che non figurano più nell'elenco Ordini sono conservati e consultabili tramite **Mostra interventi di ordini non attivi**. Le loro visite future ancora programmate liberano la squadra e non vengono aggiornate dagli orari standard; gli interventi passati o completati conservano lo storico degli impegni. Le assenze rimangono sempre visibili e occupano la fascia prevista.

Se un ordine ritorna nell'elenco e le vecchie assegnazioni si sovrappongono a nuovi impegni, il calendario segnala entrambi gli interventi e le persone coinvolte. Apri le visite segnalate e modifica gli orari o la squadra per risolvere il conflitto.

Il calendario e i tab condivisi delle impostazioni si aggiornano automaticamente ogni **10 secondi** mentre sono visibili. Le modifiche ancora da salvare vengono conservate; un avviso segnala eventuali aggiornamenti da un altro PC.

Senza connessione puoi consultare l'ultima copia scaricata. Per modificare dipendenti, orari o programmazione serve il database disponibile. Lo stato della connessione è visibile nella finestra. Da un intervento di lavoro puoi generare la scheda lavoro con data, squadra e note già compilate.

Nello Storico, in Ordini, nei Preventivi inviati aperti, nella Dashboard e nel Calendario trovi le stesse azioni sui documenti: **PDF** apre il preventivo; **Azioni** permette di generare la scheda lavoro, aprire la cartella cliente o consultare il dettaglio nello storico. Dal calendario e da un intervento salvato, la scheda lavoro usa squadra, data, fascia oraria e note della programmazione aggiornata. Negli altri elenchi puoi scegliere questi dati nella finestra della scheda lavoro. Il PDF del preventivo viene rigenerato dai dati salvati senza creare nuovi preventivi.

Sulle attività di lavoro già salvate trovi **Finito e costi**. Il pulsante segna come finito il singolo intervento, conservando identificativo, giorno, orari, squadra e note, e archivia il PDF **Costi e guadagno** direttamente in **CostiLavori** nella cartella principale dei PDF. Il nome contiene numero del preventivo, data e identificativo della visita: più visite dello stesso ordine producono documenti distinti, senza cartelle per cliente o riferimento. Se manca un calcolo dei costi salvato, si apre la relativa finestra per compilarlo; se viene chiusa senza salvare, l’intervento rimane programmato. Gli interventi finiti restano visibili anche quando l’ordine non è più attivo. **PDF costi** permette di rigenerare il documento. Per chiudere o rigenerare i costi serve una connessione al database; nell’editor salva prima eventuali modifiche a data, orari, squadra o note.
