---
name: everlost-report-to-codex
description: Předej Codexu stručný report po každém vývojovém úkolu nebo průzkumu Everlostu, včetně neúspěšné či zablokované práce. Ulož ho do sdíleného projektu před závěrečnou odpovědí; nepoužívej pro běžnou konverzaci bez projektové práce.
---

# Report pro Codex

Report předej souborem v kořeni skutečného projektu Everlost URP, nikoli v kopii, sandboxovém exportu nebo adresáři Assets. Dodrž existující pravidla CLAUDE.md. Tento skill nezajišťuje doručení zprávy do chatu ani automatické spuštění Codexu.

## Kdy a kam
- Před závěrečnou odpovědí po vývojovém úkolu, opravě, diagnostice, review nebo změně konfigurace vytvoř `_reports/claude-handoff/<YYYYMMDD-HHMMSS>-<kratky-popis>-<unikatni-suffix>.md`. Čas uveď s časovou zónou; jméno musí být unikátní a existující report nepřepisuj.
- Soubor `_reports/claude-handoff/LATEST.md` má obsahovat pouze datum, jednovětý výsledek a relativní Markdown odkaz na nový report. Aktualizuj ho až po úspěšném uložení a přečtení reportu; při souběžné práci nezaměň novější ukazatel za starší.
- Po přerušení či zablokování úkolu zaznamenej rozpracovaný stav, pokud máš ještě možnost zapisovat. Netvrď, že havarovaný proces vždy report vytvoří.
- Pokud nelze zapisovat do skutečné sdílené složky, dej obsah reportu do odpovědi a výslovně oznam, že předání souborem neproběhlo. Nevytvářej dojem doručení do Codex chatu.

## Obsah (obvykle do 300 slov)

# <Úkol>
- Čas: <datum a čas se zónou>
- Stav: hotovo / částečně / blokováno; <konkrétní výsledek>

## Změny
- <soubor relativně ke kořeni>: <co a proč>; odděl vlastní změny od předchozí práce uživatele.

## Ověření
- <skutečně provedená kontrola a výsledek, případně cesta k relevantnímu logu>
- Neověřeno: <konkrétní mezera, nebo žádná známá v rozsahu úkolu>

## Předání Codexu
- <zbývající problém či další potřebný krok; při dokončení stačí „Žádná navazující práce.“>
- <jen relevantní dopad na savy, multiplayer, Inspector reference nebo výkon>

Neuváděj tokeny, hesla, celé logy, přepisy chatů ani soukromé údaje. Nevydávej statickou kontrolu za runtime test. Bez měření neslibuj výkonové ani kreditové úspory. Report nesmí být důvodem spouštět další testy nebo opakovat průzkum.

V závěrečné odpovědi uživateli přidej odkaz na uložený report. Používej formulaci „Report pro Codex uložen“, nikoli „Codex ho přijal/přečetl“, pokud to není skutečně potvrzené. Bez dalšího zadání neposílej nic přes Slack, e-mail ani jiné externí kanály.
