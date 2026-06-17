# Data-only module plugins

[Deutsch](README.de.md) | [English](README.en.md)

The `Plugins` directory can contain files ending in `*.ciscoplugin.json`. A plugin may add localized module definitions and Cisco command templates, but it cannot load executable .NET code.

## Search locations

- `Plugins` next to the application
- `%APPDATA%/CiscoKonfigurator/Plugins`

## Requirements

- The plugin ID must be unique.
- Module names must be unique and should use a plugin prefix, for example `example.customBanner`.
- Field names must not conflict with existing fields.
- Plugin modules must use one of the existing technical areas.
- German and English module definitions can be stored in the same manifest.
- Command templates use placeholders in the format `{fieldName}`.
- Required fields can be declared through `requiredFields`.

The example file deliberately uses the `.example` suffix and is therefore not loaded automatically.


## Plugin Manager

The Plugin Manager is available under **System → Settings → Plugin Manager**.

It can:

- display discovered plugins and their versions
- validate German and English localization definitions
- show module, field, and generator diagnostics
- enable or disable plugins
- open the user plugin directory
- restart the application in a controlled way after changes
- enable or disable validation during application startup

Activation changes are stored in application settings and take effect after the next restart. Invalid plugins are not loaded.
