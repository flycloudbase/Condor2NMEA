# `$PCBC` – Condor-Daten für Cloudbase

`$PCBC` ist eine eigene, herstellerspezifische NMEA-Satzfamilie von Condor2NMEA. Sie transportiert alles, was Condor 3
über das eigene Flugzeug und das Rennen weiß und was in GPS-, LXWP0- und FLARM-Sätzen keinen Platz hat. Gedacht ist sie
für [Cloudbase](https://github.com/flycloudbase/cloudbase); jedes andere Programm kann sie ignorieren.

Condor2NMEA sendet `$PCBC` **zusätzlich** zu `$GPRMC`, `$GPGGA`, `$LXWP0`, `$LXWP2` und `$PFLAU`/`$PFLAA`/`$PFLAM`
(Profil **Cloudbase** eines Ausgangs, oder im Profil „Benutzerdefiniert“ das Häkchen „Cloudbase ($PCBC)“).

## Rahmen

- Standard-NMEA-0183-Rahmung: `$PCBC,<Untertyp>,<Version>,…*hh<CR><LF>`, Prüfsumme wie üblich (XOR zwischen `$` und `*`).
- `P` = herstellerspezifisch, `CBC` = **C**loud**b**ase/**C**ondor.
- Feld 0 ist der Untertyp (`DATA`, `INFO`, `EVENT`), Feld 1 die Versionsnummer des Untertyps (derzeit `1`).
- Alle Felder stehen an fester Position. **Leeres Feld = unbekannt** (nicht 0).
- **Erweiterung nur hinten:** Neue Felder werden ausschließlich angehängt; Leser müssen überzählige Felder ignorieren.
  Ändert sich die Bedeutung eines Felds, steigt die Version.
- Zahlen mit `.` als Dezimaltrenner, Einheiten SI (m, m/s, kg), Winkel in Grad. Nie `-0.00`.
- Texte sind reines ASCII (Akzente entfernt, `ß` → `ss`) und enthalten nie `,` `*` `$` `!` `\` `^`.
- Zeilen sind höchstens rund 130 Zeichen lang (Cloudbase akzeptiert bis 256).

## `$PCBC,DATA` – Flugdaten

Takt: wie die Vario-Sätze (Einstellung „Vario-Rate“, Standard 2 Hz). Wird nur gesendet, wenn mindestens TAS,
TE-Vario, Netto oder Querlage bekannt ist.

| Feld | Inhalt | Einheit | Quelle in Condor | Hinweis |
|---|---|---|---|---|
| 0 | `DATA` | | | |
| 1 | Version, `1` | | | |
| 2 | Simulationszeit | `hhmmss.ss` | NMEA-Zeit, sonst UDP `time` | Uhrzeit **in Condor**, nicht die echte Uhr |
| 3 | IAS (angezeigte Fahrt) | m/s | UDP `airspeed` | Nicht im Handbuch 1.03; aus Mitschnitten als IAS gedeutet (liegt um √σ unter der Fluggeschwindigkeit) |
| 4 | TAS (wahre Fahrt) | m/s | `$LXWP0` Feld 1 (Condor schreibt dort TAS), sonst UDP `airspeed` | |
| 5 | Vario TE-kompensiert | m/s | `$LXWP0` Feld 3, sonst UDP `vario` | gleicher Wert wie im LXWP0 |
| 6 | **Netto-Vario** | m/s | UDP `nettovario` | echtes Luftmassen-Steigen aus Condor – Cloudbase muss es nicht aus der Polare schätzen |
| 7 | Vario unkompensiert | m/s | UDP `vario` (pneumatisch) | |
| 8 | E-Vario | m/s | UDP `evario` | folgt im Sollfahrt-Modus der STF-Anzeige, nicht als Steigen verwenden |
| 9 | Integrator (Mittelwert) | m/s | UDP `integrator` | |
| 10 | Kurs (Steuerkurs) | ° | `$LXWP0` Feld 9, sonst UDP `compass` | Nase, nicht Kurs über Grund |
| 11 | Querlage | ° | UDP `bank` | **+ = rechte Fläche unten** (Luftfahrt-Konvention; Condor liefert das umgekehrte Vorzeichen, ist bereits umgerechnet) |
| 12 | Längsneigung | ° | UDP `pitch` | + = Nase hoch |
| 13 | Lastvielfaches | g | UDP `gforce` | 1.00 im Geradeausflug |
| 14 | Höhe über Grund | m | UDP `height` | Schwerpunkt über Gelände **in Condors Landschaft** |
| 15 | Am Boden | `0`/`1` | UDP `wheelheight` < 0,3 m | |
| 16 | MacCready | m/s | UDP `MC` | braucht `ExtendedData1=1` |
| 17 | Wasserballast | kg | UDP `water` | braucht `ExtendedData1=1`; fehlt bei Flugzeugen ohne Wasser |
| 18 | Wölbklappen-Stellung | Index | UDP `flaps` | 0 = negativste Stellung; braucht `ExtendedData1=1` |
| 19 | Turbulenzstärke | 0…1 | UDP `turbulencestrength` | braucht `ExtendedData=1` |
| 20 | COM-Frequenz | MHz | UDP `radiofrequency` | Nicht im Handbuch 1.03 |

Beispiel (aus einem echten Condor-3-Mitschnitt):

```
$PCBC,DATA,1,122541.00,0.0,0.7,0.00,-1.01,0.00,0.00,0.00,252,5.7,6.1,1.00,0.7,1,0.0,,,0.84,123.500*24
```

## `$PCBC,INFO` – eigenes Flugzeug und Rennen

Takt: alle 5 s (Einstellung `CloudbaseInfoIntervalSeconds`). Das eigene Flugzeug kommt aus Spectate.json (markiert mit
`"selected":"True"`, sonst die eingestellte Wettbewerbskennung). Ohne Multiplayer-Daten sind die Felder 4–15 leer.

| Feld | Inhalt | Beispiel |
|---|---|---|
| 0 | `INFO` | |
| 1 | Version, `1` | |
| 2 | Simulator | `Condor` |
| 3 | Sendende Software | `Condor2NMEA 0.1.0` |
| 4 | Wettbewerbskennung | `CG` |
| 5 | Kennzeichen | `D-KECG` |
| 6 | Flugzeugmuster | `Ventus3-18` |
| 7 | Pilot | `Max Muster` |
| 8 | Land | `Germany` |
| 9 | Status im Rennen | `Warmup`, `Flying`, `Finished`, … (wie Condor) |
| 10 | Platz | `1` |
| 11 | Punkte | `999.7` |
| 12 | Strecke | `252.3` (km) |
| 13 | Schnitt | `210.7` (km/h) |
| 14 | Rennzeit | `01:11:51` |
| 15 | Piloten in der Sitzung | `14` |

Feld 2 erlaubt Cloudbase, den Condor-Modus automatisch zu erkennen oder zu bestätigen.

```
$PCBC,INFO,1,Condor,Condor2NMEA 0.1.0,MM,D-KXMM,Ventus3-18,Max Muster,Germany,Finished,1,999.7,252.3,210.7,01:11:51,2*4A
```

## `$PCBC,EVENT` – Meldungen aus Condor

Gesendet, sobald Condor eine neue HUD-Meldung zeigt (UDP `hudmessages`, braucht `ExtendedData=1`), je Meldung einmal.

| Feld | Inhalt |
|---|---|
| 0 | `EVENT` |
| 1 | Version, `1` |
| 2 | Simulationszeit `hhmmss.ss` |
| 3 | Meldungstext wie in Condor, z. B. `M.Muster released`, `M.Muster landed`, `M.Muster found an outstanding lift` |

```
$PCBC,EVENT,1,122550.04,M.Muster released*22
```

Damit kann Cloudbase z. B. Ausklinken und Landung genau erkennen (Logbuch, Flugaufzeichnung) statt sie aus der
Bewegung zu schätzen.

## Vorschlag für die Auswertung in Cloudbase

| `$PCBC`-Wert | Ziel in Cloudbase |
|---|---|
| DATA 3 / 4 | `OwnshipSample.airspeed.ias` / `.tas` – IAS dann „vom Gerät“ statt „aus TAS“ |
| DATA 5 | `OwnshipSample.vario` (wie LXWP0) |
| DATA 6 | Netto-Box direkt aus dem Gerät statt aus Polare und Fahrt |
| DATA 11–13 | Kreisflug-Erkennung, Fluglage-Anzeige, Thermikassistent |
| DATA 14, 15 | Höhe über Grund in Condors Gelände (statt Mapterhorn, das von Condors Landschaft abweichen kann), Start/Landung |
| DATA 16–18 | `DeviceInfo.macCready`, Ballast (kg statt Überladefaktor), Klappen |
| INFO 4–8 | Kennzeichen/WBK/Muster im Condor-Modus |
| INFO 9–15 | Rennstand-Anzeige |
| EVENT | Logbuch-Ereignisse (Ausklinken, Landung, Neustart) |

`$PCBC` ändert nichts an den übrigen Sätzen: Ein Cloudbase-Gerät „Condor“ funktioniert mit und ohne `$PCBC`.
