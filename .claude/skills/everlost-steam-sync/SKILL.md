---
name: everlost-steam-sync
description: "Oprav a rozšiř existující Steam a NGO multiplayer Everlostu: připojení, synchronizaci světa, hráče a late join. Pro zavádění Unity Services použij samostatný skill."
---

# Steam a synchronizace Everlostu

Vstupy jsou `Assets/Scripts/Multiplayer/SteamLobbyManager.cs`, `MultiplayerManager.cs`, `NetworkWorldSync.cs`, `NetworkPlayerBridge.cs`, `RemotePlayerController.cs`, `NetworkData.cs`. Rozsáhlý `MULTIPLAYER_NAVOD.md` čti jen pro příslušnou část setupu.

`NetworkWorldSync` používá NGO `CustomMessagingManager` a named messages na běžné komponentě; nepředpokládej RPC/NetworkBehaviour architekturu. Nejprve ověř skutečný formát zprávy a lifecycle registrace.

1. Odděl discovery/lobby, transport, načtení scény, plný stav a následné změny. Reprodukuj konkrétní selhávající fázi s rolí host/klient a pořadím událostí.
2. Pro každý změněný typ zprávy projdi sender, receiver, pořadí polí, typy, doručení a relay. Ověř identitu odesílatele a autoritu před změnou světa; síťové ID není automaticky ID lokálního hráče.
3. Zachovej registraci handlerů až po dostupnosti sítě, odregistrování při ukončení a aplikaci pending stavu až po připravení scény/světa. Opakované připojení nesmí násobit subscriptions.
4. Zkontroluj shodu seedu a stabilních object IDs; snapshot a živé změny nesmějí obnovit zničený objekt ani aplikovat těžbu/stavbu dvakrát. U klientských světů ověř oddělení od hostova savu.
5. Ověř jen dotčené scénáře: host + klient, late join po změně světa, disconnect/rejoin a offline režim. Bez dvou instancí označ síťové ověření jako neprovedené.

Nepřeváděj Steam lobby na Unity Services ani celý protokol na RPC jako vedlejší efekt opravy. Výstup podlož konkrétním tokem zprávy a výsledkem reprodukce.
