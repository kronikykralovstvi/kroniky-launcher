# Zabezpečení a nouzová obnova

Launcher 1.1.0 ověřuje `trusted-release.json` podpisem ECDSA P-256 / SHA-256.
Podepisují se přesné bajty JSON payloadu; po ověření se kontrolují verze,
pořadové číslo a zdroje souborů. Veřejný klíč je pevně zabudovaný v EXE.
Starší nepodepsané manifesty nepovolují novému launcheru instalaci.

Soukromý klíč je pouze na počítači správce:
`%LOCALAPPDATA%/KronikyKralovstvi/Signing/release-key.dpapi`.
Je chráněný Windows DPAPI pro aktuální účet. Není v repozitáři, klientovi,
balíčku pro hráče ani v GitHub Actions secrets. Nástroje podepisování spouštěj
z uložené důvěryhodné lokální kopie; automaticky je nenahrazuj kódem z GitHubu.

## Schválení nových modů

Workflow **Vydat mody** nahraje a ověří soubory a připraví `release-request.json`.
Samo nasazení do launcheru 1.1.0 neaktivuje. Stáhni požadavek, zkontroluj zdrojové
soubory, verzi a SHA-256 launcheru a na svém PC spusť PowerShell 7:

```powershell
pwsh -File tools/Approve-Release.ps1 -CandidateFile "C:\Balicek\release-request.json"
```

Potvrzení `VYDAT` znamená povolení instalace daných souborů hráčům.
Nástroj použije nové pořadové číslo, podepíše vydání a zveřejní obálku.
Pokud v požadavku nesouhlasí verze launcheru, nic nepodepisuj a nejdřív
vyber správný ověřený launcher. Nikdy nepodepisuj neprověřený požadavek jen proto,
že je uložený v repozitáři. Pro schválení je potřeba přihlášení `gh auth login`.

## Nouzové pozastavení a pokračování

```powershell
pwsh -File tools/Approve-Release.ps1 -Mode Pause -Message "Probíhá kontrola aktualizací. Sleduj Discord."
pwsh -File tools/Approve-Release.ps1 -Mode Resume
```

Podepsaná pauza zastaví další instalace i spuštění hry přes launcher při příští
kontrole. Nevypne už běžící hru a není vzdáleným ovládáním hráčova PC.
Šíření změny může kvůli cache GitHubu chvíli trvat. Pokud je GitHub účet
napadený, nejdřív obnov jeho kontrolu, odvolej cizí relace/tokeny a oprav oprávnění.
Útočník ovládající repozitář může distribuci blokovat; samotný klíč z něj nezíská.

## Návrat k dřívějším modům

Použij lokálně uložený ověřený požadavek staršího balíčku. Zachovej aktuální
ověřený launcher a vlož dřívější část `mods`. Podepiš jako nové vydání pomocí
`Approve-Release.ps1`; jeho pořadové číslo musí růst. Pouhé vrácení staré podepsané
obálky mohou klienti správně odmítnout jako pokus o návrat ke staršímu stavu.
Před publikací ověř kompatibilitu se serverovým katalogem.

Přerušené instalace obnovuje launcher automaticky ze `.kroniky-mods` v adresáři hry.
Před instalací kontroluje běžící hru, cílové cesty, kontrolní součty i velikosti.
Cizí mody nemaže. Soubor změněný ručně mimo launcher automaticky neodstraní.

## Záloha podpisového klíče

DPAPI soubor sám nestačí pro přenos na jiné PC nebo nový Windows účet.
Vytvoř heslem chráněnou offline zálohu a ulož ji na oddělené bezpečné médium:

```powershell
pwsh -File tools/Sign-Release.ps1 -RecoveryBackup "E:\Zaloha\kroniky-recovery-key.pem"
```

Heslo zadáš skrytě přímo do konzole, nikdy do chatu, GitHubu nebo argumentů.
Záloha se při tomto nasazení automaticky nevytváří, protože vyžaduje tvoje heslo
a zvolené offline médium. Ztráta klíče bez obnovitelné zálohy znamená ruční
distribuci nového důvěryhodného launcheru. Při úniku klíče je nutná výměna důvěry.

## Hranice ochrany

Podpis chrání aktualizace již nainstalovaného důvěryhodného launcheru proti
podvržení bez podpisového klíče. Nechrání první stažení EXE z napadeného účtu,
napadený Windows účet správce, obsah vědomě podepsaný správcem ani samotný herní
server. Staré launchery 1.0.8 a nižší podpisy nevynucují; hráči musí přejít na 1.1.0.
Pořadové číslo brání návratu pod nejvyšší již viděné číslo, nikoli zadržování
nejnovější zprávy útočníkem. Žádná verze se neoznačuje jako „nehacknutelná“.

Launcher neobsahuje zadní vrátka, vzdálený terminál ani skryté administrátorské heslo.
