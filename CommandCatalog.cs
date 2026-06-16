namespace CiscoConfigGuiWpf;

/// <summary>
/// Operational and diagnostic command catalog loaded from embedded language files.
/// Cisco command syntax remains unchanged; only names and descriptions are localized.
/// </summary>
public static class CommandCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<CommandGroup>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<CommandGroup> Groups => All;

    public static IReadOnlyList<CommandGroup> All
    {
        get
        {
            var language = LocalizationService.CurrentLanguage;
            if (Cache.TryGetValue(language, out var cached)) return cached;
            var loaded = LocalizedCatalogLoader.LoadList<CommandGroup>("Commands.Operational");
            Cache[language] = loaded;
            return loaded;
        }
    }
}
