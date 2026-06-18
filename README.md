# Cisco Configuration Tool

**Current version / Aktuelle Version:** `0.15-pre.Release`  
[Changelog](CHANGELOG.md)

[Deutsch](README.de.md) | [English](README.en.md)

Das Cisco Configuration Tool ist eine C#-WPF-Anwendung zur strukturierten Planung, Erzeugung, Prüfung und Dokumentation von Cisco-IOS-/IOS-XE-Konfigurationen.

- [Benutzung](README.de.md#benutzung)
- [Disclaimer](README.de.md#disclaimer)

The Cisco Configuration Tool is a C# WPF application for structured planning, generation, validation, and documentation of Cisco IOS / IOS-XE configurations.

- [Usage](README.en.md#usage)
- [Disclaimer](README.en.md#disclaimer)

- [Plugin-Dokumentation](Plugins/README.de.md)
- [Plugin documentation](Plugins/README.en.md)


## Technische Grundlage / Technical foundation

- C# / WPF auf .NET 8
- ausführbare Datei / executable: `Cisco Configuration Tool.exe`
- Self-Contained Single-File-Veröffentlichung / self-contained single-file publishing
- `PublishTrimmed=false`
- getrennte Workflow-Services für Konfiguration, Import, Projekte und AutoSave
