using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public sealed class ModulePluginManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public Dictionary<string, List<ModuleDefinition>> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PluginCommandTemplate> Generators { get; set; } = new();
}

public sealed class PluginCommandTemplate
{
    public string Module { get; set; } = "";
    public string Section { get; set; } = "PLUGIN";
    public List<string> Commands { get; set; } = new();
    public List<string> RequiredFields { get; set; } = new();
    public string WhenField { get; set; } = "";
    public List<string> WhenValues { get; set; } = new();
}

public sealed record PluginGeneratedSection(string Section, IReadOnlyList<string> Lines);

public sealed class PluginStatusInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool Enabled { get; init; }
    public bool Valid { get; init; }
    public int ModuleCount { get; init; }
    public int GeneratorCount { get; init; }
    public string Languages { get; init; } = "";
    public DateTime LastWriteUtc { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public string StateCode => !Valid ? "Invalid" : Enabled ? "Enabled" : "Disabled";
    public string DisplayState => !Valid ? LocalizationService.Get("plugins.state_invalid", "Ungültig") : Enabled ? LocalizationService.Get("plugins.state_enabled", "Aktiv") : LocalizationService.Get("plugins.state_disabled", "Deaktiviert");
    public string DiagnosticSummary => Diagnostics.Count == 0 ? "OK" : string.Join(" | ", Diagnostics);
}

/// <summary>
/// Data-only plugin loader. Plugins may add module definitions and command templates,
/// but cannot execute arbitrary .NET code. This keeps the plugin boundary predictable
/// for the single-file desktop application.
/// </summary>
public static class PluginModuleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string[] AllowedTabs =
    {
        "Basis", "Management", "Interfaces", "Switching", "Routing", "IPv6/DHCP/ACL", "Security/WAN"
    };

    private sealed class ScannedPlugin
    {
        public required string FilePath { get; init; }
        public ModulePluginManifest? Manifest { get; init; }
        public required PluginStatusInfo Status { get; init; }
    }

    private static IReadOnlyList<ScannedPlugin>? _scanCache;

    public static IReadOnlyList<ModuleDefinition> LoadModules(string language, IEnumerable<ModuleDefinition> existingModules)
    {
        var builtIn = existingModules.ToList();
        var existing = new HashSet<string>(builtIn.Select(module => module.Name), StringComparer.OrdinalIgnoreCase);
        var fieldNames = new HashSet<string>(builtIn.SelectMany(module => module.Fields ?? new List<FieldDefinition>()).Select(field => field.Name), StringComparer.OrdinalIgnoreCase);
        var result = new List<ModuleDefinition>();

        foreach (var manifest in LoadEnabledManifests())
        {
            var localizedModules = SelectLocalizedModules(manifest, language);
            foreach (var module in localizedModules)
            {
                module.Fields ??= new List<FieldDefinition>();
                module.Devices ??= new List<string>();
                if (string.IsNullOrWhiteSpace(module.Name) || string.IsNullOrWhiteSpace(module.Title))
                {
                    StartupDiagnostics.WriteWarning($"Plugin '{manifest.Id}' contains a module without name or title.");
                    continue;
                }
                if (!AllowedTabs.Contains(module.Tab, StringComparer.OrdinalIgnoreCase))
                {
                    StartupDiagnostics.WriteWarning($"Plugin module '{module.Name}' uses unsupported tab '{module.Tab}'.");
                    continue;
                }
                if (!existing.Add(module.Name))
                {
                    StartupDiagnostics.WriteWarning($"Plugin module '{module.Name}' conflicts with an existing module and was skipped.");
                    continue;
                }
                var duplicateField = module.Fields.Select(field => field.Name)
                    .FirstOrDefault(fieldName => string.IsNullOrWhiteSpace(fieldName) || fieldNames.Contains(fieldName));
                if (duplicateField != null)
                {
                    StartupDiagnostics.WriteWarning($"Plugin module '{module.Name}' contains an empty or conflicting field name '{duplicateField}' and was skipped.");
                    existing.Remove(module.Name);
                    continue;
                }
                foreach (var field in module.Fields) fieldNames.Add(field.Name);

                result.Add(module);
                StartupDiagnostics.WriteInfo($"Loaded plugin module '{module.Name}' from '{manifest.Id}' ({manifest.Version}).");
            }
        }

        return result;
    }

    public static IReadOnlyList<PluginGeneratedSection> Generate(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, bool> modules)
    {
        var result = new List<PluginGeneratedSection>();
        foreach (var manifest in LoadEnabledManifests())
        {
            foreach (var generator in manifest.Generators ?? new List<PluginCommandTemplate>())
            {
                generator.Commands ??= new List<string>();
                generator.RequiredFields ??= new List<string>();
                generator.WhenValues ??= new List<string>();
                if (string.IsNullOrWhiteSpace(generator.Module) ||
                    !modules.TryGetValue(generator.Module, out var active) || !active)
                    continue;

                if (generator.RequiredFields.Any(field =>
                        !values.TryGetValue(field, out var requiredValue) || string.IsNullOrWhiteSpace(requiredValue)))
                {
                    StartupDiagnostics.WriteWarning($"Plugin generator '{generator.Module}' skipped because required fields are empty.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(generator.WhenField))
                {
                    values.TryGetValue(generator.WhenField, out var conditionValue);
                    if (generator.WhenValues.Count > 0 &&
                        !generator.WhenValues.Contains(conditionValue ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                        continue;
                }

                var lines = generator.Commands
                    .Select(command => Expand(command, values))
                    .Where(command => !string.IsNullOrWhiteSpace(command))
                    .ToList();
                if (lines.Count == 0) continue;

                var section = string.IsNullOrWhiteSpace(generator.Section)
                    ? $"PLUGIN: {manifest.Name}"
                    : generator.Section;
                result.Add(new PluginGeneratedSection(section, lines));
            }
        }
        return result;
    }

    public static IReadOnlyList<PluginStatusInfo> GetPluginStatuses(bool forceRefresh = false)
    {
        if (forceRefresh) ClearCache();
        return ScanPlugins().Select(x => x.Status).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool SetPluginEnabled(string pluginId, bool enabled, out string message)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            message = LocalizationService.Get("plugins.error_missing_id", "Plugin-ID fehlt.");
            return false;
        }
        var status = GetPluginStatuses().FirstOrDefault(x => x.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (status == null)
        {
            message = LocalizationService.Format("plugins.error_not_found", pluginId);
            return false;
        }
        if (!status.Valid && enabled)
        {
            message = LocalizationService.Format("plugins.error_invalid", pluginId);
            return false;
        }

        var settings = ApplicationSettingsService.Current;
        settings.DisabledPluginIds ??= new List<string>();
        settings.DisabledPluginIds.RemoveAll(x => x.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (!enabled) settings.DisabledPluginIds.Add(pluginId);
        ApplicationSettingsService.Save(settings);
        ModuleCatalog.ClearCache();
        message = enabled
            ? LocalizationService.Format("plugins.enabled_message", pluginId)
            : LocalizationService.Format("plugins.disabled_message", pluginId);
        return true;
    }

    public static string BuildDiagnostics(PluginStatusInfo status, bool english)
    {
        string R(string de, string en) => english ? en : de;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{R("Plugin", "Plugin")}: {status.Name}");
        sb.AppendLine($"ID: {status.Id}");
        sb.AppendLine($"{R("Version", "Version")}: {status.Version}");
        sb.AppendLine($"{R("Datei", "File")}: {status.FilePath}");
        sb.AppendLine($"{R("Status", "Status")}: {status.StateCode}");
        sb.AppendLine($"{R("Module", "Modules")}: {status.ModuleCount}");
        sb.AppendLine($"{R("Generatoren", "Generators")}: {status.GeneratorCount}");
        sb.AppendLine($"{R("Sprachen", "Languages")}: {status.Languages}");
        sb.AppendLine();
        sb.AppendLine(R("Diagnose", "Diagnostics"));
        if (status.Diagnostics.Count == 0) sb.AppendLine(R("Keine Fehler gefunden.", "No errors found."));
        else foreach (var item in status.Diagnostics) sb.AppendLine($"- {LocalizeDiagnostic(item, english)}");
        return sb.ToString();
    }

    public static void ClearCache() => _scanCache = null;

    public static IReadOnlyList<string> GetPluginDirectories() => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "Plugins"),
        Path.Combine(ApplicationSettingsService.SettingsDirectory, "Plugins")
    };

    private static IReadOnlyList<ModulePluginManifest> LoadEnabledManifests() =>
        ScanPlugins().Where(x => x.Status.Valid && x.Status.Enabled && x.Manifest != null).Select(x => x.Manifest!).ToList();

    private static IReadOnlyList<ScannedPlugin> ScanPlugins()
    {
        if (_scanCache != null) return _scanCache;

        var scanned = new List<ScannedPlugin>();
        var disabled = new HashSet<string>(ApplicationSettingsService.Current.DisabledPluginIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var directory in GetPluginDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.ciscoplugin.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteWarning($"Plugin directory '{directory}' could not be scanned: {ex.Message}");
                continue;
            }

            foreach (var file in files)
                scanned.Add(ScanFile(file, disabled));
        }

        foreach (var duplicateGroup in scanned.Where(x => x.Manifest != null && !string.IsNullOrWhiteSpace(x.Manifest.Id))
                     .GroupBy(x => x.Manifest!.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
        {
            foreach (var item in duplicateGroup.ToList())
            {
                var diagnostics = item.Status.Diagnostics.Concat(new[] { $"Duplicate plugin id '{duplicateGroup.Key}'." }).ToList();
                var invalid = CloneStatus(item.Status, valid: false, diagnostics: diagnostics);
                var index = scanned.IndexOf(item);
                scanned[index] = new ScannedPlugin { FilePath = item.FilePath, Manifest = item.Manifest, Status = invalid };
            }
        }

        foreach (var item in scanned)
        {
            if (item.Status.Valid)
                StartupDiagnostics.WriteInfo($"Plugin manifest discovered: {item.Status.Id} {item.Status.Version} ({item.Status.StateCode}).");
            else
                StartupDiagnostics.WriteWarning($"Plugin manifest invalid: {item.FilePath}: {item.Status.DiagnosticSummary}");
        }

        _scanCache = scanned;
        return _scanCache;
    }

    private static ScannedPlugin ScanFile(string file, HashSet<string> disabled)
    {
        var diagnostics = new List<string>();
        ModulePluginManifest? manifest = null;
        try
        {
            manifest = JsonSerializer.Deserialize<ModulePluginManifest>(File.ReadAllText(file), JsonOptions);
            if (manifest == null)
            {
                diagnostics.Add("Manifest is empty.");
            }
            else
            {
                NormalizeManifest(manifest);
                ValidateManifest(manifest, diagnostics);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(ex.Message);
        }

        var id = manifest?.Id?.Trim() ?? string.Empty;
        var status = new PluginStatusInfo
        {
            Id = string.IsNullOrWhiteSpace(id) ? Path.GetFileNameWithoutExtension(file) : id,
            Name = string.IsNullOrWhiteSpace(manifest?.Name) ? Path.GetFileName(file) : manifest!.Name.Trim(),
            Version = manifest?.Version?.Trim() ?? string.Empty,
            FilePath = file,
            Enabled = !string.IsNullOrWhiteSpace(id) && !disabled.Contains(id),
            Valid = manifest != null && diagnostics.Count == 0,
            ModuleCount = manifest?.Modules?.Values.SelectMany(x => x).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0,
            GeneratorCount = manifest?.Generators?.Count ?? 0,
            Languages = manifest?.Modules == null ? string.Empty : string.Join(", ", manifest.Modules.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            LastWriteUtc = File.GetLastWriteTimeUtc(file),
            Diagnostics = diagnostics
        };
        return new ScannedPlugin { FilePath = file, Manifest = manifest, Status = status };
    }

    private static void NormalizeManifest(ModulePluginManifest manifest)
    {
        manifest.Modules ??= new Dictionary<string, List<ModuleDefinition>>(StringComparer.OrdinalIgnoreCase);
        manifest.Generators ??= new List<PluginCommandTemplate>();
        foreach (var modules in manifest.Modules.Values)
        foreach (var module in modules)
        {
            module.Fields ??= new List<FieldDefinition>();
            module.Devices ??= new List<string>();
        }
        foreach (var generator in manifest.Generators)
        {
            generator.Commands ??= new List<string>();
            generator.RequiredFields ??= new List<string>();
            generator.WhenValues ??= new List<string>();
        }
    }

    private static void ValidateManifest(ModulePluginManifest manifest, List<string> diagnostics)
    {
        if (manifest.FormatVersion != 1) diagnostics.Add($"Unsupported format version: {manifest.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.Id)) diagnostics.Add("Plugin id is required.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) diagnostics.Add("Plugin name is required.");
        if (!manifest.Modules.ContainsKey("de-DE")) diagnostics.Add("German module localization (de-DE) is missing.");
        if (!manifest.Modules.ContainsKey("en-US")) diagnostics.Add("English module localization (en-US) is missing.");

        var allModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (language, modules) in manifest.Modules)
        {
            var languageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules)
            {
                if (string.IsNullOrWhiteSpace(module.Name)) diagnostics.Add($"{language}: module name is missing.");
                else if (!languageNames.Add(module.Name)) diagnostics.Add($"{language}: duplicate module '{module.Name}'.");
                if (string.IsNullOrWhiteSpace(module.Title)) diagnostics.Add($"{language}: module '{module.Name}' has no title.");
                if (!AllowedTabs.Contains(module.Tab, StringComparer.OrdinalIgnoreCase)) diagnostics.Add($"{language}: module '{module.Name}' uses unsupported tab '{module.Tab}'.");
                var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in module.Fields ?? new List<FieldDefinition>())
                {
                    if (string.IsNullOrWhiteSpace(field.Name)) diagnostics.Add($"{language}: module '{module.Name}' contains a field without name.");
                    else if (!fieldNames.Add(field.Name)) diagnostics.Add($"{language}: module '{module.Name}' contains duplicate field '{field.Name}'.");
                    if (string.IsNullOrWhiteSpace(field.Label)) diagnostics.Add($"{language}: field '{field.Name}' has no label.");
                    if (!string.IsNullOrWhiteSpace(field.Name)) allFieldNames.Add(field.Name);
                }
                if (!string.IsNullOrWhiteSpace(module.Name)) allModuleNames.Add(module.Name);
            }
        }

        if (manifest.Modules.TryGetValue("de-DE", out var german) && manifest.Modules.TryGetValue("en-US", out var english))
        {
            var deMap = german.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var enMap = english.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var name in deMap.Keys.Except(enMap.Keys, StringComparer.OrdinalIgnoreCase)) diagnostics.Add($"English module '{name}' is missing.");
            foreach (var name in enMap.Keys.Except(deMap.Keys, StringComparer.OrdinalIgnoreCase)) diagnostics.Add($"German module '{name}' is missing.");
            foreach (var name in deMap.Keys.Intersect(enMap.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var deFields = deMap[name].Fields.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var enFields = enMap[name].Fields.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!deFields.SetEquals(enFields)) diagnostics.Add($"Module '{name}' has different German and English field structures.");
            }
        }

        try
        {
            var builtInModules = LocalizedCatalogLoader.LoadList<ModuleDefinition>("Modules");
            var builtInModuleNames = builtInModules.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var builtInFieldNames = builtInModules.SelectMany(x => x.Fields ?? new List<FieldDefinition>()).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var moduleName in allModuleNames.Where(builtInModuleNames.Contains))
                diagnostics.Add($"Plugin module '{moduleName}' conflicts with a built-in module.");
            foreach (var fieldName in allFieldNames.Where(builtInFieldNames.Contains))
                diagnostics.Add($"Plugin field '{fieldName}' conflicts with a built-in field.");
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Built-in module conflict validation failed: {ex.Message}");
        }

        foreach (var generator in manifest.Generators)
        {
            if (string.IsNullOrWhiteSpace(generator.Module)) diagnostics.Add("Generator module is missing.");
            else if (!allModuleNames.Contains(generator.Module)) diagnostics.Add($"Generator references unknown module '{generator.Module}'.");
            if (generator.Commands.Count == 0) diagnostics.Add($"Generator '{generator.Module}' contains no commands.");

            foreach (var requiredField in generator.RequiredFields.Where(field => !allFieldNames.Contains(field)))
                diagnostics.Add($"Generator '{generator.Module}' references unknown required field '{requiredField}'.");
            if (!string.IsNullOrWhiteSpace(generator.WhenField) && !allFieldNames.Contains(generator.WhenField))
                diagnostics.Add($"Generator '{generator.Module}' references unknown condition field '{generator.WhenField}'.");
            foreach (var placeholder in generator.Commands
                         .SelectMany(command => Regex.Matches(command ?? string.Empty, @"\{([A-Za-z0-9_.-]+)\}").Cast<Match>().Select(match => match.Groups[1].Value))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(field => !allFieldNames.Contains(field)))
                diagnostics.Add($"Generator '{generator.Module}' references unknown placeholder '{placeholder}'.");
        }
    }

    private static string LocalizeDiagnostic(string diagnostic, bool english)
    {
        if (english || string.IsNullOrWhiteSpace(diagnostic)) return diagnostic;

        var mappings = new (string Pattern, string Replacement)[]
        {
            (@"^Manifest is empty\.$", "Das Manifest ist leer."),
            (@"^Unsupported format version: (.+)\.$", "Nicht unterstützte Formatversion: $1."),
            (@"^Plugin id is required\.$", "Eine Plugin-ID ist erforderlich."),
            (@"^Plugin name is required\.$", "Ein Plugin-Name ist erforderlich."),
            (@"^German module localization \(de-DE\) is missing\.$", "Die deutsche Modullokalisierung (de-DE) fehlt."),
            (@"^English module localization \(en-US\) is missing\.$", "Die englische Modullokalisierung (en-US) fehlt."),
            (@"^Duplicate plugin id '(.+)'\.$", "Doppelte Plugin-ID '$1'."),
            (@"^(.+): module name is missing\.$", "$1: Der Modulname fehlt."),
            (@"^(.+): duplicate module '(.+)'\.$", "$1: Doppeltes Modul '$2'."),
            (@"^(.+): module '(.+)' has no title\.$", "$1: Das Modul '$2' besitzt keinen Titel."),
            (@"^(.+): module '(.+)' uses unsupported tab '(.+)'\.$", "$1: Das Modul '$2' verwendet den nicht unterstützten Bereich '$3'."),
            (@"^(.+): module '(.+)' contains a field without name\.$", "$1: Das Modul '$2' enthält ein Feld ohne Namen."),
            (@"^(.+): module '(.+)' contains duplicate field '(.+)'\.$", "$1: Das Modul '$2' enthält das Feld '$3' mehrfach."),
            (@"^(.+): field '(.+)' has no label\.$", "$1: Das Feld '$2' besitzt keine Bezeichnung."),
            (@"^English module '(.+)' is missing\.$", "Das englische Modul '$1' fehlt."),
            (@"^German module '(.+)' is missing\.$", "Das deutsche Modul '$1' fehlt."),
            (@"^Module '(.+)' has different German and English field structures\.$", "Das Modul '$1' besitzt unterschiedliche deutsche und englische Feldstrukturen."),
            (@"^Plugin module '(.+)' conflicts with a built-in module\.$", "Das Plugin-Modul '$1' kollidiert mit einem integrierten Modul."),
            (@"^Plugin field '(.+)' conflicts with a built-in field\.$", "Das Plugin-Feld '$1' kollidiert mit einem integrierten Feld."),
            (@"^Built-in module conflict validation failed: (.+)$", "Die Konfliktprüfung mit integrierten Modulen ist fehlgeschlagen: $1"),
            (@"^Generator module is missing\.$", "Die Modulzuordnung des Generators fehlt."),
            (@"^Generator references unknown module '(.+)'\.$", "Der Generator verweist auf das unbekannte Modul '$1'."),
            (@"^Generator '(.+)' contains no commands\.$", "Der Generator '$1' enthält keine Befehle."),
            (@"^Generator '(.+)' references unknown required field '(.+)'\.$", "Der Generator '$1' verweist auf das unbekannte Pflichtfeld '$2'."),
            (@"^Generator '(.+)' references unknown condition field '(.+)'\.$", "Der Generator '$1' verweist auf das unbekannte Bedingungsfeld '$2'."),
            (@"^Generator '(.+)' references unknown placeholder '(.+)'\.$", "Der Generator '$1' verweist auf den unbekannten Platzhalter '$2'.")
        };

        foreach (var (pattern, replacement) in mappings)
            if (Regex.IsMatch(diagnostic, pattern, RegexOptions.CultureInvariant))
                return Regex.Replace(diagnostic, pattern, replacement, RegexOptions.CultureInvariant);
        return diagnostic;
    }

    private static PluginStatusInfo CloneStatus(PluginStatusInfo source, bool valid, IReadOnlyList<string> diagnostics) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Version = source.Version,
        FilePath = source.FilePath,
        Enabled = source.Enabled,
        Valid = valid,
        ModuleCount = source.ModuleCount,
        GeneratorCount = source.GeneratorCount,
        Languages = source.Languages,
        LastWriteUtc = source.LastWriteUtc,
        Diagnostics = diagnostics
    };

    private static IReadOnlyList<ModuleDefinition> SelectLocalizedModules(ModulePluginManifest manifest, string language)
    {
        if (manifest.Modules.TryGetValue(language, out var exact) && exact.Count > 0) return exact;
        if (manifest.Modules.TryGetValue("de-DE", out var german) && german.Count > 0) return german;
        if (manifest.Modules.TryGetValue("en-US", out var english) && english.Count > 0) return english;
        return manifest.Modules.Values.FirstOrDefault(list => list.Count > 0) ?? new List<ModuleDefinition>();
    }

    private static string Expand(string command, IReadOnlyDictionary<string, string> values)
    {
        return Regex.Replace(command ?? string.Empty, @"\{([A-Za-z0-9_.-]+)\}", match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        }).TrimEnd();
    }
}
