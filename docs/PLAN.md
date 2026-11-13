# Condor2NMEA – Entwurf und Umsetzungsplan

Desktopprogramm (Windows) für folgende Aufgaben:

1. Es sammelt **alle Datenquellen von Condor 3** an einer Stelle: die serielle NMEA-Ausgabe, die generische UDP-Ausgabe und die Multiplayer-Daten aus Spectate.json.
2. Es führt die Daten in einem gemeinsamen Flugzustand zusammen.
3. Daraus erzeugt es **sauberes NMEA**: GPS, LX-Vario, FLARM-Verkehr und optional OpenVario.
4. Es schickt das NMEA **per TCP** an XCSoar, LK8000, SeeYou Navigator, LXNAV-Apps usw.
5. Es legt **selbst einen virtuellen COM-Port** an, sodass Condor direkt hineinschreiben kann. HW VSP3 und andere Fremdtools sind dann nicht mehr nötig.

---

## 1. Was Condor 3 liefert (Quellen und Fakten)

| Quelle | Transport | Inhalt | Rate | Quelle der Info |
|---|---|---|---|---|
| **NMEA-Ausgabe** | COM-Port (Setup → Options → *NMEA output*) | `$GPGGA`, `$GPRMC`, `$LXWP0` | ca. 1 Hz | Handbuch S. 23 und 81, XCSoar `Condor.cpp` |
| **Generic UDP** | UDP, `UDP.ini` (`Host`, `Port=55278`, `SendIntervalMs`) | ASCII `key=value`, Floats mit `.`: time, altitude, vario, evario, nettovario, integrator, compass, slipball, turnrate, yawstringangle, yaw/pitch/bank, quaternion, vx/vy/vz, roll/pitch/yawrate, gforce. Mit `ExtendedData=1` zusätzlich height, wheelheight, turbulence, surfaceroughness, hudmessages. Mit `ExtendedData1=1` zusätzlich flaps, MC, water | einstellbar (bis 1 ms) | Handbuch Kap. 14.1 |
| **Spectate.json (Datei)** | Datei, Standard `C:\Condor3\Logs\Spectate.json` | Array aller Piloten: ID, CN, RN, Name, plane, latitude, longitude, altitude, speed, heading, vario, Status, Rang … | ca. 1 Hz | XCSoar `Condor3Spectate.cpp`/`SpectateFilePort.cpp` |
| **Spectate HTTP** | `http://localhost:8080/allPilots` bzw. `/selectedPilot` (Port über `Settings\Spectate.ini`) | wie oben | Polling | Handbuch S. 70–72 (nur im *Spectate!*-Modus) |

### Wichtige Eigenheiten (größtenteils bei XCSoar abgeschaut, dort schon schmerzhaft gelernt)

- **`$LXWP0` Feld 1 ist TAS, nicht IAS.** Das Feld bleibt so, wie Condor es liefert, und wird im Datenmodell als TAS geführt.
- **Windrichtung in `$LXWP0`:** Condor 1/2 gibt die Richtung an, *wohin* der Wind weht, Condor 3 die Richtung, *woher* er kommt. XCSoar hat deshalb zwei Treiber (`Condor` mit Kehrwert, `Condor3` ohne). Das Programm braucht dafür eine Option. Standard ist Condor 3.
- **`$GPGGA` ohne Geoid-Separation.** XCSoar zieht dann EGM96 ab, die Höhe liegt dadurch etwa 40 m zu tief (Commit „Fix altitude offset from GPS geoid“). Beim Erzeugen von GGA wird deshalb die Geoid-Separation korrekt gesetzt, wahlweise auf 0 mit MSL-Höhe. Ein Abgleich gegen die Baro-Höhe ist nötig.
- **UDP `bank` hat das umgekehrte Vorzeichen** zur Luftfahrtkonvention.
- **Diese UDP-Werte nicht für die Navigation verwenden**, so die Erfahrung von XCSoar (Commit „Ignore cockpit nav keys that duplicate NMEA“):
  - `altitude` ist der Höhenmesser mit QNH/QFE.
  - `evario` wird im STF-Modus zur Sollfahrt-Anzeige.
  - `compass`/`yaw` sind keine Spur über Grund.
  - `vx/vy` passen nicht zur Spur.
- **Spectate-Höhe** ist die Höhenmesserhöhe (MSL). Die relative Höhe für FLARM wird deshalb immer aus Spectate selbst (eigenes Flugzeug gegen fremdes) gebildet und nicht mit GPS-Höhe gemischt.
- **Koordinatenformat in Spectate:** Das Handbuch zeigt `45.53.345N`, XCSoar parst dagegen Hemisphäre plus Dezimalgrad. Der Parser muss beide Formate tolerieren. **Geprüft mit einer echten Datei:** Condor 3 schreibt `"latitude":"N47.1309"` (Hemisphäre vorne, Dezimalgrad, 4 Nachkommastellen) und zusätzlich `"selected":"True"` für das eigene bzw. ausgewählte Flugzeug.
- **Spectate.json** wird ständig neu geschrieben. Deshalb mit Datei-Sharing öffnen, ein UTF-8-BOM entfernen und bei halb geschriebener Datei (Parse-Fehler) den letzten gültigen Stand behalten.
- **COM-Port:** Condor listet die Ports beim Start. Der virtuelle Port muss deshalb **vor dem Start von Condor** existieren (Handbuch S. 81, Schritt 11).
- **`UDP.ini`-Beispiel im Handbuch:** Dort steht `Host=127.0.0.` (Tippfehler). Der Konfigurationshelfer schreibt `127.0.0.1`.

### Warum ein eigenes Programm, wenn XCSoar Condor3UDP und Spectate schon kann?

- Die XCSoar-Treiber für UDP und Spectate funktionieren nur, wenn XCSoar **auf demselben PC** läuft: Spectate wird als lokale Datei gelesen, und UDP kann pro Ziel nur einen Host bedienen. Auf einem **Kobo/Android-Gerät im WLAN** kommen Spectate-Verkehr und UDP-Daten heute gar nicht an.
- Andere Programme (LK8000, SeeYou Navigator, LXNAV-Apps, Top Hat …) haben diese Treiber nicht.
- Condor2NMEA macht daraus **einen einzigen Standard-NMEA-Strom über TCP**, den jedes Programm versteht. Dazu kommt der eingebaute COM-Port.

---

## 2. Architektur

```
             ┌──────────────────────────── Condor 3 ───────────────────────────┐
             │ NMEA → COMx           UDP → 127.0.0.1:55278     Logs\Spectate.json │
             └────┬──────────────────────┬─────────────────────────┬────────────┘
                  │                      │                         │ (oder HTTP :8080)
   ┌──────────────▼───────┐  ┌───────────▼──────────┐  ┌───────────▼───────────┐
   │ VirtualComProvider   │  │ UdpSource            │  │ SpectateSource        │
   │ (eigener COM-Port,   │  │ key=value Parser     │  │ File-Poll / HTTP-Poll │
   │  App liest Gegenende)│  │ Rate, Paketstatistik │  │ JSON → Pilotenliste   │
   │ → NmeaSerialSource   │  └───────────┬──────────┘  └───────────┬───────────┘
   │ Checksumme, Parser   │              │                         │
   └──────────┬───────────┘              │                         │
              └─────────────┬────────────┴────────────┬────────────┘
                            ▼                         ▼
                 ┌────────────────────┐    ┌──────────────────────┐
                 │ FlightStateFusion  │    │ TrafficModel         │
                 │ Feld = Wert+Zeit+  │◄───┤ eigenes Flugzeug     │
                 │ Quelle, Prioritäten│    │ (CN / Auto-Match),   │
                 │ Timeouts           │    │ Relativ-N/E/H        │
                 └─────────┬──────────┘    └──────────┬───────────┘
                           └────────────┬─────────────┘
                                        ▼
                         ┌─────────────────────────────┐
                         │ SentenceGenerators (Profile) │
                         │ GPS · LX · FLARM · POV · Raw │
                         └──────────────┬──────────────┘
                                        ▼
                         ┌─────────────────────────────┐
                         │ OutputHub                    │
                         │ TCP-Server · TCP-Client      │
                         │ (UDP · zweiter COM optional) │
                         └─────────────────────────────┘
                                        │
                           XCSoar / LK8000 / Navigator …
```

Prinzipien:

- **Jede Quelle ist optional.** Das Programm liefert immer das Beste aus dem, was gerade verfügbar ist.
- **Ereignisgesteuert mit festem Takt am Ausgang:** Ein Scheduler pro Ausgabe und Satztyp sorgt zum Beispiel für GPS mit 1 Hz, Vario mit 4–10 Hz und FLARM mit 1 Hz. So überfluten 1-ms-UDP-Pakete keinen Kobo.
- **Durchreichen oder neu erzeugen:** Pro Ausgabe wählbar ist entweder „Condor-NMEA roh durchreichen“ (maximal kompatibel zu HW VSP3) oder „aus dem Zustand neu erzeugen“ (angereichert und korrigiert).

---

## 3. Komponenten im Detail

### 3.1 Eingänge

**NmeaSerialSource**
- Liest das interne Ende des virtuellen COM-Port-Paars. Alternativ kann ein beliebiger vorhandener COM-Port gewählt werden, etwa für Nutzer mit eigenem Null-Modem.
- Zerlegt die Daten zeilenweise und prüft die Checksumme. Geparst werden `GPRMC`, `GPGGA` und `LXWP0`. Unbekannte Sätze werden gezählt und unverändert behalten, damit „Raw“ sie durchreichen kann.

**UdpSource**
- Bindet `UDP.ini:Port` (Standard 55278) auf 127.0.0.1.
- Parst je Datagramm alle `key=value`-Zeilen, unbekannte Keys schadlos ignorieren. So sind spätere Condor-Versionen mit weiteren Keys (XCSoar erwähnt etwa `radiofrequency`, `latitude`/`longitude`, `ax/ay/az`) automatisch mit abgedeckt.
- Optional **UDP-Weiterleitung** an weitere Ziele, etwa an Motion-Plattformen. Condor erlaubt nur einen Host, das Programm wird so zum UDP-Verteiler.

**SpectateSource**
- **Datei-Modus** (Standard, funktioniert auch beim eigenen Multiplayer-Flug): Die Datei wird per `FileSystemWatcher` und zusätzlich per Poll alle 1 s beobachtet. Lesen mit `FileShare.ReadWrite`, Größenlimit 1 MB.
- **HTTP-Modus** (wenn Condor im Spectate!-Modus läuft): `GET /allPilots` alle 1 s, Port aus `Settings\Spectate.ini` lesen.
- Die Werte werden robust geparst: Zahlen können als String vorliegen, `"—"` bedeutet „kein Wert“, für Koordinaten gibt es zwei Formate.

**CondorConfigHelper**
- Findet den Condor-Ordner (Registry/Standardpfade, sonst manuell).
- Liest und schreibt `UDP.ini` (`Enabled=1`, `ExtendedData=1`, `ExtendedData1=1`, `SendIntervalMs=50` als sinnvoller Standard) und `Spectate.ini`, jeweils **mit Backup**.
- Zeigt an, ob Condor.exe läuft, und warnt, falls der COM-Port erst nach dem Condor-Start angelegt wurde.

### 3.2 Datenmodell und Fusion

`FlightState` speichert jedes Feld als `(Wert, Zeitstempel, Quelle)` mit einer Gültigkeitsdauer, zum Beispiel 3 s.

| Größe | Primärquelle | Fallback |
|---|---|---|
| Position, Grundgeschwindigkeit, Spur, Uhrzeit | NMEA `GPRMC`/`GPGGA` | UDP `latitude/longitude` (falls vorhanden), sonst Spectate eigenes Flugzeug (1 Hz) |
| Baro-Höhe (MSL) | `LXWP0` | Spectate eigenes Flugzeug |
| TAS | `LXWP0` | – (IAS optional aus TAS und Höhe per Standardatmosphäre) |
| TE-Vario | `LXWP0` | UDP `vario` |
| Pneumatisches Vario, Netto, Integrator | UDP | – |
| Wind | `LXWP0` (Richtungskonvention konfigurierbar) | – |
| Lage (Pitch/Bank), Drehraten, G-Last | UDP (Bank negiert) | – |
| MC, Wasserballast, Klappen | UDP (`ExtendedData1`) | – |
| Verkehr | Spectate | – |

**Erkennung des eigenen Flugzeugs in Spectate**
- Primär über die konfigurierte Wettbewerbskennung (CN).
- Sonst automatisch: Der Pilot, dessen Position am nächsten an der NMEA-Position liegt (unter 50 m) und dessen Kurs passt, wird über mehrere Zyklen stabil zugeordnet.

**Relative Verkehrsposition**
- Nord/Ost werden aus der Live-Position berechnet (NMEA ist schneller als Spectate).
- Die relative Höhe wird nur innerhalb von Spectate gebildet (siehe Eigenheiten oben).
- **Extrapolation:** Fremde Positionen werden mit Kurs und Geschwindigkeit bis zum Ausgabezeitpunkt vorausgerechnet. Das glättet die 1-Hz-Sprünge.

### 3.3 Satz-Generatoren

| Generator | Sätze | Abnehmer |
|---|---|---|
| GPS | `$GPRMC`, `$GPGGA` (mit korrekter Geoid-Separation), optional `$GPGSA` | alle |
| LX/Condor | `$LXWP0` (TAS, Baro-Höhe, Vario, Kurs, Wind), optional `$LXWP2` (MC, Ballast, Bugs) | XCSoar-Treiber „Condor Soaring Simulator 3“, LK8000 „Condor“ |
| FLARM | `$PFLAU`, `$PFLAA` (ID = 24-Bit aus Condor-ID, Feld „ID!CN“, Geschwindigkeit in m/s), `$PFLAM` (Name, CN, Kennzeichen, Typ) | alle FLARM-fähigen Programme. XCSoar parst PFLAU/PFLAA **unabhängig vom gewählten Treiber**. |
| OpenVario | `$POV,E,…,e,…,S,…,L,…` (TE, unkompensiertes Vario, TAS, G-Last) | XCSoar-Treiber „OpenVario“ als zweites Gerät, damit Netto/Lage-Daten ankommen |
| Raw | unveränderte Condor-Zeilen | Kompatibilitätsmodus wie HW VSP3 |
| PGRMZ | Druckhöhe | ältere Programme |

**Profile** sind vordefinierte Kombinationen pro Ausgabe:
- *XCSoar (Condor 3)*: GPS + LXWP0 + FLARM
- *XCSoar zweites Gerät (OpenVario)*: POV
- *LK8000*
- *Nur FLARM-Verkehr*
- *Raw*

Jede Ausgabe bekommt ein eigenes Profil. Beispiel: Port 4353 liefert „XCSoar (Condor 3)“, Port 4354 liefert „OpenVario“.

### 3.4 Ausgänge (OutputHub)

- **TCP-Server** (Standard): Das Programm lauscht zum Beispiel auf 4353 oder 10110 und nimmt mehrere Clients gleichzeitig an. In XCSoar wählt man dafür den Port-Typ *TCP client* mit der IP des Condor-PCs.
- **TCP-Client**: Das Programm verbindet sich zu XCSoar, wenn dort *TCP-Port 4353* als Listener eingestellt ist. Das ist die heutige Anleitung aus dem Handbuch und braucht keine Umkonfiguration beim Nutzer. Bei Abbruch verbindet es sich automatisch neu.
- Optional: UDP-Broadcast/Unicast und ein zweiter virtueller COM-Port, für PC-Programme, die nur COM können.
- Pro Client gibt es Warteschlangen mit Begrenzung. Langsame Clients verlieren alte Sätze, statt alles zu blockieren.
- Eingehende Daten von Clients, etwa `$PFLAx`- oder MC-Befehle, werden zunächst nur protokolliert. Condor nimmt über COM keine Befehle an.

### 3.5 Eigener virtueller COM-Port (Kernanforderung)

Condor kann NMEA **nur** auf einen COM-Port schreiben. Unter Windows braucht man für einen COM-Port, der in der Geräteliste erscheint, einen **Treiber**. Eine reine Usermode-Lösung ist nicht möglich. Deshalb gibt es eine Abstraktion `IVirtualComProvider` (`CreatePair`, `RemovePair`, `ListPairs`, `Open`) mit drei Ausbaustufen.

**Stufe A – com0com mitliefern und steuern (MVP)**
- com0com ist ein Open-Source-Null-Modem-Paar (GPL), über `setupc.exe` per Skript steuerbar.
- Das Programm legt ein Paar an:
  - **Ende 1:** echter Name `COM<n>`. Er wird als freie Nummer oberhalb der vorhandenen Ports ermittelt (Registry `HKLM\HARDWARE\DEVICEMAP\SERIALCOMM`). Diesen Port wählt man in Condor.
  - **Ende 2:** interner Name wie `CNCB0`, ohne `COM`. Er taucht in Condors Liste nicht auf, und das Programm öffnet ihn selbst.
  - Befehl: `setupc install PortName=COM9,EmuBR=yes PortName=-`
- Das Anlegen braucht einmalig Adminrechte. Dafür gibt es einen kleinen Helfer `Condor2NMEA.PortSetup.exe` mit `requireAdministrator`-Manifest. Die Haupt-App läuft ohne Erhöhung, der Port bleibt dauerhaft bestehen.
- **Risiko:** Treibersignatur unter Windows 10/11 mit Secure Boot. Die verfügbaren signierten com0com-Builds müssen in **Phase 0** auf einer frischen Win11-Installation getestet werden. Fällt der Test negativ aus, wird Stufe B vorgezogen.

**Stufe B – eigener Treiber (Endausbau, „echt eigenständig“)**
- Ein UMDF-2-Treiber auf Basis des Microsoft-Beispiels *VirtualSerial2* (Windows-driver-samples, `serial/VirtualSerial2`).
- Er stellt einen COM-Port bereit und leitet geschriebene Daten über ein Device-Interface mit IOCTL an die App weiter. Ein zweites COM-Ende ist damit unnötig, und auch der Rückkanal ist möglich.
- Installation über ein MSI/WiX-Bundle mit `pnputil` und Gerätesetup (SetupAPI/SwDeviceCreate). Die COM-Nummer wird über `PortName` in den Geräteparametern gesetzt.
- Voraussetzungen:
  - EV-Code-Signing-Zertifikat und **Attestation Signing** über das Microsoft Partner Center. Das kostet laufend Geld und muss organisatorisch geklärt werden.
  - Testsignierung ist nur für die Entwicklung gedacht (`bcdedit /set testsigning on`).

**Stufe C – Betrieb ohne COM (Fallback, ganz ohne Treiber)**
- Läuft der COM-Port nicht, entstehen Position, Grundgeschwindigkeit und Spur aus
  - UDP `latitude/longitude`, falls die eingesetzte Condor-Version sie liefert (zu prüfen), sonst
  - Spectate eigenes Flugzeug (1 Hz, mit Extrapolation).
- Vario, Lage und MC kommen aus UDP.
- Damit liefert das Programm auch ohne jeden Treiber einen brauchbaren NMEA-Strom, nur mit geringerer GPS-Rate.

### 3.6 Bedienoberfläche

- **Statusseite:** je Quelle eine Ampel mit Rate (Hz), Alter des letzten Datensatzes und Fehlerzählern. Je Ausgabe die verbundenen Clients.
- **Live-Werte:** Position, Höhe, TAS, Vario/Netto, Wind, MC, Lage.
- **Verkehrsliste** (CN, Distanz, relative Höhe), optional als Mini-Radar.
- **NMEA-Monitor:** ein- und ausgehende Sätze mit Filter, Pause und Export.
- **Einrichtungsassistent:**
  1. Condor-Ordner finden
  2. COM-Port anlegen
  3. `UDP.ini` aktivieren
  4. Ausgabe/Profil wählen und die IP-Adresse des PCs anzeigen (inklusive QR-Code bzw. XCSoar-Anleitung)
- **Tray-Betrieb und Autostart:** Warnung, falls Condor vor der App gestartet wurde.
- **Aufzeichnung und Wiedergabe:** Alle Rohquellen werden mit Zeitstempeln protokolliert und lassen sich ohne Condor erneut abspielen (siehe Tests).

---

## 4. Technologie

| Bereich | Wahl | Begründung |
|---|---|---|
| Sprache/Runtime | **C# / .NET 8 (LTS)** | Windows-only wie Condor, `System.IO.Ports`, `System.Text.Json` und Sockets sind eingebaut, Single-File-Publish |
| UI | **WPF** (MVVM, CommunityToolkit.Mvvm) | ausgereift, Tray-Icon einfach. Alternativ Avalonia, falls später Linux/Wine gewünscht ist. |
| Struktur | Generic Host + DI, `Channel<T>` zwischen den Stufen | saubere Nebenläufigkeit, testbar |
| Logging | Serilog (Datei + UI-Senke) | |
| Treiber (Stufe B) | C, UMDF 2, WDK | MS-Beispiel als Basis |
| Installer | WiX v4 (MSI + Bundle für Treiber/com0com) | |
| Build | lokal per `dotnet build`/`dotnet test`/`dotnet publish` (keine GitHub Actions) | |

**Lizenz:** Werden Codeteile aus XCSoar übernommen, muss das Programm GPL-2.0-or-later sein. Die Formate sind aber gut dokumentiert (Handbuch, FLARM FTD-012, LX-Protokoll), eine Neuimplementierung ist also leicht möglich. Davor sollte die Lizenz entschieden werden. GPL wäre zu com0com ohnehin passend.

### Projektstruktur

```
src/
  Condor2Nmea.Core/          Datenmodell, Fusion, Einheiten, Geo-Mathe, NMEA-Builder/Parser, Checksumme
  Condor2Nmea.Sources/       NmeaSerialSource, UdpSource, SpectateSource (File/HTTP), CondorConfigHelper
  Condor2Nmea.Outputs/       Generatoren, Profile, TcpServerOutput, TcpClientOutput, UdpOutput
  Condor2Nmea.VirtualCom/    IVirtualComProvider, Com0comProvider, (später) DriverProvider
  Condor2Nmea.App/           WPF-UI, Tray, Assistent
  Condor2Nmea.PortSetup/     erhöhter Helfer für Portanlage
driver/                      (Stufe B) UMDF-Treiber + INF
tests/
  Condor2Nmea.Core.Tests/    Unit-Tests (Parser, Builder, Fusion, FLARM-Geometrie)
  Condor2Nmea.Replay.Tests/  Wiedergabe aufgezeichneter Sessions → Golden-NMEA
  samples/                   echte Condor-Mitschnitte (NMEA, UDP-LogToFile, Spectate.json)
docs/
```

---

## 5. Teststrategie

- **Unit-Tests:**
  - Checksumme und Formatierung jedes Satztyps (Feldanzahl, Rundung, Vorzeichen, Hemisphären)
  - Parser mit Randfällen: `"—"`, BOM, abgeschnittenes JSON, beide Koordinatenformate, unbekannte UDP-Keys
  - FLARM-Geometrie: relative N/E-Werte gegen eine Referenzrechnung, Track-Normalisierung, ID-Bildung
- **Golden-Tests mit echten Mitschnitten:**
  - UDP über `LogToFile=1` aus Condor
  - NMEA über die App-Aufzeichnung
  - Spectate.json-Schnappschüsse aus einem Multiplayer-Flug
  - Wiedergabe gegen eine erwartete NMEA-Ausgabe
- **Interop-Tests:**
  - XCSoar (Desktop/Linux-Build oder Android) per TCP: Position, Höhe (Geoid!), Wind (Richtung!), FLARM-Radar
  - LK8000 und SeeYou Navigator stichprobenartig
- **COM-Tests** auf frischer Win10- und Win11-VM mit Secure Boot: Portanlage, Condor sieht den Port, Neustart-Persistenz, Deinstallation.
- **Simulator-Quelle** (Kreisflug + KI-Verkehr) für UI-Entwicklung ohne Condor.

---

## 6. Fahrplan

| Phase | Inhalt | Ergebnis |
|---|---|---|
| **0 – Klärung (1 Woche)** | Echte Mitschnitte aller drei Quellen erstellen. Spectate-Koordinatenformat und UDP-Keys der aktuellen Condor-Version prüfen. com0com-Signatur unter Win11 mit Secure Boot testen. Lizenz festlegen. | `tests/samples/`, Entscheidung Stufe A/B |
| **1 – Kern** | Core: NMEA-Parser/-Builder, Datenmodell, UDP- und Spectate-Parser, Unit-Tests | Bibliothek + Tests grün |
| **2 – Pipeline (Konsole)** | Quellen → Fusion → Generatoren → TCP-Server/-Client, Profile. Konsolenprogramm für Tests mit XCSoar. | Erster durchgehender Fluss Condor → XCSoar |
| **3 – Virtueller COM (Stufe A)** | Com0comProvider, PortSetup-Helfer, Freie-Nummer-Logik, Condor-Läuft-Warnung | Kein HW VSP3 mehr nötig |
| **4 – Desktop-App** | WPF-UI, Assistent, Monitor, Tray, Einstellungen (JSON in `%APPDATA%`), `UDP.ini`-Helfer | Benutzbares Release 0.x |
| **5 – Verkehr verfeinern** | Auto-Erkennung des eigenen Flugzeugs, Extrapolation, PFLAM, Radar-Ansicht, HTTP-Spectate | Flüssiges FLARM-Radar |
| **6 – Installer** | WiX-MSI, lokales Release-Skript, Doku/Anleitung für XCSoar/LK8000 | Release 1.0 |
| **7 – Eigener Treiber (Stufe B)** | UMDF-2-Treiber, Signierung, Installer-Integration, Rückkanal | Vollständig eigenständig |

---

## 7. Offene Fragen und Risiken

1. **Treibersignatur** (größtes Risiko): Läuft das gebündelte com0com auf aktuellen Systemen mit Secure Boot? Wenn nicht: Wer trägt EV-Zertifikat und Partner-Center-Konto für einen eigenen Treiber? Bis dahin dienen Stufe C und manuelle Ports als Brücke.
2. **Spectate.json-Format und Pfad** in der aktuellen Condor-3-Version: Koordinatenformat, ob die Datei auch im Training bzw. Einzelflug geschrieben wird, Rate.
3. **UDP-Keys** über das Handbuch 1.03 hinaus (`latitude`/`longitude`, `radiofrequency`, `ax/ay/az`): Ab welcher Condor-Version gibt es sie? Davon hängt ab, wie gut Stufe C wird.
4. **Rückkanal:** Soll MC/Ballast aus XCSoar je nach Condor zurückgehen? Laut Handbuch gibt es dafür keine Eingangsschnittstelle, deshalb vorerst außerhalb des Umfangs.
5. **Mehrere Condor-Instanzen** oder ein Condor-PC mit Ausgabe an mehrere Geräte: TCP-Server mit mehreren Clients ist eingeplant, mehrere Condor-Instanzen nicht.
6. **Lizenz:** Neuimplementierung oder GPL-Übernahme aus XCSoar.

---

## 8. Erkenntnisse aus echten Mitschnitten (Condor 3, Nov. 2026)

- **NMEA:** nur `$GPGGA`, `$GPRMC`, `$LXWP0`, 1 Hz, alle Checksummen gültig. RMC hat **kein Datum**, GGA hat keine Geoid-Separation und hängt Leerfelder an (`…,1101.1,M,,,,,0000`). Die GGA-Höhe ist identisch mit der LXWP0-Baro-Höhe.
- **UDP:** etwa 10 Hz, je Datagramm eine Zeile pro Wert, im `LogToFile`-Mitschnitt durch Leerzeilen getrennt. Zusätzlich zum Handbuch gibt es `airspeed` (m/s), `radiofrequency` (MHz), `quaternionw`, `ax/ay/az`. `hudmessages` kommt nur, wenn es eine Meldung gibt. `water`/`flaps` fehlten im Mitschnitt (vermutlich abhängig vom Flugzeug).
- **Spectate.json:** siehe oben; Zahlen als Strings, Zusatzfelder `penalty`, `time`, `selected`.
