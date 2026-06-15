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

## Voraussetzungen

Für die Entwicklung und den Build wird das .NET 8 SDK unter Windows benötigt.

```powershell
dotnet restore
dotnet build -c Release
```

Single-File-Veröffentlichung:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-output
```

## Projektstatus

Das Projekt befindet sich in aktiver Entwicklung. Neue Cisco-Plattformen, Befehle und IOS-XE-Versionen können unterschiedliche Syntax oder Einschränkungen besitzen. Erzeugte Konfigurationen sollten deshalb vor dem Einsatz auf Produktivgeräten geprüft und zunächst in einer Testumgebung validiert werden.

## Hinweis

Dieses Projekt steht in keiner Verbindung zu Cisco Systems, Inc. Cisco, Cisco IOS und Cisco IOS XE sind Marken beziehungsweise eingetragene Marken ihrer jeweiligen Rechteinhaber.
