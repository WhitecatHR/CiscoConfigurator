using System.IO;

namespace CiscoConfigGuiWpf;

internal static class LocalizedCatalogLoader
{
    public static IReadOnlyList<T> LoadList<T>(string resourcePrefix)
    {
        var language = LocalizationService.CurrentLanguage;
        var candidates = new[]
        {
            $"{resourcePrefix}.{language}.json",
            $"{resourcePrefix}.de-DE.json",
            $"{resourcePrefix}.en-US.json"
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var file in candidates)
        {
            var loaded = EmbeddedJsonResourceLoader.Load<List<T>>(file);
            if (loaded is not { Count: > 0 }) continue;

            StartupDiagnostics.WriteInfo($"Loaded catalog '{file}' with {loaded.Count} entries.");
            return loaded;
        }

        StartupDiagnostics.WriteWarning($"No usable catalog found for prefix '{resourcePrefix}'.");
        return Array.Empty<T>();
    }
}
