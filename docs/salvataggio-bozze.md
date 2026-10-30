# Salvataggio delle bozze e conferma dei preventivi

In **Impostazioni → Generali → Comportamento** è disponibile **Salva le bozze solo con Genera PDF o alla chiusura**.

L'opzione è inizialmente disattivata, per conservare il salvataggio periodico delle bozze nelle installazioni esistenti. Quando è attiva, il timer e i salvataggi richiamati durante l'inserimento delle righe non scrivono né la bozza locale né il preventivo nel database. La preferenza viene applicata appena si salvano le impostazioni.

- **Genera PDF** conserva una bozza di recupero prima di preparare il documento e conferma il contenuto nel database con il normale salvataggio del PDF. Dopo il successo la bozza locale viene eliminata.
- **Chiusura del programma**, compresa quella preparata dall'aggiornatore, conserva la bozza locale e tenta la condivisione se il documento nel database è ancora una bozza. In assenza di connessione rimane la copia locale.

La protezione dei preventivi già finalizzati è sempre attiva, indipendentemente da questa preferenza. Tutti gli stati diversi da **Bozza** sono protetti: salvataggio periodico e chiusura non sostituiscono cliente, riferimento, cantiere, righe, importi, note per il cliente o termini di pagamento del preventivo confermato. Le modifiche restano nell'editor e, quando previsto, in una bozza di recupero locale; per confermarle occorre **Genera PDF**. La bozza locale conserva lo stato del documento di origine anche dopo un riavvio.

Aprire e chiudere un preventivo finalizzato senza modifiche non crea una nuova bozza di recupero. Le voci dei costi caricate nell'editor sono copie indipendenti del documento aperto.

Prima di condividere una bozza viene controllato anche lo stato corrente nel database. Se un'altra postazione l'ha finalizzata, il salvataggio automatico non procede. Il controllo della revisione impedisce anche la sovrascrittura se la conferma avviene tra la lettura e il salvataggio; l'eventuale conflitto conserva la copia locale e richiede di riaprire il documento. Il salvataggio automatico non ritenta come modifica autorevole.

I comandi espliciti per stato, invio, materiali, note operative e costi mantengono il proprio funzionamento. L'interfaccia dello storico non cambia.
