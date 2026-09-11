# EdilPaint Android 3.0

App Android nativa collegata direttamente a Neon. Non richiede hosting o un server applicativo.

## Funzioni aggiunte

- Dashboard, ricerca preventivi/clienti, filtri data/stato/inviati aperti, ordinamento.
- Creazione, modifica, duplicazione ed eliminazione logica preventivi; clienti modificabili ed eliminabili.
- Riordino materiali/lavorazioni, NOTE pubbliche separate dalle note interne.
- Cataloghi materiali e lavorazioni modificabili, flag materiali aziendali.
- Ordini con colori per stato e ordinamento predefinito per data ordine decrescente.
- Materiali ordinati dal cliente senza trasformarlo in un fornitore anagrafico.
- Email ordine con `N.quantita prodotto` e copia automatica all'azienda.
- Invio SMTP TLS o composizione nell'app email; registrazione invii e solleciti.
- Guadagno reale con il calcolatore del desktop, ricalcolo salvato e report riservato.
- Regole materiali automatici con il calcolatore condiviso del desktop.
- Collaborazioni e costi delle due aziende, PDF riservato.
- PDF preventivo, condivisione, allegati PDF/immagini su Neon, certificato di posa.
- Cronologia operazioni e controllo revisione per evitare sovrascritture concorrenti.

## Configurazione

1. Installare l'APK firmato. Per aggiornare un'installazione esistente serve la stessa chiave di firma; non disinstallare l'app per aggirare un errore di firma senza considerare la perdita delle credenziali locali.
2. Un amministratore deve verificare ed eseguire `tools/security/Neon-MobileWriter.sql` nel database/branch corretto. Lo script NON viene eseguito dall'app e NON e' stato eseguito durante lo sviluppo.
3. Usare il ruolo operativo `edilpaint_mobile`, non l'account proprietario Neon. Conservare la connection string tramite l'accesso iniziale.
4. In Altro / Configura email impostare SMTP, mittente, modelli e valori iniziali del guadagno. La copia ordini utilizza il mittente configurato, oppure l'email aziendale letta dal database.

Le credenziali e le impostazioni sono cifrate tramite SecureStorage/Keystore; il backup Android e' disabilitato. Non esiste un database locale di preventivi. Le letture hanno una scadenza complessiva di 20 secondi: in caso di rete assente l'interfaccia resta aperta e conserva le credenziali, ma non puo' salvare offline. I documenti condivisi richiedono file temporanei nella cache privata, rimossi dopo 24 ore alla successiva apertura; destinatari e app di condivisione possono conservarne una copia.

I report di guadagno/collaborazione sono riservati e non vengono allegati automaticamente alle email cliente. L'apertura nell'app email non dimostra l'invio: la registrazione manuale chiede conferma. Un errore SMTP dopo l'inizio dell'invio richiede controllo della posta prima di riprovare, per evitare duplicati.

## Limiti attuali e verifiche rimanenti

- Non ancora equivalenti al PC: integrazione/login Velux, importazioni specifiche del desktop, gestione completa impostazioni aziendali/loghi/template, aggiornamento automatico APK e bozze offline.
- PDF Android con impaginazione propria; non riproduce tutti i template desktop e non incorpora automaticamente le immagini allegate.
- Le impostazioni SMTP e le regole personalizzate non sono sincronizzate con appsettings del PC: vanno configurate sul telefono.
- Il guadagno automatico propone i materiali dopo il comando esplicito; occorre confermarli e premere Ricalcola e salva.
- Da verificare prima della distribuzione a tutti: CRUD sul branch Neon di test, permessi effettivi del ruolo, conflitti PC/telefono, invio SMTP reale, allegati e riapertura del guadagno salvato.
- Firma attuale tramite la chiave di sviluppo Android locale; mantenere la stessa chiave per gli aggiornamenti. Non e' una pubblicazione Play Store.

## Build e test

```powershell
dotnet test EdilPaintPreventibiviGen.Android.Tests/EdilPaintPreventibiviGen.Android.Tests.csproj
dotnet test EdilPaintPreventibiviGen.Tests/EdilPaintPreventibiviGen.Tests.csproj
dotnet publish EdilPaintPreventibiviGen.Android/EdilPaintPreventibiviGen.Android.csproj -f net10.0-android -c Release -p:AndroidPackageFormats=apk
```

La variante `-p:EnableMobileSmokeTests=true` ha un application ID separato (`it.edilpaint.preventivi.smoketest`), dati fittizi e nessuna credenziale reale. Non distribuirla agli utenti. `tools/android/Smoke-Test.ps1` verifica schermate a 2076x2152 e 1080x2400, genera un PDF multipagina e conserva le prove in `EdilPaintPreventibiviGen.Android/bin/mobile-qa`.

Verifiche eseguite il 2026-09-11: 162 test desktop, 19 test mobile (compresi limite complessivo di 20 secondi, cancellazione, tentativi limitati e verifica TLS Neon), 12 aperture schermate per ciascuna variante Debug/Release su emulatore, PDF A4 di 5 pagine generato e renderizzato. Nessuna modifica al database reale e nessuna email reale inviata durante questi test.
