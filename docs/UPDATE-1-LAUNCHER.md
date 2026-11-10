# Launcher 1.1.3 — dokončení kontroly

- Navazuje na 1.1.2, zachovává novinky Endora i podpisové ověřování modů.
- Před připojením vybere nejnovější oficiální klientský archiv. Server-only release nepovažuje za klienta.
- Kontroluje protokol zveřejněný na oficiálním webu proti serveru; rozdílná patch verze sama o sobě není chyba.
- Stahuje a ověřuje velikost i SHA256; instalace je oddělená a verzovaná. Rozbalení odmítá cesty mimo instalaci, duplicitní cesty a symlinky.
- Obnoví poškozený archiv a změněnou instalaci. Neúspěšný download nepřepíše předchozí instalaci.
- Oficiálnímu launcheru předává --connect, --name a --game s cestou použité instalace modů.
- Neaktualizuje svévolně herní server při příchodu nekompatibilního protokolu; zobrazí příčinu a čeká na jeho aktualizaci.

Ověření: kompletní testy prošly; dotnet publish prošel. Skutečný archiv klienta0.49.0 ověřen, kontrola skutečného serveru0.49.1/protokol122 prošla. Samostatná revize odhalila chybějící --game a obnovu poškozené ZIP cache; obě opravy kryté regresními testy. Spuštění hry v rámci této kontroly ještě neproběhlo.
