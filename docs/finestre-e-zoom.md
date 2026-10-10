# Dimensioni delle finestre e zoom

Le finestre ridimensionabili ricordano larghezza e altezza alla chiusura e le ripristinano alla riapertura, anche dopo aver riavviato il programma. Ogni tipo di finestra conserva la propria misura: per esempio, Storico, Ordini, Calendario e Impostazioni possono avere dimensioni diverse.

Le misure tengono conto dello zoom condiviso con la finestra principale. Cambiare lo zoom continua a ridimensionare il contenuto delle finestre secondarie, senza moltiplicare la misura a ogni riapertura. La finestra principale conserva invece la propria dimensione esterna e ricorda anche se era massimizzata; la barra superiore mantiene il comportamento precedente.

Se lo schermo è più piccolo, la finestra viene adattata all'area disponibile senza perdere la misura preferita. Le finestre con dimensioni automatiche o non ridimensionabili mantengono il proprio comportamento. Una chiusura annullata non salva la misura.

Queste preferenze sono locali al PC e all'utente Windows, nel file `dimensioni-finestre.json` della cartella del profilo in `%LOCALAPPDATA%` (`EdilPaintPreventivi` per l'installazione EdilPaint). Non richiedono modifiche al database.
