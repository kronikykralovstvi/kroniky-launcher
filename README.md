# Kroniky Království – launcher

Windows launcher pro KCD:MP server Kroniky Království. Verze **1.1.0** přidává
středověký vzhled, živé novinky z webu a podepsané aktualizace launcheru i modů. Projekt byl obnoven z vlastního
vydání 1.0.6, protože původní repozitář obsahoval jen aktualizační manifest.

## Pro hráče

1. Spusť `KronikyKralovstviLauncher.exe` z nejnovějšího vydání.
2. Úplně ukonči Kingdom Come: Deliverance II. Launcher se pokusí najít hru;
   případně klikni na **Nastavení hry** a vyber složku s `Bin` a `Data`.
3. Počkej na zprávu **Mody … jsou připravené**, zadej jméno a klikni na **VSTOUPIT DO HRY**.

Kontrola probíhá při otevření, ruční aktualizaci i před připojením. Stahují se
jen chybějící nebo změněné soubory. Při nedostupném manifestu nebo poškozeném
stažení launcher připojení nespustí; lze zkusit aktualizaci znovu. Hráč nepotřebuje
GitHub účet ani .NET SDK. KCD:MP klient a zakoupená hra se instalují samostatně.

## Jak vydat nové mody

### Přes web GitHubu

1. Připrav ZIP obsahující pouze kořenovou složku `Mods` a uvnitř všechny aktuální
   klientské mody serveru. Pojmenuj ho například `Mods.zip`.
2. V **Releases > Draft a new release** založ vydání podkladů s novým tagem,
   například `podklady-2026-11-04`, a nahraj `Mods.zip`. Může zůstat jako draft.
3. V **Actions > Vydat mody > Run workflow** vyplň tento tag, název ZIPu a novou
   verzi modů, například `2026.11.04.1`.
4. Počkej na zelené dokončení a stáhni z vydání `release-request.json`.
5. Na důvěryhodném administrátorském PC požadavek zkontroluj a podepiš pomocí
   `Approve-Release.ps1`. Teprve podpis aktivuje nové mody. Viz [SECURITY.md](SECURITY.md).

### Z administrátorského PC

Na administrátorském PC je potřeba Python **3.12+** a [GitHub CLI](https://cli.github.com/).
Přihlas se jednou příkazem `gh auth login`. Přihlášení je jen pro správce,
nepatří do klienta ani do manifestu.

Připrav složku `Mods`, která obsahuje **celý aktuální požadovaný klientský balíček**,
nikoli jen nové soubory. Uvnitř budou jednotlivé mody, například
`Mods/kk_free/mod.manifest` a `Mods/kk_free/Data/…pak`.
Nepřidávej serverové skripty, `items.json`, přihlašovací údaje ani neaktivní archivy.
Distribuuj pouze soubory, pro které máš potřebná oprávnění.

Z kořene tohoto repozitáře spusť (verze musí být pokaždé nová):

```powershell
python tools/publish_mods.py --mods "C:\Balicek\Mods" --version 2026.11.03.1 --output "C:\Balicek\vydani-2026.11.03.1" --publish
```

Skript spočítá kontrolní součty, připraví soubory, vytvoří draft GitHub Release,
nahraje klientské soubory, ověří jejich velikost i SHA-256 a vytvoří
`release-request.json` pro následné lokální schválení a podepsání. Bez `--publish` pouze připraví lokální podklady.
Výstupní složka musí být prázdná nebo nová. Při chybě před aktivací zůstane
starý manifest aktivní; použij nový identifikátor vydání a zkus opravený balíček.

Repozitář a výsledná modová vydání musí být veřejné. Launcher 1.1.0 načítá
podepsaný manifest přímo z `raw.githubusercontent.com` z větve `main`; nemusí čekat na Pages.
GitHub Pages ponech funkční na `https://kronikykralovstvi.github.io/kroniky-launcher/`
kvůli automatickému přechodu starých launcherů 1.0.6. Běžná další verze modů
**nevyžaduje nové EXE**.

Po aktualizaci modů také samostatně udržuj odpovídající serverový katalog a
serverové skripty. Launcher distribuuje klientské soubory, nepřepisuje server.

## Zálohy a odstraněné mody

Launcher spravuje jen cesty uvedené v modovém manifestu. Nepřítomný soubor odstraní
jen tehdy, pokud byl dříve spravovaný launcherem a stále odpovídá uloženému hashi.
Ručně změněný starý soubor automaticky nesmaže: oznámí jeho cestu.
Ostatní mody se zachovají; záměrné překryvy s nimi může být potřeba řešit ručně.

Před výměnou se vše stáhne a ověří. Zálohy a stav jsou ve složce
`<hra>/.kroniky-mods`. Po chybě zápisu nebo přerušení instalace se před další
aktualizací obnoví předchozí soubory. Zálohy se automaticky nemažou; ponech
dostatek místa pro stažení a předchozí verzi. Běžící hra blokuje aktualizaci.

Nouzové pozastavení, návrat k předchozím modům a zálohu podpisového klíče
popisuje [SECURITY.md](SECURITY.md). Podepsané aktualizace neumožňují skrytý
přístup do počítačů hráčů.

## Vývoj a vydání launcheru

```powershell
dotnet run --project tests/Mods.Tests.csproj
python -m unittest discover -s tests -p "test_*.py"
dotnet publish src/Launcher/Launcher.csproj -c Release -r win-x64 --self-contained true -o artifacts/launcher
```

Testy pokrývají instalaci, rozdílové aktualizace, kontrolní součty, ochranu cizích
souborů, návrat po chybě, obnovu přerušené transakce, neplatné cesty a běžící hru.

Tag `v1.1.0` spustí sestavení draftu launcher Release. Po ověření zveřejni EXE,
jeho SHA-256 a až poté nastav `version`, `launcherUrl`, `launcherSha256` v
`manifest.json`. Zachovej funkční `updaterUrl` a `updaterSha256`. Staré pole
`files` nech prázdné: launcher 1.0.6 by je instaloval do profilu Windows.
Launcher 1.1.0 používá výhradně podepsaný `trusted-release.json`.
`mods-manifest.json` zůstává historickým podkladem pro starší klienty.
Před aktivací launcheru podepiš aktualizovaný požadavek včetně jeho EXE a hashů.

Nejprve musí být dostupný modový manifest a jeho assets, teprve potom aktivuj
nové EXE pro hráče. Původní launcher 1.0.6 se aktualizuje svým dosavadním updaterem.
Jiné starší varianty bez funkční automatické aktualizace vyžadují nové EXE ručně.

## Vzhled, odkazy a novinky

Vzhled vychází z grafického návrhu provozovatele. Tlačítka jsou skutečné ovládací
prvky, podporují klávesnici a přizpůsobují se velikosti okna.
- Oficiální web: https://rpkronikykralovstvi.cz/
- Discord: https://discord.gg/uYYEDDFPcF
- Novinky se načítají ze stejného veřejného `/api/portal/news` jako web.
- Přehled změn otevírá `/novinky/`; stav instalace ukazuje skutečnou verzi modů.

Webové články se zobrazují jako text a obrázky. Nespouštějí skripty, neovládají
instalátor a nemohou měnit důvěryhodné aktualizační zdroje.
