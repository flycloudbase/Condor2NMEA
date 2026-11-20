# Condor2NMEA

Windows-Programm, das **alle Datenquellen von Condor 3** zusammenführt und als sauberes NMEA per TCP/UDP an
XCSoar, LK8000, SeeYou Navigator & Co. weitergibt – inklusive **eigenem virtuellem COM-Port** (über com0com),
sodass HW VSP3 o. ä. nicht mehr nötig ist.

| Eingang | Woher | Was |
|---|---|---|
| NMEA | Condor → virtueller COM-Port | `$GPRMC`, `$GPGGA`, `$LXWP0` (Position, Baro-Höhe, TAS, TE-Vario, Wind) |
| UDP | `UDP.ini` (Port 55278) | Netto, pneumatisches/E-Vario, Lage, Drehraten, G-Last, MC, Wasser, Klappen, Funkfrequenz, HUD-Meldungen … |
| Spectate | `Condor3\Logs\Spectate.json` oder `http://localhost:8080/allPilots` | alle Multiplayer-Flugzeuge → FLARM-Verkehr |

| Ausgabe | Sätze |
|---|---|
| GPS | `$GPRMC` (mit Datum), `$GPGGA` (Geoid-Separation 0.0 → keine 40-m-Höhenfehler in XCSoar) |
| LX | `$LXWP0` wie Condor (TAS in Feld 1), optional `$LXWP2` (MacCready) |
| FLARM | `$PFLAA` und `$PFLAU` nach FLARM Data Port ICD (FTD-012 7.23) mit berechneten Kollisionswarnungen (Stufe 1–3, siehe [docs/FLARM-Warnungen.md](docs/FLARM-Warnungen.md)), `$PFLAM` (Pilot, Wettbewerbskennung, Kennzeichen, Muster) |
| OpenVario | `$POV` (TE, unkompensiert, TAS, G-Last) für ein zweites XCSoar-Gerät |
| Raw | Condor-NMEA unverändert |
| Cloudbase | eigene Sätze `$PCBC,DATA/INFO/EVENT` und die Condor-Aufgabe als `$PCBC,TASK/TP` mit Netto, IAS, Lage, G-Last, Höhe über Grund, MacCready, Ballast, Klappen, Funkfrequenz, Rennstand und Condor-Meldungen – siehe [docs/PCBC.md](docs/PCBC.md) |

## Schnellstart

`Condor2NMEA.exe` starten. Beim ersten Start öffnet sich die Seite **Einrichtung**, die in fünf Schritten durch alles führt
und erledigte Schritte automatisch abhakt:

1. **com0com installieren** – Download-Knopf öffnet die Seite des signierten Treibers. Bei der Installation die Häkchen für Beispiel-Ports entfernen.
2. **Virtuellen COM-Port anlegen** – ein Klick (Windows fragt einmal nach Administratorrechten). Es entsteht z. B. `COM9` für Condor ↔ `CNCB0` für Condor2NMEA.
3. **Condor-Ausgaben einschalten** – in Condor *Setup → Options → NMEA output* den angezeigten Port wählen; „UDP-Ausgabe einrichten“ schreibt die `UDP.ini` (mit Sicherung).
4. **Condor starten und fliegen** – Condor erst *nach* dem Anlegen des Ports starten.
5. **Flugcomputer verbinden** – in XCSoar *Konfiguration → Geräte → Gerät A*: Port **TCP-Client**, angezeigte IP-Adresse, Port **4353**,
   Treiber **Condor Soaring Simulator 3**.

Die **Übersicht** zeigt danach auf einen Blick, ob Daten aus Condor ankommen und ob der Flugcomputer verbunden ist, dazu Höhe, Fahrt,
Vario, Netto, Wind und MacCready. Unter **Verkehr** gibt es ein Radar der Multiplayer-Flugzeuge und eine Auswahl des eigenen Flugzeugs (automatisch oder manuell aus der Liste der Piloten), der **NMEA-Monitor** zeigt alle Sätze.
Weitere Geräte (z. B. ein zweites XCSoar-Gerät mit Profil *OpenVario* oder den umgekehrten Verbindungsweg *TCP-Client*) legst du unter
**Einstellungen → Ausgänge** an.

### Ohne COM-Port

Ist kein NMEA vorhanden, verwendet Condor2NMEA die eigene Position aus Spectate.json (1 Hz) und die übrigen Werte aus UDP.
Das eigene Flugzeug wird über `"selected":"True"` in Spectate.json erkannt oder über die eingestellte Wettbewerbskennung.

## Kommandozeile

```
condor2nmea run [--settings datei] [--replay aufnahme.c2nrec] [--speed 4] [--out]
condor2nmea com0com list | create [COM-Nummer] | remove <index>
condor2nmea udp-ini [--condor C:\Condor3] [--apply]
condor2nmea convert-logs --nmea nmea.txt --udp udplog.txt --spectate Spectate.json -o aufnahme.c2nrec
condor2nmea default-settings
```

Einstellungen liegen in `%APPDATA%\Condor2NMEA\settings.json`. Mit *Aufzeichnung* werden alle Eingangsdaten in
`%APPDATA%\Condor2NMEA\recordings\*.c2nrec` gespeichert und lassen sich mit `--replay` ohne Condor abspielen.

## Bauen

```
dotnet build Condor2Nmea.sln
dotnet test tests/Condor2Nmea.Tests
dotnet publish src/Condor2Nmea.App -c Release -r win-x86 --self-contained -p:PublishSingleFile=true
```

Condor2NMEA ist ein reines Windows-Programm (.NET 8, WPF mit [WPF-UI](https://github.com/lepoco/wpfui) im Fluent-Design),
gebaut als **32-Bit (x86)** wie Condor selbst: Nur so lässt sich Condors 32-Bit-`NaviCon.dll` laden, die die Wendepunkte
der Aufgabe umrechnet.
GitHub Actions baut bei jedem Push auf einen Arbeitsbranch (nicht beim Öffnen eines PRs, nicht beim Merge auf `main`, nicht bei reinen Doku-Änderungen) und stellt `Condor2NMEA.exe` und `condor2nmea.exe` als Artefakt bereit. Von Hand startbar unter Actions → build → Run workflow.

## Projektstruktur

```
src/Condor2Nmea.Core      Parser (NMEA, UDP, Spectate), Datenfusion, Verkehr, Satz-Generatoren, Einstellungen
src/Condor2Nmea.Runtime   Quellen (COM, UDP, Spectate), Ausgänge (TCP-Server/-Client, UDP), Engine, com0com, UDP.ini
src/Condor2Nmea.Cli       Kommandozeile
src/Condor2Nmea.App       WPF-Oberfläche
tests/                    xUnit-Tests mit (anonymisierten) echten Condor-3-Mitschnitten
docs/PLAN.md              Entwurf
```
