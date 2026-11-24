# `$PCBC` – Satzfamilie für Condor-Daten

`$PCBC` ist eine herstellerspezifische NMEA-0183-Satzfamilie von Condor2NMEA. Sie überträgt alles, was der
Flugsimulator Condor 3 über das eigene Flugzeug, das Rennen und die Aufgabe weiß und was in GPS-, LXWP- und
FLARM-Sätzen keinen Platz hat. Gedacht ist sie für [Cloudbase](https://github.com/flycloudbase/cloudbase). Jedes
andere Programm kann sie gefahrlos ignorieren, weil sie die Standardsätze nicht verändert.

Dieses Dokument ist die verbindliche Beschreibung des Formats. Code, der `$PCBC` erzeugt oder liest, richtet sich
danach.

## Inhalt

1. [Überblick](#überblick)
2. [Einschalten](#einschalten)
3. [Allgemeine Regeln](#allgemeine-regeln)
4. [`DATA` – Flugdaten](#pcbcdata--flugdaten)
5. [`INFO` – eigenes Flugzeug und Rennen](#pcbcinfo--eigenes-flugzeug-und-rennen)
6. [`EVENT` – Meldungen aus Condor](#pcbcevent--meldungen-aus-condor)
7. [`TASK` und `TP` – Aufgabe](#pcbctask-und-pcbctp--aufgabe)
8. [Leitfaden für Empfänger](#leitfaden-für-empfänger)
9. [Voraussetzungen in Condor](#voraussetzungen-in-condor)
10. [Offene Punkte](#offene-punkte)
11. [Versionen](#versionen)

## Überblick

| Satz | Inhalt | Wann | Länge (ca.) |
|---|---|---|---|
| `$PCBC,DATA` | IAS, TAS, Varios (TE, Netto, unkompensiert, elektronisch, Integrator), Kurs, Fluglage, G-Last, Höhe über Grund, am Boden, MacCready, Wasser, Klappen, Turbulenz, Funkfrequenz | im Vario-Takt (Standard 2 Hz) | bis 140 Zeichen |
| `$PCBC,INFO` | Simulator, Software, eigenes Flugzeug (WBK, Kennzeichen, Muster, Pilot, Land), Rennstand | alle 5 s | bis 200 Zeichen |
| `$PCBC,EVENT` | Meldung aus Condor (Ausklinken, Landung, Neustart, starker Bart …) | sofort, je Meldung einmal | bis 150 Zeichen |
| `$PCBC,TASK` | Kopf der Aufgabe: Hash, Anzahl Punkte, Strecke, Landschaft, Name | bei Änderung, bei neuer Verbindung, sonst alle 30 s | bis 110 Zeichen |
| `$PCBC,TP` | ein Wendepunkt der Aufgabe mit Position und Sektor | direkt nach `TASK`, einer je Punkt | bis 140 Zeichen |

`$PCBC` ergänzt die Standardsätze, die Condor2NMEA im selben Datenstrom sendet:
- `$GPRMC` und `$GPGGA` (Position, Grundgeschwindigkeit, Höhe),
- `$LXWP0` (TAS, Barohöhe, TE-Vario, Wind) und `$LXWP2` (MacCready),
- `$PFLAU`, `$PFLAA` und `$PFLAM` (Multiplayer-Verkehr mit Kollisionswarnungen nach FLARM-ICD).

## Einschalten

In Condor2NMEA unter **Einstellungen → Ausgänge**:
- Profil **„Cloudbase“**: sendet GPS, LXWP0, LXWP2, FLARM und `$PCBC`. Ab Werk gibt es dafür einen Ausgang
  „Cloudbase (TCP-Server 4354)“.
- Profil **„Benutzerdefiniert“** mit dem Häkchen **„Cloudbase ($PCBC)“**: `$PCBC` zusammen mit beliebigen anderen
  Sätzen.

In Cloudbase verbindet sich ein Gerät vom Typ **Condor** per TCP mit der IP-Adresse des Condor-PCs und Port 4354.

## Allgemeine Regeln

**Rahmung.** Wie NMEA 0183: `$PCBC,<Untertyp>,<Version>,<Feld 2>,…*hh<CR><LF>`. `P` kennzeichnet einen
herstellerspezifischen Satz, `CBC` steht für **C**loud**b**ase/**C**ondor. Die Prüfsumme `hh` ist das XOR aller
Zeichen zwischen `$` und `*`, als zwei Hex-Ziffern in Großbuchstaben.

**Felder.**
- Feld 0 ist der Untertyp, Feld 1 die Versionsnummer dieses Untertyps (derzeit überall `1`).
- Alle weiteren Felder haben eine **feste Position**.
- Ein **leeres Feld heißt „unbekannt“**, nicht 0. Empfänger setzen in diesem Fall nichts.
- Ein Satz hat nie weniger Felder als in seiner Version beschrieben. Leere Felder am Ende werden mitgeschickt.

**Versionen.**
- Neue Felder kommen **nur hinten** dazu, die Versionsnummer bleibt dann gleich. Empfänger ignorieren überzählige
  Felder.
- Ändert sich Bedeutung, Einheit oder Position eines Felds, steigt die Versionsnummer dieses Untertyps.
- Empfänger verwerfen Sätze mit einer höheren Versionsnummer, als sie kennen, und Untertypen, die sie nicht kennen.

**Zahlen.**
- Immer `.` als Dezimaltrenner, nie Tausendertrennzeichen, nie Exponent, nie `-0.00`.
- Die Nachkommastellen stehen je Feld in den Tabellen.

**Einheiten.** SI: m, m/s, kg, °C. Winkel in Grad.
- Richtungen 0–359, rechtweisend, im Uhrzeigersinn.
- Koordinaten in Dezimalgrad, Süd und West negativ.

**Text.**
- Reines ASCII von Leerzeichen bis `~`. Akzente werden entfernt, `ß` wird `ss`.
- Nie die Zeichen `,` `*` `$` `!` `\` `^`.
- Jedes Textfeld hat eine Höchstlänge (siehe Tabellen); längere Texte werden abgeschnitten.

**Länge.** Jeder Satz ist kürzer als 256 Zeichen. Das ist die Grenze, die auch Cloudbase und das LX-Protokoll
erlauben. Die FLARM-Grenze von 80 Zeichen gilt für `$PCBC` nicht.

**Zeit.**
- `Sim-Zeit` ist die Uhrzeit **in Condor** (`hhmmss.ss`), nicht die echte Uhrzeit. Sie stammt aus Condors NMEA,
  ersatzweise aus UDP `time`.
- Empfänger stempeln Werte wie alle NMEA-Werte mit der Empfangszeit. Die Sim-Zeit ist nur Zusatzinformation, etwa für
  Logbuch-Ereignisse.

## `$PCBC,DATA` – Flugdaten

```
$PCBC,DATA,1,<SimZeit>,<IAS>,<TAS>,<TE>,<Netto>,<Vario>,<EVario>,<Integrator>,<Kurs>,<Querlage>,<Längsneigung>,
     <GLast>,<HöheÜberGrund>,<AmBoden>,<MacCready>,<Wasser>,<Klappen>,<Turbulenz>,<Funk>*hh
```

Gesendet im Vario-Takt (Einstellungen → Erweitert → Vario-Rate, Standard 2 Hz). Der Satz kommt nur, wenn mindestens
TAS, TE-Vario, Netto oder Querlage bekannt ist.

| Feld | Name | Typ / Format | Einheit | Quelle in Condor | Hinweis |
|---|---|---|---|---|---|
| 0 | Untertyp | `DATA` | | | |
| 1 | Version | `1` | | | |
| 2 | Sim-Zeit | `hhmmss.ss` | | NMEA-Zeit, sonst UDP `time` | |
| 3 | IAS | 1 Nachkommastelle | m/s | UDP `airspeed` | Nicht im Handbuch 1.03. Aus Mitschnitten als angezeigte Fahrt gedeutet: Sie liegt um √σ unter der Fluggeschwindigkeit. |
| 4 | TAS | 1 Nachkommastelle | m/s | `$LXWP0` Feld 1, sonst UDP `airspeed` | Condor schreibt TAS in das IAS-Feld von LXWP0 |
| 5 | TE-Vario | 2 Nachkommastellen | m/s | `$LXWP0` Feld 3, sonst UDP `vario` | gleicher Wert wie in LXWP0 |
| 6 | Netto-Vario | 2 Nachkommastellen | m/s | UDP `nettovario` | Steigen der Luftmasse, von Condor berechnet |
| 7 | Vario unkompensiert | 2 Nachkommastellen | m/s | UDP `vario` | pneumatisches Vario |
| 8 | E-Vario | 2 Nachkommastellen | m/s | UDP `evario` | Folgt im Sollfahrt-Modus der Sollfahrtanzeige; nicht als Steigen verwenden |
| 9 | Integrator | 2 Nachkommastellen | m/s | UDP `integrator` | Mittelwert des Varios |
| 10 | Kurs | ganzzahlig, 0–359 | ° | `$LXWP0` Feld 9, sonst UDP `compass` | Steuerkurs der Flugzeugnase, nicht Kurs über Grund |
| 11 | Querlage | 1 Nachkommastelle | ° | UDP `bank` | **+ = rechte Fläche unten.** Condor liefert das umgekehrte Vorzeichen; hier schon umgerechnet. |
| 12 | Längsneigung | 1 Nachkommastelle | ° | UDP `pitch` | + = Nase hoch |
| 13 | G-Last | 2 Nachkommastellen | g | UDP `gforce` | 1.00 im Geradeausflug |
| 14 | Höhe über Grund | 1 Nachkommastelle | m | UDP `height` | Schwerpunkt über dem Gelände **von Condors Landschaft** |
| 15 | Am Boden | `0` / `1` | | UDP `wheelheight` < 0,3 m | |
| 16 | MacCready | 1 Nachkommastelle | m/s | UDP `MC` | braucht `ExtendedData1=1` |
| 17 | Wasserballast | ganzzahlig | kg | UDP `water` | braucht `ExtendedData1=1`; leer bei Flugzeugen ohne Wassertank |
| 18 | Wölbklappen | ganzzahlig | Index | UDP `flaps` | 0 = negativste Stellung; braucht `ExtendedData1=1` |
| 19 | Turbulenz | 2 Nachkommastellen | 0…1 | UDP `turbulencestrength` | braucht `ExtendedData=1` |
| 20 | COM-Frequenz | 3 Nachkommastellen | MHz | UDP `radiofrequency` | nicht im Handbuch 1.03 |

Beispiele:

```
$PCBC,DATA,1,131500.00,63.5,83.2,-4.17,-0.85,-3.90,-4.50,1.20,98,30.0,2.9,1.60,812.4,0,1.5,120,3,0.37,123.500*0D
$PCBC,DATA,1,122541.00,0.0,0.7,0.00,-1.01,0.00,0.00,0.00,252,5.7,6.1,1.00,0.7,1,0.0,,,0.84,123.500*24
```

Die erste Zeile zeigt ein Flugzeug im Flug: 30° Querlage rechts, 1,6 g, MacCready 1,5 m/s, 120 kg Wasser, Klappe 3.
Die zweite stammt aus einem echten Mitschnitt und zeigt ein Flugzeug am Boden. Wasser und Klappen sind leer, weil
dieses Flugzeug sie nicht hat.

## `$PCBC,INFO` – eigenes Flugzeug und Rennen

```
$PCBC,INFO,1,<Simulator>,<Software>,<WBK>,<Kennzeichen>,<Muster>,<Pilot>,<Land>,<Status>,<Platz>,<Punkte>,
     <Strecke>,<Schnitt>,<Rennzeit>,<Piloten>*hh
```

Gesendet alle 5 s (Einstellung `CloudbaseInfoIntervalSeconds`), auch ohne Multiplayer-Daten. Dann sind die Felder
4–15 leer.

Das eigene Flugzeug kommt aus Spectate.json:
- das manuell gewählte (Seite Verkehr),
- sonst das von Condor als `selected` markierte, wenn es höchstens 2 km von der eigenen Position entfernt ist,
- sonst das, dessen Position zur eigenen passt.

| Feld | Name | Typ | Höchstlänge | Beispiel | Quelle |
|---|---|---|---|---|---|
| 0 | Untertyp | `INFO` | | | |
| 1 | Version | `1` | | | |
| 2 | Simulator | Text | | `Condor` | fest |
| 3 | Software | Text | 24 | `Condor2NMEA 0.1.0` | Condor2NMEA |
| 4 | Wettbewerbskennung | Text | 8 | `CG` | Spectate `CN` |
| 5 | Kennzeichen | Text | 12 | `D-KECG` | Spectate `RN` |
| 6 | Muster | Text | 20 | `Ventus3-18` | Spectate `plane` |
| 7 | Pilot | Text | 30 | `Max Muster` | Spectate `firstname` + `lastname` |
| 8 | Land | Text | 20 | `Germany` | Spectate `country` |
| 9 | Rennstatus | Text | 16 | `Warmup`, `Flying`, `Finished` | Spectate `playerstatus`, unverändert |
| 10 | Platz | Text | 4 | `1` | Spectate `rank` |
| 11 | Punkte | 1 Nachkommastelle | | `999.7` | Spectate `score` |
| 12 | Strecke | 1 Nachkommastelle, km | | `252.3` | Spectate `dist` |
| 13 | Schnitt | 1 Nachkommastelle, km/h | | `210.7` | Spectate `averagespeed` |
| 14 | Rennzeit | Text `hh:mm:ss` | 10 | `01:11:51` | Spectate `time` |
| 15 | Piloten in der Sitzung | ganzzahlig | | `14` | Anzahl Einträge in Spectate |

Feld 2 erlaubt einem Empfänger, den Condor-Modus zu erkennen oder zu bestätigen.

```
$PCBC,INFO,1,Condor,Condor2NMEA 0.1.0,MM,D-KXMM,Ventus3-18,Max Muster,Germany,Finished,1,999.7,252.3,210.7,01:11:51,2*4A
$PCBC,INFO,1,Condor,Condor2NMEA 0.1.0,,,,,,,,,,,,*09
```

## `$PCBC,EVENT` – Meldungen aus Condor

```
$PCBC,EVENT,1,<SimZeit>,<Meldung>*hh
```

Condor zeigt Meldungen im Bild an (HUD) und schickt sie per UDP `hudmessages`, solange sie angezeigt werden.
Condor2NMEA sendet jede Meldung **einmal**, sobald sie neu erscheint. Verschwindet sie länger als 5 s und kommt
wieder, wird sie erneut gesendet.

| Feld | Name | Typ | Höchstlänge |
|---|---|---|---|
| 0 | Untertyp `EVENT` | | |
| 1 | Version `1` | | |
| 2 | Sim-Zeit | `hhmmss.ss` | |
| 3 | Meldung | Text, wie in Condor | 120 |

Bisher beobachtete Meldungen sind englisch, mit Pilotennamen vorne:

| Meldung | Bedeutung |
|---|---|
| `<Name> released` | Ausklinken (Schlepp oder Winde) |
| `<Name> landed` | Landung |
| `<Name> restarted` | Neustart |
| `<Name> found an outstanding lift` | starker Bart gefunden |

Die Liste ist nicht vollständig. Empfänger sollten unbekannte Meldungen als Text anzeigen oder ignorieren.

```
$PCBC,EVENT,1,122550.04,M.Muster released*22
```

## `$PCBC,TASK` und `$PCBC,TP` – Aufgabe

Condor2NMEA liest die Condor-Aufgabe (`.fpl`). Einstellbar unter Einstellungen → Aufgabe für Cloudbase:
- standardmäßig die zuletzt gespeicherte Datei in `Dokumente\Condor3\FlightPlans` (mit Unterordnern),
- oder eine bestimmte Datei.

Die Wendepunkte stehen in der Datei als Landschafts-Koordinaten. Condor2NMEA rechnet sie mit Condors eigener
`NaviCon.dll` in WGS84 um.

Gesendet wird ein **Block**: ein `TASK`-Satz, danach ein `TP`-Satz je Punkt in Reihenfolge des Index. Der Block kommt:
- sofort, wenn sich die Aufgabe ändert,
- sofort, wenn sich ein Gerät mit einem Cloudbase-Ausgang verbindet,
- sonst alle 30 s (Einstellung `Task.RepeatSeconds`).

Ohne Aufgabe oder ohne gültige Koordinaten wird nichts gesendet.

### `$PCBC,TASK`

```
$PCBC,TASK,1,<Hash>,<Anzahl>,<Strecke>,<Landschaft>,<Name>*hh
```

| Feld | Name | Typ | Hinweis |
|---|---|---|---|
| 0 | Untertyp `TASK` | | |
| 1 | Version `1` | | |
| 2 | Hash | 8 Hex-Ziffern | Kennung des Inhalts (z. B. `3CDCE314`). Ändert sich, sobald sich ein Punkt, ein Sektor oder die Landschaft ändert. |
| 3 | Anzahl Punkte | ganzzahlig | einschließlich Startplatz |
| 4 | Strecke | 1 Nachkommastelle, km | Abflug → Wenden → Ziel, ohne Startplatz, Mittelpunkte |
| 5 | Landschaft | Text, max. 30 | z. B. `AA3` |
| 6 | Name | Text, max. 50 | Name der Aufgabe, sonst Dateiname |

### `$PCBC,TP`

```
$PCBC,TP,1,<Hash>,<Index>,<Anzahl>,<Rolle>,<Breite>,<Länge>,<Höhe>,<Sektortyp>,<Radius>,<Winkel>,<TPAltitude>,
     <Fensterbreite>,<TPHeight>,<Ausrichtung>,<Flugplatz>,<Name>*hh
```

| Feld | Name | Typ / Einheit | Werte | `.fpl`-Schlüssel |
|---|---|---|---|---|
| 0 | Untertyp `TP` | | | |
| 1 | Version `1` | | | |
| 2 | Hash | 8 Hex-Ziffern | wie im zugehörigen `TASK` | |
| 3 | Index | ganzzahlig | 0 = Startplatz | |
| 4 | Anzahl | ganzzahlig | wie `TASK` Feld 3 | `Count` |
| 5 | Rolle | Buchstabe | `T` Startplatz (nicht gewertet), `S` Abflug, `W` Wende, `F` Ziel | aus der Position |
| 6 | Breite | Grad, 6 Nachkommastellen | Süd negativ | `TPPosX`/`TPPosY` → NaviCon |
| 7 | Länge | Grad, 6 Nachkommastellen | West negativ | |
| 8 | Geländehöhe | ganzzahlig, m | | `TPPosZ` |
| 9 | Sektortyp | ganzzahlig | `0` Sektor/Zylinder, `1` Fenster | `TPSectorType` |
| 10 | Radius | ganzzahlig, m | | `TPRadius` |
| 11 | Winkel | ganzzahlig, ° | `360` = Zylinder, sonst Sektor symmetrisch zur Winkelhalbierenden der Schenkel | `TPAngle` |
| 12 | TPAltitude | ganzzahlig, m | Mindesthöhe, bei Fenstern Höhe des Fensters | `TPAltitude` |
| 13 | Fensterbreite | ganzzahlig, m | nur bei Fenstern sinnvoll | `TPWidth` |
| 14 | TPHeight | ganzzahlig, m | Höchsthöhe, bei Fenstern Fensterhöhe | `TPHeight` |
| 15 | Ausrichtung | ganzzahlig, ° | nur bei Fenstern | `TPAzimuth` |
| 16 | Flugplatz | `0` / `1` | | `TPAirport` |
| 17 | Name | Text, max. 30 | | `TPName` |

Die Felder 9–15 geben Condors Werte unverändert weiter. Die Rolle folgt Condors Aufbau:
- Punkt 0 ist der Startplatz,
- Punkt 1 der Abflug,
- der letzte Punkt das Ziel.

```
$PCBC,TASK,1,3CDCE314,5,64.6,TestLandscape,Testaufgabe Muster*11
$PCBC,TP,1,3CDCE314,0,5,T,46.450453,10.701292,700,0,3000,360,1500,0,5000,0,1,Startplatz*4F
$PCBC,TP,1,3CDCE314,1,5,S,46.468468,10.727273,800,0,3000,90,1500,0,2500,0,0,Abflug*79
$PCBC,TP,1,3CDCE314,2,5,W,46.540541,10.961039,2100,0,500,360,2200,0,5000,0,0,Gipfel Sud*20
$PCBC,TP,1,3CDCE314,3,5,W,46.675676,10.831169,1200,1,0,0,1800,1000,400,45,0,Fenster*14
$PCBC,TP,1,3CDCE314,4,5,F,46.450453,10.701292,700,0,3000,360,1500,0,5000,0,1,Startplatz*59
```

## Leitfaden für Empfänger

**Lesen.**
1. Den Satz wie jeden NMEA-Satz prüfen (Prüfsumme, höchstens 256 Zeichen) und an den Kommas zerlegen.
2. Bei unbekanntem Untertyp oder einer Version größer als bekannt: ignorieren.
3. Jedes Feld einzeln lesen. Ein leeres oder ungültiges Feld lässt den bisherigen Wert unverändert und setzt ihn nicht
   auf 0.
4. Werte mit der Empfangszeit stempeln und wie andere Gerätewerte veralten lassen. `DATA` kommt mehrmals pro
   Sekunde; bleibt es länger als ein paar Sekunden aus, gelten die Werte nicht mehr.

**Vorrang gegenüber den Standardsätzen.** `$PCBC` und die Standardsätze kommen aus derselben Quelle und widersprechen
sich nicht:

| Wert | auch in | Empfehlung |
|---|---|---|
| TAS, TE-Vario, Kurs | `$LXWP0` | gleich; einmal übernehmen, nicht doppelt zählen |
| MacCready | `$LXWP2` Feld 1 | gleich |
| Wasser | – (`$LXWP2` Feld 2 bleibt leer) | aus `DATA` Feld 17 in kg; ein Überladefaktor lässt sich mit den Massen aus dem eigenen Flugzeugprofil berechnen |
| IAS, Netto, Fluglage, G-Last, Höhe über Grund | – | nur in `$PCBC` |

**Aufgabe zusammensetzen.**

```
bei TASK(hash, anzahl, …):   puffer[hash] = { kopf, punkte: leer, anzahl }
bei TP(hash, index, …):      wenn puffer[hash] fehlt: verwerfen (Kopf kommt mit der nächsten Wiederholung)
                             puffer[hash].punkte[index] = punkt
                             wenn alle Indizes 0 … anzahl−1 da sind:
                                 wenn hash == aktiver Hash: nichts tun (unverändert)
                                 sonst: Aufgabe übernehmen bzw. anbieten, aktiver Hash = hash
alte Puffer nach ca. 2 min ohne Ergänzung verwerfen
```

Gleicher Hash heißt unveränderte Aufgabe. Ein Empfänger übernimmt sie nicht erneut und fragt auch nicht erneut nach.

**Condor-Modus.** `INFO` Feld 2 = `Condor` zeigt sicher, dass der Datenstrom aus dem Simulator kommt.

## Voraussetzungen in Condor

| Damit … | braucht es |
|---|---|
| `DATA` überhaupt kommt | NMEA-Ausgabe über den virtuellen COM-Port und/oder UDP-Ausgabe (`UDP.ini` `Enabled=1`) |
| Netto, Lage, G-Last, IAS kommen | UDP-Ausgabe |
| Höhe über Grund, Turbulenz, `EVENT` kommen | `UDP.ini` `ExtendedData=1` |
| MacCready, Wasser, Klappen kommen | `UDP.ini` `ExtendedData1=1` |
| `INFO` Felder 4–15 gefüllt sind | Multiplayer-Flug (Spectate.json) |
| `TASK`/`TP` kommen | gespeicherte Aufgabe (`.fpl`) und Condors `NaviCon.dll`; Condor2NMEA ist dafür 32-Bit wie Condor |

Der Knopf „UDP-Ausgabe einrichten“ in Condor2NMEA setzt `Enabled`, `ExtendedData` und `ExtendedData1`.

## Offene Punkte

- **IAS:** Dass UDP `airspeed` die angezeigte Fahrt ist, ist aus Mitschnitten abgeleitet, nicht dokumentiert.
- **Aufgabe:** Was Condor 3 in `TPAltitude`/`TPHeight` genau meint und wo Condor die Aufgabe eines
  Multiplayer-Rennens ablegt, ist noch an echten Dateien zu bestätigen.
- **Meldungen:** Die Liste der `EVENT`-Meldungen ist unvollständig.

## Versionen

| Datum | Untertyp | Version | Änderung |
|---|---|---|---|
| 14.11.2026 | `DATA`, `INFO`, `EVENT` | 1 | erste Fassung |
| 19.11.2026 | `TASK`, `TP` | 1 | Aufgabe aus Condor |
