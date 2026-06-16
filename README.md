ENGLISH / ENGLISCH

# Cisco Configurator

Cisco Configurator is a multilingual Windows application for creating, importing, validating, analyzing, comparing, documenting, and deploying Cisco IOS and IOS-XE configurations.

The application supports routers, Layer 2 switches, and Layer 3 switches. Its modular WPF interface provides dedicated configuration areas for switching, routing, security, network services, device management, and documentation.

## Languages

The user interface supports:

* German
* English
* automatic system language detection

The language can be changed in the application settings. Cisco commands, protocol names, interface identifiers, and generated configurations remain unchanged.

## Main Features

* Modular Cisco configuration generation
* Router, Layer 2 switch, and Layer 3 switch profiles
* IPv4, IPv6, and dual-stack support
* VLAN, trunk, EtherChannel, and Spanning Tree configuration
* OSPF, OSPFv3, BGP, EIGRP, IS-IS, static routes, and VRF
* DHCP, ACL, NAT, NTP, SNMP, Syslog, SSH, AAA, and RADIUS
* HSRP, GRE, IPsec, MPLS, QoS, VoIP, and QinQ
* Full configuration import and command recognition
* Unknown-command detection
* Dependency and conflict validation
* Automatic correction suggestions
* Peer-device and counterpart configuration generation
* Multi-device project management
* IPv4 and IPv6 IP address management
* Interactive port planning
* Configuration comparison and rollback generation
* SSH deployment and device backup management
* Command registry with explanations and diagnostic commands
* Security analysis
* Interactive network diagrams
* HTML, DOCX, and PDF project reports
* Automatic saving and crash recovery
* Configurable themes, accent colors, font sizes, and export options

## Interactive Network Diagram

Devices can be positioned manually using drag and drop. Connections can display:

* connection type
* source and destination interfaces
* descriptions
* VLAN information
* IP addressing
* different colors and line styles

Device positions and connection information are stored inside the project.

## Settings

The central settings area includes:

* application language
* report language
* theme and accent color
* font size and compact mode
* default device and platform
* IPv4, IPv6, or dual-stack defaults
* configuration export options
* automatic saving and backups
* SSH connection settings
* diagram and report preferences
* JSON settings import and export

Application settings are stored locally under:

```text
%AppData%\CiscoKonfigurator\settings.json
```

## Technology

* C#
* .NET 8
* WPF
* Windows
* JSON-based project and settings storage

## Disclaimer

This project is an independent configuration and documentation tool. It is not affiliated with, endorsed by, or maintained by Cisco Systems, Inc.

Generated configurations should always be validated in a lab or test environment before being deployed to production devices.

___________________________________________________________________________________________________________

DEUTSCH / GERMAN

# Cisco Konfigurator

## Kurze Repository-Beschreibung

Ein modularer Cisco-IOS/IOS-XE-Konfigurator für Windows mit WPF-Oberfläche. Das Programm erstellt, importiert, analysiert und vergleicht Cisco-Konfigurationen für Router sowie Layer-2- und Layer-3-Switches und unterstützt Mehrgeräte-Projekte, IPAM, Validierung, Gegenstellen, Backups, SSH-Übertragung und Dokumentation.

## Projektübersicht

Der Cisco Konfigurator ist eine native Windows-Anwendung auf Basis von C# und WPF für die strukturierte Erstellung und Prüfung von Cisco-IOS- und IOS-XE-Konfigurationen. Die Oberfläche ordnet Funktionen nach Basis, Switching, Routing, Sicherheit, Diensten und Analyse. Eingaben werden validiert, in Cisco-Befehle umgesetzt und als vollständige Konfiguration oder modulbezogene Vorschau ausgegeben.

Das Projekt richtet sich an Netzwerkadministratoren, Auszubildende, Fachinformatiker und Lab-Umgebungen, in denen wiederholbare, nachvollziehbare und weitgehend fehlerfreie Cisco-Konfigurationen benötigt werden.

## Zentrale Funktionen

- Konfiguration von Cisco-Routern, Layer-2-Switches und Layer-3-Switches
- VLANs, Trunks, SVIs, Routed Ports und Router-on-a-Stick
- Spanning Tree mit PVST+, Rapid-PVST+ und MST
- EtherChannel, Port-Security, Voice-VLAN und QinQ
- OSPF, OSPFv3, EIGRP, BGP, IS-IS und statische Routen
- VRF, HSRP, DHCP, NAT/PAT, ACLs, SSH, AAA, RADIUS, NTP, SNMP und Syslog
- IPv4- und IPv6-Subnetzrechner sowie Einzel-IP-Prüfung
- Import und Analyse vorhandener Cisco-Konfigurationen
- Erkennung bekannter, unbekannter und kontextabhängiger Befehle
- Mehrgeräte-Projekte mit Gegenstellen- und Peer-Anforderungen
- IP-Adressverwaltung mit Überschneidungs- und Konfliktprüfung
- Konfigurationsvergleich und automatische Rollback-Erzeugung
- Grafischer Portplan und Netzwerkdiagramm
- Sicherheits-, Abhängigkeits- und Konsistenzprüfung
- Live-Vorschau je Modul und vollständige Gesamtausgabe
- SSH-Verbindung, Konfigurationsübertragung und Geräte-Backups
- Automatisches Speichern und Wiederherstellen des Projektzustands
- Globale Suche über Module, Felder und Cisco-Befehle
- Befehlsregister mit Erläuterungen und Parameteranalyse
- Export von Konfigurationen, Diagrammen und Projektberichten

## Technische Basis

- C#
- .NET 8
- WPF
- Native Windows-Anwendung
- Veröffentlichung als selbst enthaltene Single-File-EXE für `win-x64`
- `System.IO.Ports` für serielle Verbindungen

## Projektstatus

Das Projekt befindet sich in aktiver Entwicklung. Neue Cisco-Plattformen, Befehle und IOS-XE-Versionen können unterschiedliche Syntax oder Einschränkungen besitzen. Erzeugte Konfigurationen sollten deshalb vor dem Einsatz auf Produktivgeräten geprüft und zunächst in einer Testumgebung validiert werden.

## Hinweis

Dieses Projekt steht in keiner Verbindung zu Cisco Systems, Inc. Cisco, Cisco IOS und Cisco IOS XE sind Marken beziehungsweise eingetragene Marken ihrer jeweiligen Rechteinhaber.
