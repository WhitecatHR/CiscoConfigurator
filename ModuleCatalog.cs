namespace CiscoConfigGuiWpf;

/// <summary>
/// Loads module labels, help texts and field metadata from embedded language files.
/// Internal field names and technical values remain language-independent.
/// </summary>
public static class ModuleCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<ModuleDefinition>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ModuleDefinition> All
    {
        get
        {
            var language = LocalizationService.CurrentLanguage;
            if (Cache.TryGetValue(language, out var cached)) return cached;

            var builtIn = LocalizedCatalogLoader.LoadList<ModuleDefinition>("Modules");
            var pluginModules = PluginModuleService.LoadModules(language, builtIn);
            var loaded = builtIn.Concat(pluginModules).ToList();
            if (loaded.Count == 0)
            {
                StartupDiagnostics.WriteWarning($"Module catalog is empty for language '{language}'.");
            }
            else
            {
                StartupDiagnostics.WriteInfo($"Module catalog ready: {loaded.Count} modules for '{language}' ({pluginModules.Count} plugin modules).");
            }

            Cache[language] = loaded;
            return loaded;
        }
    }

    public static void ClearCache()
    {
        Cache.Clear();
        PluginModuleService.ClearCache();
    }
}
