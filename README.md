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
| FLARM | `$PFLAU`, `$PFLAA` (ID `HEX!CN`), `$PFLAM` (Pilot, CN, Kennzeichen, Typ) |
| OpenVario | `$POV` (TE, unkompensiert, TAS, G-Last) für ein zweites XCSoar-Gerät |
| Raw | Condor-NMEA unverändert |

## Schnellstart

1. **com0com installieren** (signiertes Setup von <https://sourceforge.net/projects/com0com/files/com0com/>).
   Bei der Installation die Optionen für Beispiel-Ports abwählen.
2. `Condor2NMEA.exe` starten → Tab **Virtueller COM-Port** → *Anlegen (Administrator)*.
   Es entsteht z. B. `COM9` (für Condor) ↔ `CNCB0` (liest Condor2NMEA). Der Eingang wird automatisch eingestellt.
3. Tab **Einstellungen** → Condor-Ordner prüfen → **UDP.ini einrichten** (aktiviert UDP, 50 ms, ExtendedData/ExtendedData1; Backup `UDP.ini.bak`).
4. **Condor starten** (erst *nach* dem Anlegen des Ports!) → *Setup → Options → NMEA output* → `COM9`.
5. **XCSoar**: *Config → Devices → Device A*
   - Port **TCP client**, IP des Condor-PCs (wird oben im Programm angezeigt), Port **4353**,
   - Treiber **Condor Soaring Simulator 3**.

   Alternativ in Condor2NMEA einen Ausgang vom Typ *TcpClient* auf die IP des XCSoar-Geräts anlegen und in XCSoar
   Port **TCP port 4353** wählen (wie in der HW-VSP3-Anleitung des Condor-Handbuchs).
6. Optional zweites XCSoar-Gerät: Ausgang mit Profil *OpenVario* (z. B. Port 4354), XCSoar-Treiber *OpenVario*.

Im Tab **Status** sieht man Rate und Alter jeder Quelle, verbundene Clients und alle Live-Werte; im Tab **Verkehr**
die Multiplayer-Flugzeuge; der **NMEA-Monitor** zeigt ein- und ausgehende Sätze.

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
dotnet publish src/Condor2Nmea.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Die Desktop-App (WPF) baut nur unter Windows; Core, Runtime, CLI und Tests laufen auch unter Linux.
Für eine einzelne, eigenständige `Condor2NMEA.exe` den `publish`-Befehl oben unter Windows ausführen
(Ergebnis in `src/Condor2Nmea.App/bin/Release/net8.0-windows/win-x64/publish/`).

## Projektstruktur

```
src/Condor2Nmea.Core      Parser (NMEA, UDP, Spectate), Datenfusion, Verkehr, Satz-Generatoren, Einstellungen
src/Condor2Nmea.Runtime   Quellen (COM, UDP, Spectate), Ausgänge (TCP-Server/-Client, UDP), Engine, com0com, UDP.ini
src/Condor2Nmea.Cli       Kommandozeile
src/Condor2Nmea.App       WPF-Oberfläche
tests/                    xUnit-Tests mit (anonymisierten) echten Condor-3-Mitschnitten
docs/PLAN.md              Entwurf
```
