# Cisco Konfigurator

[Deutsch](README.de.md) | [English](README.en.md)

## Überblick

Der Cisco Konfigurator ist eine C#-WPF-Anwendung für die strukturierte Erstellung, Verwaltung, Prüfung und Dokumentation von Cisco-IOS- und IOS-XE-Konfigurationen. Die Anwendung unterstützt Router, Layer-3-Switches und Layer-2-Switches und stellt die benötigten Funktionen über fachlich gegliederte Module bereit.

Der Funktionsumfang geht über die reine Konfigurationserzeugung hinaus. Enthalten sind Mehrgeräte-Projekte, IPAM, Portplanung, Gegenstellenkonfigurationen, Konfigurationsanalyse, SSH-Übertragung, Backups, Netzwerkdiagramme und exportierbare Netzpläne.


## Benutzung

Der Cisco Konfigurator ist für einen projektorientierten Arbeitsablauf ausgelegt. Konfiguration, Geräteverwaltung, Prüfung und Netzplan greifen dabei auf denselben Projektstand zu.

### 1. Projekt anlegen

1. Den Bereich **Projekt** öffnen.
2. Ein neues Netzwerkprojekt anlegen oder ein vorhandenes Projekt öffnen.
3. Projektname, Projektnummer, Organisation, Standort, Projektleitung, Bearbeiter, Version, Status und Beschreibung erfassen.
4. Das Projekt als `.ciscoproject.json` speichern.

### 2. Zielgerät festlegen

1. In der Kopfzeile den Gerätetyp auswählen:
   - Router
   - Layer-3-Switch
   - Layer-2-Switch
2. Den Konfigurationsmodus auswählen:
   - ohne VRF
   - mit VRF
3. Im Bereich **Basis** mindestens Hostname und benötigte Grundeinstellungen eintragen.

Die sichtbaren Module und Eingabefelder werden passend zu Gerätetyp und Modus gefiltert.

### 3. Konfigurationsmodule bearbeiten

1. In der linken Navigation den benötigten Fachbereich öffnen.
2. Die erforderlichen Module aktivieren.
3. Eingabefelder ausfüllen und Hinweise sowie Tooltips beachten.
4. Abhängigkeiten zwischen VLANs, Interfaces, Routing, ACLs, VRFs und Sicherheitsfunktionen prüfen.
5. Nicht benötigte Module deaktiviert lassen, da nur aktive Module in die Konfiguration übernommen werden.

### 4. Konfiguration erzeugen

1. Den Bereich **Ausgabe** öffnen.
2. Die Vorschau aktualisieren.
3. Warnungen, doppelte Befehle und Platzhalter prüfen.
4. Die erzeugte Konfiguration kopieren oder als TXT-Datei exportieren.
5. `write memory` nur aktivieren, wenn das direkte Speichern auf dem Zielgerät beabsichtigt ist.

### 5. Gerät in das Projekt übernehmen

1. Den Bereich **Projekt** öffnen.
2. **Aktuelles Gerät übernehmen** auswählen.
3. Weitere Router oder Switches auf dieselbe Weise konfigurieren und zum Projekt hinzufügen.
4. Bereits gespeicherte Projektgeräte können geladen, aktualisiert, dupliziert, exportiert oder entfernt werden.

### 6. IPAM und Portplanung verwenden

1. Den Bereich **IPAM** öffnen.
2. Netze manuell erfassen oder aus der aktuellen beziehungsweise gespeicherten Konfiguration importieren.
3. VLAN, Präfix, Gateway, DHCP-Bereich, Gerät und Interface zuordnen.
4. Überschneidungen und mehrfach verwendete Gateways prüfen.
5. Den Interface- und Portplan aus einer aktuellen oder gespeicherten Gerätekonfiguration erzeugen.
6. IPAM- und Portplandaten bei Bedarf als CSV exportieren.

### 7. Verbindungen und Diagramm erstellen

1. Den Bereich **Diagramm** öffnen.
2. Quellgerät, Quellinterface, Zielgerät und Zielinterface auswählen.
3. Verbindungstyp und optionale Beschreibung festlegen.
4. Die Verbindung zum Projekt hinzufügen.
5. Geräte per Drag-and-drop positionieren oder automatisch anordnen lassen.
6. Router, Layer-3-Switches und Layer-2-Switches werden durch eigene Vektorsymbole dargestellt.
7. Das reine Netzwerkdiagramm kann als SVG exportiert werden.

Verbindungen werden ausschließlich im Bereich **Diagramm** erstellt und gepflegt. Der Netzplan übernimmt diese Daten automatisch.

### 8. Analyse durchführen

Im Bereich **Analyse** können unter anderem folgende Prüfungen ausgeführt werden:

- Abhängigkeits- und Konfliktprüfung
- Sicherheitsprüfung
- Konfigurationsvergleich
- Rollback-Entwurf
- globale Suche
- Cisco-Befehlsanalyse
- Erkennung unbekannter oder nicht zugeordneter Befehle

Erkannte Warnungen und Fehler sollten vor Export oder Übertragung geprüft und behoben werden.

### 9. Vorhandene Konfiguration importieren

1. Den Bereich **Import** öffnen.
2. Eine vorhandene Cisco-Konfiguration einfügen oder laden.
3. Die Analyse starten.
4. Erkannte Werte und Module prüfen.
5. Unbekannte Befehle manuell bewerten.
6. Übernommene Daten anschließend in der normalen Modulansicht kontrollieren.

### 10. SSH und Backups verwenden

1. Den Bereich **SSH** öffnen.
2. Host, Port, Benutzer und Authentifizierungsverfahren eintragen.
3. Die Verbindung testen.
4. Die aktuelle Konfiguration nur nach erfolgreicher Prüfung übertragen.
5. Vor Änderungen eine Running-Config oder Startup-Config sichern.
6. Backups können exportiert, verglichen und als Grundlage für einen Rollback-Entwurf verwendet werden.

SSH-Passwörter werden nicht in Projekt- oder AutoSave-Dateien gespeichert.

### 11. Netzplan exportieren

1. Projektinformationen, Geräte, IPAM-Daten und Verbindungen vollständig pflegen.
2. Den Unterbereich **Netzplan** im Bereich **Diagramm** öffnen.
3. Die Vorschau aktualisieren.
4. Den Netzplan als HTML, DOCX oder PDF exportieren.

Der Netzplan enthält abhängig vom vorhandenen Projektstand unter anderem:

- Projektinformationen
- grafische Topologie
- Geräteübersicht
- Verbindungen und Interfaces
- IP-Netze und VLANs
- Portinformationen
- statische Routen
- ACL-Zuordnungen
- VRFs
- Routingprotokolle
- Prüfungen und Testbefehle

### 12. Projekt speichern und fortsetzen

Projektänderungen sollten regelmäßig gespeichert werden. AutoSave und Wiederherstellung können zusätzlich über die Einstellungen konfiguriert werden. Projektdateien enthalten keine SSH-Passwörter, können jedoch technische Netzwerkdaten und Konfigurationen enthalten und sollten entsprechend geschützt werden.


## Unterstützte Gerätetypen

- Router
- Layer-3-Switch
- Layer-2-Switch
- Konfiguration ohne VRF
- Konfiguration mit VRF

Module und Eingabefelder werden abhängig von Gerätetyp und Konfigurationsmodus gefiltert.

## Hauptnavigation

### Konfiguration

- Übersicht
- Basis
- Management
- Interface / Ports
- Switching
- Routing
- Netzdienste
- Security/WAN

### Werkzeuge

- Subnetting
- Befehlsregister
- SSH
- Import
- Gegenstelle
- Ausgabe

### Dokumentation

- Projekt
- IPAM
- Analyse
- Diagramm

### System

- Einstellungen

## Konfigurationsmodule

### Basis

- Grunddaten und Hostname
- globale Geräteeinstellungen
- Banner

### Management

- SSH und Line VTY
- lokale Benutzer und Berechtigungsstufen
- AAA und Konsolenzugriff
- RADIUS und TACACS+
- NTP
- Syslog
- SNMP
- Logging

### Interface / Ports

- Einzelinterfaces
- Interface-Ranges
- Subinterfaces
- Router-on-a-Stick
- Interface-Profile
- Interface-Rollenmodell
- Trunks und Uplinks
- EtherChannel
- QinQ / 802.1ad
- grundlegende QoS-Konfiguration

### Switching

- VLANs und SVIs
- VLAN-/IP-Plan
- Access-Switch-Konfiguration
- Voice VLAN
- Spanning Tree
- Port-Security
- DHCP Snooping
- Dynamic ARP Inspection
- IP Source Guard
- Errdisable Recovery
- Switch-Hardening

### Routing

- allgemeine Routing-Einstellungen
- statische Routen
- OSPF
- OSPFv3
- EIGRP
- IS-IS
- BGP
- HSRP
- VRRP
- GLBP
- VRF-Lite
- VRF-spezifische statische Routen
- VRF-spezifisches OSPFv2 und OSPFv3
- VRF-spezifisches BGP
- Route-Maps
- Prefix-Lists
- IP SLA und Object Tracking
- MPLS, LDP und L3VPN

### Netzdienste

- IPv4- und IPv6-Konfiguration
- DHCP
- IPv4- und IPv6-ACLs
- ACL-Assistent
- OSPFv3
- IPv6-Routingprotokolle

### Security/WAN

- Hardening
- NAT und PAT
- GRE
- GRE over IPsec
- Site-to-Site-IPsec-VPN
- Zone-Based Firewall
- DMZ-Assistent
- WAN-Failover
- benutzerdefinierte Zusatzbefehle

## Konfigurationserzeugung

- modulbasierte Generierung von Cisco-Konfigurationen
- Live-Vorschau der aktiven Module
- Zusammenführung der aktiven Konfigurationsbereiche
- Prüfung auf doppelte oder widersprüchliche Befehle
- Text-Export aus dem Bereich „Ausgabe“
- Kopieren der erzeugten Konfiguration
- serielle Übertragung über COM / Konsole
- optionale Speicherung mit `write memory`

Die Funktionen „TXT Export“ und „Kopieren“ befinden sich ausschließlich im Bereich „Ausgabe“ und nicht mehr zusätzlich in der Kopfzeile.

## Mehrgeräte-Projekte

Projekte können mehrere Router und Switches enthalten. Gespeichert werden unter anderem:

- Projektinformationen
- Geräte und Gerätetypen
- aktive Module
- Eingabewerte
- erzeugte Konfigurationen
- IPAM-Einträge
- Verbindungen
- Diagrammpositionen
- Backups

Zusätzliche Projektinformationen:

- Projektname
- Projektnummer
- Organisation / Kunde
- Standort
- Projektleiter
- Bearbeiter
- Version
- Status
- Beschreibung

## IPAM und Portplanung

- zentrale IPv4- und IPv6-Netzverwaltung
- VLAN-Zuordnung
- Gateway-Dokumentation
- DHCP-Bereiche
- Geräte- und Interface-Zuordnung
- Import aus aktuellen und gespeicherten Konfigurationen
- Erkennung von Netzüberschneidungen
- Prüfung mehrfach verwendeter Gateways
- CSV-Export
- Interface- und Portplan aus Cisco-Konfigurationen
- Darstellung von Access-, Trunk- und Routed-Ports
- Voice VLAN, Native VLAN und erlaubte VLANs
- Port-Channel- und STP-Informationen

## Subnetting

Der integrierte Subnetzrechner unterstützt die Planung und Aufteilung von IPv4-Netzen. Ergebnisse können direkt für die weitere Konfiguration und Dokumentation verwendet werden.

## Befehlsregister und Befehlsanalyse

- Konfigurationsbefehle mit Parametern und Beschreibung
- Betriebs-, Show-, Test- und Clear-Befehle
- Suche nach Befehlen und Modulen
- Analyse einzelner Cisco-Befehlszeilen
- Erklärung von Befehlsbestandteilen und Parameterpositionen

## Import und Analyse

Vorhandene Cisco-Konfigurationen können importiert und analysiert werden. Die Anwendung erkennt bekannte Module, Felder und Konfigurationsbereiche und weist nicht zuordenbare Befehle separat aus.

Enthaltene Analysefunktionen:

- Abhängigkeitsprüfung
- Pflichtfeldprüfung
- Konfliktprüfung
- Sicherheitsprüfung
- globale Suche
- Konfigurationsvergleich
- Rollback-Entwurf
- Prüfung unbekannter Befehle

## Gegenstellenkonfiguration

Aus der aktuellen Konfiguration können Anforderungen und Konfigurationsentwürfe für die jeweilige Gegenstelle abgeleitet werden. Gegenstellen können anschließend als eigenes Projektgerät übernommen werden.

## SSH und Backups

- Verbindungstest
- Übertragung erzeugter Konfigurationen
- OpenSSH mit Schlüsseldatei
- Plink mit Passwort
- konfigurierbare Port- und Zeitwerte
- Running-Config-Backup
- Startup-Config-Backup
- Backup-Export
- Vergleich eines Backups mit einer neuen Konfiguration
- Verwendung eines Backups als Grundlage für einen Rollback-Entwurf

Passwörter werden nicht in Projekt- oder Autosave-Dateien gespeichert.

## Diagramm und Netzplan

Verbindungen werden im Bereich „Diagramm“ erstellt und gepflegt. Unterstützt werden unter anderem:

- Ethernet
- Access
- Trunk
- Port-Channel
- Routed Link
- WAN
- Tunnel
- Serial
- Fiber
- Wireless

Das Diagramm unterstützt:

- Drag-and-drop-Positionierung
- automatische Anordnung
- Interface-Beschriftungen
- Verbindungstypen
- frei definierbare Verbindungsbeschreibungen
- unterschiedliche Linienfarben und Linienarten
- SVG-Export

Gerätetypen werden mit eigenen Vektorsymbolen dargestellt:

- Router-Symbol mit vier Richtungsachsen
- Layer-3-Switch-Symbol mit horizontaler und vertikaler Weiterleitung
- Layer-2-Switch-Symbol mit Layer-2-Portdarstellung

Der Netzplan übernimmt die Verbindungen aus dem Diagramm und ergänzt technische Übersichten zu:

- Projektinformationen
- Geräten
- Verbindungen
- IP-Netzen
- VLANs
- Interfaces und Ports
- statischen Routen
- ACL-Zuordnungen
- VRFs
- Routingprotokollen
- Prüfergebnissen
- Testbefehlen

Exportformate:

- SVG für das Netzwerkdiagramm
- HTML-Netzplan mit eingebetteter Vektortopologie
- DOCX-Netzplan
- PDF-Netzplan

## Lokalisierung

- deutsche Benutzeroberfläche
- englische Benutzeroberfläche
- eingebettete JSON-Ressourcen
- lokalisierte Module
- lokalisierte Befehle
- lokalisierte Tooltips
- Fallback bei fehlenden Übersetzungen
- Sprachwechsel über die Einstellungen

## Bedienung und Datensicherheit

- Vorlagen speichern und laden
- AutoSave
- Wiederherstellung des letzten Projektstands
- konfigurierbare Startseite
- Such- und Filterfunktionen
- ausführliche mehrzeilige Tooltips
- Single-File-fähige Projektstruktur
- keine Speicherung von SSH-Passwörtern

## Projektdateien

Netzwerkprojekte werden als `.ciscoproject.json` gespeichert. Die Datei enthält die Projektstruktur, Geräte, Verbindungen, IPAM-Daten, Diagrammpositionen und technische Metadaten.

## Technische Grundlage

- C#
- .NET 8
- WPF
- eingebettete JSON-Kataloge
- System.IO.Ports
- native WPF-Vektorgrafiken
- HTML-, SVG-, DOCX- und PDF-Ausgabe

## Disclaimer

Der Cisco Konfigurator ist ein Hilfswerkzeug zur Planung, Erzeugung, Analyse und Dokumentation von Cisco-Konfigurationen. Er ersetzt keine fachliche Prüfung durch qualifizierte Netzwerkadministratoren.

- Erzeugte Konfigurationen sind vor dem Einsatz vollständig zu prüfen.
- Befehle können je nach Plattform, Gerätemodell, Lizenz, IOS-/IOS-XE-Version und aktiviertem Feature-Set abweichen oder nicht unterstützt werden.
- Konfigurationen sollten zuerst in einer Labor-, Test- oder Staging-Umgebung validiert werden.
- Vor produktiven Änderungen sind aktuelle Backups, ein Rollback-Plan und die geltenden Change-Prozesse sicherzustellen.
- Die automatische Analyse kann nicht alle Fehlkonfigurationen, Sicherheitsrisiken, Abhängigkeiten oder betrieblichen Auswirkungen erkennen.
- Zugangsdaten, Schlüssel, Projektdateien, Backups und exportierte Konfigurationen sind durch den Benutzer angemessen zu schützen.
- Der Benutzer ist für die Einhaltung interner Richtlinien, Datenschutzvorgaben, Sicherheitsanforderungen, Lizenzbedingungen und gesetzlicher Vorgaben verantwortlich.
- Die Nutzung der Software und der erzeugten Ergebnisse erfolgt auf eigenes Risiko. Für Ausfälle, Fehlkonfigurationen, Datenverlust, Sicherheitsvorfälle oder sonstige Schäden wird keine Gewähr oder Haftung übernommen, soweit gesetzlich zulässig.

Cisco, Cisco IOS, Cisco IOS XE und weitere Produktnamen sind Marken oder eingetragene Marken der Cisco Systems, Inc. Dieses Projekt ist nicht mit Cisco Systems, Inc. verbunden, wird nicht von Cisco unterstützt und stellt kein offizielles Cisco-Produkt dar.
