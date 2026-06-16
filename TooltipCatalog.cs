using System.Collections.Concurrent;

namespace CiscoConfigGuiWpf;

public sealed class TooltipLanguageCatalog
{
    public Dictionary<string, string> Texts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ModuleHelp { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> CommandAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ParameterHelp { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Text(string key, string? fallback = null) =>
        Texts.TryGetValue(key, out var value) ? value : fallback ?? key;

    public string Parameter(string key) =>
        ParameterHelp.TryGetValue(key, out var value)
            ? value
            : ParameterHelp.TryGetValue("default", out var defaultValue) ? defaultValue : key;
}

public static class TooltipCatalog
{
    private static readonly ConcurrentDictionary<string, TooltipLanguageCatalog> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static TooltipLanguageCatalog Current => ForLanguage(LocalizationService.CurrentLanguage);

    public static TooltipLanguageCatalog ForLanguage(string? language)
    {
        var normalized = language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en-US" : "de-DE";
        return Cache.GetOrAdd(normalized, key =>
        {
            var catalog = EmbeddedJsonResourceLoader.Load<TooltipLanguageCatalog>($"Tooltips.{key}.json");
            if (catalog != null) return catalog;

            if (key != "de-DE")
                catalog = EmbeddedJsonResourceLoader.Load<TooltipLanguageCatalog>("Tooltips.de-DE.json");

            return catalog ?? new TooltipLanguageCatalog();
        });
    }
}
