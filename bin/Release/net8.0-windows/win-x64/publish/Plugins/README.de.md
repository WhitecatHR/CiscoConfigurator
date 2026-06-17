# Datenbasierte Modulplugins

[Deutsch](README.de.md) | [English](README.en.md)

Der Ordner `Plugins` kann Dateien mit der Endung `*.ciscoplugin.json` enthalten. Ein Plugin darf lokalisierte Moduldefinitionen und Cisco-Befehlsvorlagen ergänzen, lädt jedoch keinen ausführbaren .NET-Code.

## Suchpfade

- `Plugins` neben der Anwendung
- `%APPDATA%/CiscoKonfigurator/Plugins`

## Anforderungen

- Plugin-ID muss eindeutig sein.
- Modulnamen müssen eindeutig sein und sollten einen Plugin-Präfix verwenden, beispielsweise `example.customBanner`.
- Feldnamen dürfen nicht mit bestehenden Feldern kollidieren.
- Plugin-Module müssen einen vorhandenen Fachbereich verwenden.
- Deutsche und englische Moduldefinitionen können im selben Manifest hinterlegt werden.
- Befehlsvorlagen verwenden Platzhalter im Format `{feldName}`.
- Pflichtfelder können über `requiredFields` festgelegt werden.

Die Beispieldatei besitzt absichtlich die Endung `.example` und wird daher nicht automatisch geladen.


## Plugin-Manager

Der Plugin-Manager befindet sich unter **System → Einstellungen → Plugin-Manager**.

Dort können:

- gefundene Plugins und deren Version angezeigt werden
- deutsche und englische Sprachdefinitionen geprüft werden
- Modul-, Feld- und Generatorfehler eingesehen werden
- Plugins aktiviert oder deaktiviert werden
- der Benutzer-Plugin-Ordner geöffnet werden
- die Anwendung nach Änderungen kontrolliert neu gestartet werden
- die Prüfung beim Anwendungsstart ein- oder ausgeschaltet werden

Aktivierungsänderungen werden in den Anwendungseinstellungen gespeichert und nach dem nächsten Neustart wirksam. Ungültige Plugins werden nicht geladen.
