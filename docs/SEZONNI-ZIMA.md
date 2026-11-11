# Sezónní Harsh Winter

Launcher 1.1.5 před vstupem čte https://kronikykralovstvi.cz/season.php. Údaj pochází z herního kalendáře přes serverový klíč existujícího webového propojení. Zima je prosinec–únor; /zimatest přepíná jen dočasné efekty, nikoli zimní textury.

## Pro hráče

Při první zimě launcher vytvoří zasněžené textury z vlastní instalace hry. Příprava může trvat několik minut a potřebuje místo na disku. Nezavírej launcher během přípravy. Další zimy využijí uloženou kopii. Při změně souborů základní hry se textury sestaví znovu.

Při přechodu podzim → zima nebo zima → jaro úplně ukonči hru a spusť ji přes launcher Kronik. Samotné odpojení nestačí. Server upozorní deset herních minut před půlnocí a při změně v 00:01. Serverové počasí se změní ihned; načtené textury potřebují nový start hry.

Mimo zimu launcher přesune svůj zimní mod mimo Mods. Ostatní mody zachová. Neznámou ručně instalovanou složku harsh_winter_world nepřepíše; zobrazí chybu pro správce.

## Správce

Veřejný server musí běžet a publikovat čerstvý kalendář. Stav starší než 120 sekund se nepoužije. Při nedostupném webu nebo serveru launcher zimní profil neodhaduje a hru nespustí. Po opětovném spuštění serveru počkej na inicializaci kalendáře.

Nasazení: season.php vedle webového core.php; kk_season_publisher.lua a upravený rp_calendar.lua do gamemodes. Používá stávající data/kk-web-bridge.key, žádný klíč se hráčům neposílá.

Lokální soubory: Mods/harsh_winter_world = aktivní textura; .kroniky-winter/cache = neaktivní textura; build-* = sestavení; retired-* = předchozí zachované verze. Generátor zapisuje jen do nové build-* složky. Základní Data a Engine pouze čte.

Pro ověření: dotnet run --project tests/Mods.Tests.csproj. Skutečné vykreslení mapy je potřeba ověřit ve hře po přechodu zimního profilu, nestačí kontrola archivů.

## Ověření vydání 1.1.5

Dne 11. 11. 2026 prošly testy přepínání, obnovy cache, aktualizace generátoru, čisté instalace, ochrany běžící hry a cizích souborů. Serverové soubory byly po nahrání staženy a byteově porovnány. Živý endpoint vrátil skutečný podzim 15. října 1403.

Na instalačních souborech tohoto PC generátor za 786 sekund přestavěl 576 textur a 212 normálových map. Pět archivů má dohromady přibližně 3,67 GB a prošlo kontrolou CRC. 191 textur generátor 4.5.1 ponechal původních (nepodporovaný formát či pravidla původního generátoru). Aktivace a odložení skutečných archivů prošly; vykreslení zasněžené mapy v běžící hře zatím ověřené není.
