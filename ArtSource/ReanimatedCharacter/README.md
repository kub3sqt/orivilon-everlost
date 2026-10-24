# Nová sada animací poutníka

Model a oblečení vycházejí z `ArtSource/DressedCharacter/DressedWanderer.blend`. Starší model zůstává jako záloha. V nové verzi jsou staré akce odstraněny a nahrazeny pouze těmito pěti klipy:

| Klip | Pohyb | Délka |
| --- | --- | --- |
| Walk | Chůze, střídání opory a protilehlé pohyby paží | 1,2 s |
| Run | Běh, vyšší zdvih nohou, předklon a pokrčené paže | 0,8 s |
| Chop | Obouruční boční švih do kmene s přetočením trupu | 1,5 s |
| Mine | Nápřah před hlavou a úder krumpáčem dolů k zemi | 1,8 s |
| Swim | Vodorovné plavání, střídavé záběry a pohyb nohou | 1,6 s |

Všechny klipy jsou ve smyčce a bez dopředného pohybu kořenové kosti. Pohyb po světě bude řídit stávající herní ovladač. Animace jsou uložené v transformacích kostí, bez runtime závislosti na skriptu nebo Blender constraints.

- `AnimatedWanderer.blend`: editovatelný model, nové akce a NLA stopy.
- `AnimatedWanderer.fbx`: export pro import do Unity, oddělené NLA takes. Import v Unity zatím neověřen.
- `AnimatedWanderer.glb`: model s pěti samostatnými animacemi.
- `index.html`: lokální 3D náhled s volbou pohybu a pozastavením; browser používá lehčí geometrii a 16 interpolovaných póz na klip.

Sekera a krumpáč jsou součástí náhledu jako mesh objekty s vlastními grip kostmi. Neaktivní nástroj má v ostatních akcích nulové měřítko. Při integraci lze jejich vzhled nahradit skutečnými herními nástroji. Tělo zůstává geometricky zachováno; pod neprůhledným oblečením má nedestruktivní masku proti prosvítání při ohybech.

Ověřeno: vytvoření všech pěti klipů, export GLB a FBX, shoda počátečních a koncových transformací v GLB, klíčové pózy všech akcí v Blender renderu, export pohyblivého náhledu. Ověření v Unity a multiplayeru zatím neproběhlo; herní assety ani scény nebyly měněny.

## Oprava podle zpětné vazby

- Chůze a běh: obrácena dráha chodidel; při došlapu jde noha dozadu vůči postavě, ve vzduchu vpřed. Pohyb paží je proti pohybu stejnostranné nohy.
- Běh: lokty jsou mimo žebra a za trupem, zápěstí dál od těla; dlaně mají vlastní směr vpřed.
- Sekera: boční nápřah, zásah a dotažení přes druhou stranu, bez zdvihu nad hlavu.
- Krumpáč: násada a hlava nástroje jdou před obličejem, nikoli skrz něj.
- Viditelnost kůže v rukávech zachována podle vah paží, aby při ohnutí nevznikaly prázdné otvory.

`check_motion_regressions.py` ověřil směr opěrné fáze chodidel, odstup loktů a rukou od trupu během běhu, boční rozsah sekery a kolize nástrojů s hlavou a kloboukem ve vzorcích po půl snímku (109 póz krumpáče, 91 póz sekery). Hlava byla pro kontrolu kolizí zjednodušena; výsledek bez průniků je v `motion_regressions.json`. Blender pózy i aktualizovaný pohyblivý náhled byly zkontrolovány. Unity a multiplayer nadále neověřeny.
