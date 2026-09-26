# Installazione per una nuova azienda

Questa versione gestisce una ditta per installazione. Ogni ditta deve avere un database dedicato (SQL Server o PostgreSQL); cambiare il nome aziendale non separa i dati già presenti nel database. Non collegare una nuova ditta al database EdilPaint.

## Creare il pacchetto

Da PowerShell nella cartella del progetto:

```powershell
./tools/Publish-Company.ps1 -OutputDirectory 'D:/Distribuzioni/NuovaAzienda-v1'
```

La destinazione deve essere nuova. Il pacchetto richiede .NET 10 Desktop Runtime per Windows. Lo script esclude impostazioni, credenziali, cataloghi, loghi e timbro EdilPaint, e genera `company-profile.id`. Conservare questo file insieme al pacchetto: identifica la ditta e deve rimanere uguale negli aggiornamenti e nelle postazioni della stessa azienda. Per un'altra ditta generare un nuovo pacchetto con un nuovo identificativo.

Per un aggiornamento usare lo stesso identificativo e una nuova cartella di pubblicazione:

```powershell
./tools/Publish-Company.ps1 -OutputDirectory 'D:/Distribuzioni/NuovaAzienda-v2' -CompanyId 'IDENTIFICATIVO-GUID-DEL-PRIMO-PACCHETTO'
```

L'aggiornatore EdilPaint è disabilitato nei pacchetti aziendali. Aggiornare distribuendo il pacchetto della propria ditta, mantenendo il suo `company-profile.id`. Un pacchetto aziendale senza questo file rifiuta l'avvio per evitare di usare l'archivio di un'altra installazione.

## Primo avvio

1. Configurare la connessione al database dedicato, già predisposto e raggiungibile. L'applicazione crea/aggiorna lo schema come nell'installazione esistente.
2. Inserire i dati aziendali. Solo la ragione sociale è obbligatoria. Per iniziare dal preventivo 1 lasciare a 0 l'ultimo numero utilizzato.
3. In **Impostazioni → Azienda** scegliere IVA predefinita e funzioni opzionali. Verificare inoltre cartella PDF, testi del documento, dipendenti, costi di lavoro ed eventuale invio email.
4. Inserire materiali e lavorazioni propri tramite le funzioni del catalogo. Il pacchetto parte senza cataloghi o storico EdilPaint. Velux, automatismi per finestre e certificato di posa sono inizialmente disattivati.

Non è prevista una modalità completamente senza database: il primo avvio e il salvataggio di dati aziendali/cataloghi richiedono la connessione. Se la connessione fallisce al primo avvio, correggerla nelle impostazioni proposte e riavviare.

## Dove sono conservati i dati

- Configurazione, dipendenti, cache, bozze, sessione Velux e immagini locali: `%LOCALAPPDATA%/PreventiviAzienda/<identificativo>/`.
- PDF predefiniti: `Documenti/PreventiviAzienda/<identificativo>/`; il percorso può essere cambiato nelle impostazioni.
- Clienti, cataloghi, storico, anagrafica azienda e numerazione: database configurato. La numerazione già utilizzata non viene ridotta.
- Logo e timbro vengono copiati nella cartella locale `Data/Branding`. Nelle altre postazioni copiare questa cartella dalla prima postazione e configurare il timbro; le password vanno reinserite perché protette per utente Windows.

Il logo scelto è usato nei preventivi, nelle schede lavoro e nei certificati. Il timbro/firma aziendale è facoltativo; la firma per accettazione del cliente resta uno spazio separato. I PDF già prodotti non vengono riscritti: i nuovi documenti e quelli rigenerati usano i dati aziendali correnti.

Le installazioni esistenti senza `company-profile.id` conservano percorsi, importazioni e funzioni precedenti. Questa procedura non cancella né trasferisce i dati EdilPaint.
