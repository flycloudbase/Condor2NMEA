# FLARM-Kollisionswarnungen

Condor2NMEA berechnet für den Multiplayer-Verkehr Kollisionswarnungen wie ein echtes FLARM und sendet sie in `$PFLAA`
(`<AlarmLevel>` je Flugzeug) und `$PFLAU` (gefährlichstes Flugzeug mit Richtung, Höhe, Abstand und ID). Cloudbase und
XCSoar zeigen und melden sie wie im echten Flugzeug. Abschaltbar unter Einstellungen → Multiplayer-Verkehr.

## Was über den echten FLARM-Algorithmus bekannt ist

Die Vorhersage von FLARM ist patentiert und nicht veröffentlicht. Öffentlich sind:

- **Warnung nach Zeit, nicht nach Abstand:** „Traffic collision warnings are issued based on the time remaining to the
  predicted collision (i.e. not the geometric distance between the aircraft)“ (PowerFLARM-Handbücher).
- **Vorhersagehorizont etwa 18–20 s:** FLARM sagt die eigene Flugbahn für weniger als die nächsten 20 s voraus und warnt
  nur bei hoher Wahrscheinlichkeit eines Zusammenstoßes in diesem Zeitraum.
- **Eingaben:** Geschwindigkeit, Kurs, Kurvenradius bzw. Drehrate, Wind; jedes Gerät sendet seine vorhergesagte Bahn.
  Eine Sicherheitsanalyse (Wang u. a., Universität Oxford) beschreibt das Verfahren als Interacting-Multiple-Model-Filter
  auf GPS-Position und -Geschwindigkeit. Die Algorithmen sind auf den Flugzeugtyp abgestimmt.
- **Warnstufen** laut Data Port ICD (FTD-012 7.23, `PFLAA`/`PFLAU`): 1 = 15–20 s, 2 = 10–15 s, 3 = 0–10 s bis zum
  Aufprall. Die Handbücher nennen je nach Gerät etwa 18/12/8 s oder 20/15/10 s.
- Quelloffene Vergleichsumsetzung: SoftRF (`TrafficHelper.cpp`). Dort wird nur geradeaus vorhergesagt, mit ±300 m
  Höhenband und Schwellen 9/13/19 s.

## Umsetzung in Condor2NMEA (`Core/Traffic/CollisionPredictor.cs`)

1. **Bahnvorhersage für beide Flugzeuge, 20 s in Schritten von 0,25 s,** mit konstanter Drehrate und konstantem
   Steigen. Dadurch werden Kreisflüge richtig vorhergesagt:
   - Zwei Segelflugzeuge auf getrennten Kreisen im selben Bart warnen nicht.
   - Wer in den Kreis hineinfliegt, löst eine Warnung aus.
   Die Drehrate wird geschätzt: für das eigene Flugzeug aus dem NMEA-Kurs, für die anderen aus den Kurswechseln in
   Spectate, jeweils geglättet und auf ±35 °/s begrenzt.
2. **Schutzraum, der mit der Vorhersagezeit wächst**, weil die Vorhersage unsicherer wird:
   - waagerecht 50 m + 2 m/s · t,
   - senkrecht 30 m + 1 m/s · t.
   Die Zeit bis zum Aufprall ist der erste Zeitpunkt, an dem das andere Flugzeug in diesem Raum liegt.
3. **Stufe nach ICD:** unter 10 s → 3, unter 15 s → 2, bis 20 s → 1.
4. **Keine Warnungen am Boden:** nicht, wenn das eigene oder das andere Flugzeug langsamer als 8 m/s ist oder als am
   Boden erkannt wird.
5. **Kein Flackern:** Eine Warnung bleibt 2 s stehen, nachdem die Vorhersage sie nicht mehr sieht. Höhere Stufen gelten
   sofort.

Die Parameter sind Annahmen und lassen sich an echten Multiplayer-Flügen nachschärfen. Die Tests in
`tests/Condor2Nmea.Tests/CollisionTests.cs` decken ab:
- Gegenkurs in allen drei Stufen,
- Parallelflug,
- Höhenstaffelung,
- gemeinsamen Bart,
- Einflug in den Kreis,
- Boden.
