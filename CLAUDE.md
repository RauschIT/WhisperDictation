# WhisperDictation

## Grafiken mit VectorCraft (MCP-Server "vectorcraft")

Grafiken (Icons usw.) werden mit VectorCraft über den MCP-Server gezeichnet, und zwar sichtbar in der App, damit der Nutzer zuschauen und korrigieren kann.

- Der MCP-Server (`vectorcraft-cli.exe mcp`) verbindet sich nur beim Verbinden mit einer App auf `127.0.0.1:7979`. Läuft keine, zeichnet er ohne Warnung unsichtbar im Hintergrund (headless).
- Vor dem ersten Zeichnen prüfen: `mcp__vectorcraft__screenshot` mit `window: true`. Schlägt das fehl, läuft der Server headless; dann nicht zeichnen.
- Läuft VectorCraft nicht mit Port 7979 (`netstat -ano | findstr ":7979"`), darf Claude die App selbst starten:
  ```powershell
  & "C:\Program Files\VectorCraft\vectorcraft.exe" --control 7979
  ```
  Ein schon offenes VectorCraft ohne `--control` vorher nicht ungefragt schließen, sondern den Nutzer fragen.
- Danach den Nutzer bitten: `/mcp` → vectorcraft → **Reconnect**. Das kann Claude nicht selbst.
- Der Startmenü-Eintrag `VectorCraft` ist eine Installer-Verknüpfung und startet die App ohne `--control`; Parameter lassen sich dort nicht eintragen.
- Entwürfe und Quellen liegen in `_private\design\icon\` (nicht im Repository) (`.vectorcraft`, `.svg`, 1024-px-PNG, `build_ico.py` für die .ico mit 16–256 px).

## Releases

Auslöser: Der Nutzer hat getestet und sagt „committen und Release bauen“. Dann baut Claude das Release selbst (Ausnahme von „Bauen macht der Nutzer“). Pushen und das GitHub-Release anlegen macht der Nutzer.

Ablauf, in dieser Reihenfolge:
1. `VERSION` erhöhen: Patch für Fehlerbehebungen, Minor für neue Funktionen. Eine schon gebaute Version (`Releases\<version>\` existiert) nie erneut verwenden.
2. `CHANGELOG.md` (Englisch): Eintrag `## <version> — <Datum>` mit `### Fixed` / `### Changed` / `### Added`.
3. Dokumentation vollständig nachziehen, nicht nur die offensichtliche Stelle: README (Funktionsliste, betroffene Abschnitte, Architekturdiagramm, Projektaufbau, Codezeilenzahl), alle Links und Sprungmarken prüfen, Installer-Skript bei neuen Prozessen/Dateien.
4. Genau ein Commit auf `main`, ohne Claude-Attribution. Noch nicht gepushte Commits per `--amend` ergänzen statt einen zweiten anzulegen. Tag `v<version>` (leichtgewichtig, wie `v1.1.0`).
5. Erst danach `installer\build-installer.ps1` ausführen: Der Build schreibt den Commit-Hash in die DLLs (`1.1.1+<hash>`), jede Änderung am Commit heißt neu bauen. Läuft die App aus `bin\Release`, sperrt sie die DLLs: Nutzer bitten, sie über Tray → Quit zu beenden, nicht selbst beenden.
6. Prüfen: `ProductVersion` von `build\publish\WhisperDictation.App.dll` endet auf den HEAD-Hash, `git status` ist sauber.
7. Release-Text (Englisch) nach `Releases\<version>\release-notes.md` schreiben (`Releases\` ist gitignored; die Prüfsumme steht erst nach dem Build fest und kann deshalb nicht in den Commit). Aufbau wie beim Release 1.1.0: ein Einleitungssatz mit Rausch-IT-Link, `## Fixed`/`## Changed` (bzw. `## Highlights`), `## Installation` (Dateiname, Update-Hinweis, keine Adminrechte, .NET-Runtime), `## Requirements`, `## Checksum` (SHA-256 aus der Skriptausgabe, Kleinbuchstaben), Links auf CHANGELOG und README.

**Release Notes nicht nachträglich ändern (User, 2026-11-04): „Du kannst die Release Notes nicht nachträglich verändern.“** Hat der Nutzer das Release selbst gebaut oder veröffentlicht, fasst Claude `release-notes.md` nicht mehr an, auch keine Platzhalter, sondern nennt ihm, was fehlt.

Am Ende dem Nutzer kompakt präsentieren, alles zum direkten Kopieren bzw. als klickbarer Link:
- Push: `git push origin main v<version>`
- Release anlegen: https://github.com/RauschIT/WhisperDictation/releases/new?tag=v<version>
- Titel: `WhisperDictation <version>`
- Beschreibung: Link auf `Releases/<version>/release-notes.md`
- Datei zum Anhängen: Link auf `Releases/<version>/WhisperDictation-<version>-setup.exe`
- SHA-256 der Datei
- Commit-Hash und Tag
