namespace CiscoConfigGuiWpf;

internal static class LocalizationCatalogValidator
{
    public static void ValidateModuleCatalogs()
    {
        try
        {
            var german = EmbeddedJsonResourceLoader.Load<List<ModuleDefinition>>("Modules.de-DE.json") ?? new();
            var english = EmbeddedJsonResourceLoader.Load<List<ModuleDefinition>>("Modules.en-US.json") ?? new();
            var germanByName = german.Where(module => !string.IsNullOrWhiteSpace(module.Name))
                .ToDictionary(module => module.Name, StringComparer.OrdinalIgnoreCase);
            var englishByName = english.Where(module => !string.IsNullOrWhiteSpace(module.Name))
                .ToDictionary(module => module.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var missing in germanByName.Keys.Except(englishByName.Keys, StringComparer.OrdinalIgnoreCase))
                StartupDiagnostics.WriteWarning($"English module translation missing: {missing}.");
            foreach (var missing in englishByName.Keys.Except(germanByName.Keys, StringComparer.OrdinalIgnoreCase))
                StartupDiagnostics.WriteWarning($"German module translation missing: {missing}.");

            foreach (var name in germanByName.Keys.Intersect(englishByName.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var germanFields = germanByName[name].Fields.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var englishFields = englishByName[name].Fields.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var missing in germanFields.Except(englishFields, StringComparer.OrdinalIgnoreCase))
                    StartupDiagnostics.WriteWarning($"English field translation missing: {name}.{missing}.");
                foreach (var missing in englishFields.Except(germanFields, StringComparer.OrdinalIgnoreCase))
                    StartupDiagnostics.WriteWarning($"German field translation missing: {name}.{missing}.");
            }

            foreach (var finding in LocalizationService.ValidateEmbeddedCatalogs())
                StartupDiagnostics.WriteWarning(finding);

            StartupDiagnostics.WriteInfo($"Module localization validation completed: DE={german.Count}, EN={english.Count}.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteError("Module localization validation", ex);
        }
    }
}
