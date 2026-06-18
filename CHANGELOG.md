# Changelog

Alle wesentlichen Änderungen am Cisco Configuration Tool werden in dieser Datei dokumentiert.

All notable changes to Cisco Configuration Tool are documented in this file.

---

## [Unreleased]

### Deutsch

#### Geändert

- Konfigurations-, Import- und Projektabläufe schrittweise aus den großen UI-Partial-Klassen in eigenständige Workflow-Services ausgelagert
- AutoSave-Verzögerung, Speicherung und Wiederherstellung in einen separaten Projekt-AutoSave-Service überführt
- Produkt-, Assembly- und Dateibeschreibung auf `Cisco Configuration Tool` vereinheitlicht; die veröffentlichte Datei heißt `Cisco Configuration Tool.exe`
- Self-Contained Single-File-Veröffentlichung beibehalten und `PublishTrimmed` ausdrücklich deaktiviert
- deutsche und englische Workflow-Meldungen gemeinsam über identische Lokalisierungsschlüssel ergänzt

#### Technische Verbesserungen

- Generatoraufrufe, Zusammenführung integrierter und pluginbasierter Abschnitte, Ausgabeaufbereitung, Duplikatprüfung, Kopierlogik und TXT-Export ohne WPF-Control-Abhängigkeiten gekapselt
- Importanalyse, Anwendungsvorbereitung und Export unbekannter Befehle von Dateidialogen und Control-Zuweisungen getrennt
- Projektladen, Projektspeichern, Normalisierung, Geräteübernahme und Projektversionierung über wiederverwendbare Services koordiniert
- asynchrone Dateizugriffe und Abbruchunterstützung in den neuen Workflows ergänzt
- vollständig verschluckte Ausnahmen in den bearbeiteten Bereichen durch Diagnoseprotokollierung ersetzt

#### Behoben

- englisch lokalisierte STP- und MST-Auswahl `Manual priority` wird vom Generator korrekt als manuelle Priorität erkannt
- gemischtsprachige Workflow- und Dialogmeldungen in den bearbeiteten Konfigurations-, Import- und Projektpfaden bereinigt
- vollständig verschluckte Fehler beim AutoSave, bei der Lokalisierungsaktualisierung und bei temporären Exportdateien werden diagnostisch protokolliert
- fehlende `System.IO`-Auflösung in den neuen Workflow-Services behoben; Datei- und Verzeichniszugriffe sind nun vollständig qualifiziert

---

### English

#### Changed

- incrementally moved configuration, import, and project operations out of the large UI partial classes into dedicated workflow services
- moved AutoSave debouncing, persistence, and restoration into a separate project AutoSave service
- standardized product, assembly, and file-description metadata as `Cisco Configuration Tool`; the published file is named `Cisco Configuration Tool.exe`
- retained self-contained single-file publishing and explicitly disabled `PublishTrimmed`
- added matching German and English localization keys for workflow messages

#### Technical improvements

- encapsulated generator calls, merging of built-in and plugin-based sections, output processing, duplicate detection, copy handling, and TXT export without WPF control dependencies
- separated import analysis, application planning, and unknown-command export from file dialogs and control assignments
- coordinated project loading, saving, normalization, device capture, and project versioning through reusable services
- added asynchronous file access and cancellation support to the new workflows
- replaced fully swallowed exceptions in the touched areas with diagnostic logging

#### Fixed

- English-localized STP and MST option `Manual priority` is now recognized correctly as a manual priority by the generator
- removed mixed-language workflow and dialog messages from the touched configuration, import, and project paths
- errors previously swallowed during AutoSave, localization refresh, and temporary export cleanup are now written to diagnostics
- fixed missing `System.IO` resolution in the new workflow services; file and directory access is now fully qualified

---

## [0.15-pre.Release] – 2026-06-17

### Deutsch

#### Hinzugefügt

- interne Projektversionierung für `.ciscoproject.json`-Projekte
- manuelle und automatische Projektstände mit Bezeichnung, Kommentar und Zeitstempel
- Vergleich eines Versionsstands mit dem aktuellen Projekt
- Wiederherstellung früherer Projektstände mit automatischer Sicherung des aktuellen Stands
- konfigurierbare Aktivierung und Begrenzung der Versionshistorie
- Export der Versionsmetadaten und Snapshots im vollständigen Projektpaket
- grafischer Plugin-Manager unter **System → Einstellungen**
- Anzeige von Plugin-ID, Version, Status, Modulanzahl, Sprachen und Diagnosemeldungen
- Aktivieren und Deaktivieren datenbasierter Plugins
- Öffnen der Plugin-Verzeichnisse und Neustart der Anwendung aus dem Plugin-Manager
- erweiterte Plugin-Validierung für Sprachvollständigkeit, doppelte IDs, Modul- und Feldkonflikte, Generatoren, Pflichtfelder, Bedingungen und Platzhalter
- lokalisierte Plugin-Diagnosen auf Deutsch und Englisch
- SSH-Live-Inventarisierung im Bereich **SSH → Inventarisierung**
- Erfassung von Hostname, Modell, Seriennummer, IOS-/IOS-XE-Version und Uptime
- Erfassung von IPv4-/IPv6-Interfaces, Beschreibungen, VLANs, Trunks und Port-Channels
- Erfassung von CDP-/LLDP-Nachbarn sowie erkannten Routingprotokollen, VRFs und HSRP
- selektive Übernahme inventarisierter Geräte, IPAM-Einträge und Nachbarverbindungen in das Projekt
- JSON-Export der Inventarisierungsdaten
- Speicherung strukturierter Inventardaten pro Projektgerät
- Inventardaten im Netzplan und im vollständigen Projektpaket
- automatische orthogonale Verbindungsführung mit Hinderniserkennung
- manuell verschiebbare Wegpunkte und Zurücksetzen automatischer Verbindungswege
- datenbasierte Plugin-Architektur über `*.ciscoplugin.json`
- automatische Strukturprüfung der deutschen und englischen Lokalisierungskataloge

#### Geändert

- Projektformat auf Version 2 erweitert
- AutoSave ist bei neuen Installationen standardmäßig deaktiviert
- Dropdowns in Topologierollen- und ACL-Tabellen verwenden explizite dunkle WPF-Stile
- manuelle Verbindungswege werden in Projektdateien und Projektpaketen gespeichert
- SVG- und HTML-Netzpläne verwenden dieselbe geroutete Verbindungslinie wie die interaktive Ansicht
- Projektpakete enthalten zusätzlich die Ordner `inventory/` und `versions/`
- Netzplan-Geräteübersicht zeigt vorhandene Modell-, Seriennummern- und Softwareinformationen
- README-Dateien und Changelog auf `0.15-pre.Release` aktualisiert
- deutsche und englische Übersetzungen der neuen Funktionen gemeinsam ergänzt und strukturell geprüft

#### Hinweise

- diese Version ist eine Vorabversion
- SSH-Inventarisierung benötigt einen erreichbaren Cisco-SSH-Dienst und ausreichende Rechte für die verwendeten `show`-Befehle
- Plugin-Änderungen an der Moduloberfläche werden nach einem Neustart wirksam
- das GitHub-Wiki/Benutzerhandbuch wird in einem separaten Schritt aktualisiert

---

### English

#### Added

- internal project versioning for `.ciscoproject.json` projects
- manual and automatic project snapshots with label, comment, and timestamp
- comparison of a saved version against the current project
- restoration of previous project states with an automatic snapshot of the current state
- configurable history enablement and retention limit
- export of version metadata and snapshots in complete project packages
- graphical Plugin Manager under **System → Settings**
- display of plugin ID, version, state, module count, languages, and diagnostics
- enable and disable actions for data-driven plugins
- opening plugin directories and restarting the application from the Plugin Manager
- extended plugin validation for localization completeness, duplicate IDs, module and field conflicts, generators, required fields, conditions, and placeholders
- localized plugin diagnostics in German and English
- SSH live inventory under **SSH → Inventory**
- collection of hostname, model, serial number, IOS / IOS-XE version, and uptime
- collection of IPv4/IPv6 interfaces, descriptions, VLANs, trunks, and port channels
- collection of CDP/LLDP neighbors and detected routing protocols, VRFs, and HSRP
- selective import of inventoried devices, IPAM entries, and neighbor connections into the project
- JSON export of inventory data
- structured inventory data stored per project device
- inventory data included in the network plan and complete project package
- automatic orthogonal connection routing with obstacle avoidance
- draggable waypoints and reset actions for automatic connection routes
- data-driven plugin architecture through `*.ciscoplugin.json`
- automatic structural validation of German and English localization catalogs

#### Changed

- extended the project format to version 2
- AutoSave is disabled by default for new installations
- topology-role and ACL table dropdowns use explicit dark WPF styles
- manual connection routes are stored in project files and project packages
- SVG and HTML network plans use the same routed connections as the interactive view
- project packages additionally contain the `inventory/` and `versions/` directories
- the network-plan device overview displays available model, serial-number, and software information
- updated README files and changelog to `0.15-pre.Release`
- added and structurally validated German and English translations for the new features together

#### Notes

- this version is a pre-release
- SSH inventory requires a reachable Cisco SSH service and sufficient permissions for the required `show` commands
- plugin changes affecting the module interface take effect after an application restart
- the GitHub Wiki/user manual will be updated in a separate step

---

## [0.14-pre.Release] – 2026-06-16

### Deutsch

#### Hinzugefügt

- intelligentes Netzplan-Layout nach Standort und Topologierolle
- unterstützte Topologierollen: WAN, Core, Distribution, Access und Other
- automatische Rollenerkennung anhand von Gerätename und Gerätetyp
- CDP- und LLDP-Nachbarimport zur automatischen Ergänzung von Verbindungen
- ACL-Editor für IPv4- und IPv6-ACLs
- tabellarische Bearbeitung von ACL-Regeln und Interface-Zuordnungen
- ACL-Regelanalyse für Schattenregeln, Redundanzen, doppelte Sequenzen, breite Freigaben und ungenutzte ACLs
- Routing-Overlay im interaktiven Netzwerkdiagramm
- Visualisierung von OSPF, OSPFv3, BGP, EIGRP, IS-IS, VRFs und HSRP
- Anzeige von Routinginformationen an Geräten und gerouteten Verbindungen
- vollständiger Projektpaket-Export als ZIP
- Export von Gerätekonfigurationen, Netzplan, SVG-Diagramm, IPAM, Verbindungen, ACLs, Routen und Analyseergebnissen
- maschinenlesbares `manifest.json` im Projektpaket
- Rollback-Entwürfe bei vorhandenen passenden Backups
- Standort und Topologierolle im Projektmodell
- Herkunft automatisch erkannter Verbindungen im Projektmodell
- Speicherung von ACL-Regeln und ACL-Zuordnungen im Projekt
- aktualisierte deutsche und englische README-Dokumentation

#### Geändert

- Diagrammlayout für größere und standortübergreifende Topologien erweitert
- SVG- und HTML-Netzplanexport um Routinginformationen ergänzt
- Netzplanexport um ACL-, VRF-, Routing- und Topologiedaten erweitert
- Projektpaket kann im Projekt- und Netzplanbereich exportiert werden
- ältere Projektdateien werden über die vorhandene Normalisierung weiterhin unterstützt

#### Hinweise

- diese Version ist eine Vorabversion
- erzeugte Konfigurationen und Analysen müssen vor produktiver Verwendung fachlich geprüft werden
- ein vollständiger Build wurde in der Entwicklungsumgebung dieses Updates nicht ausgeführt

---

### English

#### Added

- intelligent network-plan layout by site and topology role
- supported topology roles: WAN, Core, Distribution, Access, and Other
- automatic role detection based on device name and device type
- CDP and LLDP neighbor import for automatic connection discovery
- ACL editor for IPv4 and IPv6 ACLs
- tabular editing of ACL rules and interface assignments
- ACL rule analysis for shadowed rules, redundancy, duplicate sequence numbers, broad permissions, and unused ACLs
- routing overlay in the interactive network diagram
- visualization of OSPF, OSPFv3, BGP, EIGRP, IS-IS, VRFs, and HSRP
- routing information displayed on devices and routed connections
- complete project-package export as ZIP
- export of device configurations, network plan, SVG diagram, IPAM, connections, ACLs, routes, and analysis results
- machine-readable `manifest.json` in project packages
- rollback drafts when matching backups are available
- site and topology role in the project model
- source tracking for automatically discovered links
- project storage for ACL rules and ACL assignments
- updated German and English README documentation

#### Changed

- extended diagram layout for larger and multi-site topologies
- added routing information to SVG and HTML network-plan exports
- expanded network-plan export with ACL, VRF, routing, and topology data
- project packages can be exported from both the Project and Network Plan areas
- older project files remain supported through the existing normalization process

#### Notes

- this version is a pre-release
- generated configurations and analysis results require professional review before production use
- a complete build was not executed in the development environment used for this update
