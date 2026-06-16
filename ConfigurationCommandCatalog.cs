namespace CiscoConfigGuiWpf;

/// <summary>
/// Configuration command catalog loaded from embedded language files.
/// The command itself is never translated.
/// </summary>
public static class ConfigurationCommandCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<CommandGroup>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CommandGroup> All
    {
        get
        {
            var language = LocalizationService.CurrentLanguage;
            if (Cache.TryGetValue(language, out var cached)) return cached;
            var loaded = LocalizedCatalogLoader.LoadList<CommandGroup>("Commands.Configuration");
            Cache[language] = loaded;
            return loaded;
        }
    }
}
