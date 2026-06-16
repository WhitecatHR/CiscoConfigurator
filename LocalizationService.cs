using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CiscoConfigGuiWpf;

/// <summary>
/// Loads all user-facing texts from embedded JSON language resources.
/// No translation catalog is compiled into the C# source.
/// </summary>
public static class LocalizationService
{
    private const string DefaultLanguage = "de-DE";
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<string, string> TranslationCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> CatalogCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> ReverseCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> ExactModuleTranslationCache = new(StringComparer.OrdinalIgnoreCase);
    private static TranslationRuleSet? _rules;
    private static readonly IReadOnlyDictionary<string, string> EmergencyCatalog = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.startup_error_title"] = "Startup error",
        ["app.log_file"] = "Log file",
        ["text.cisco_konfigurator_konnte_nicht_gestartet_werden"] = "Cisco Configurator could not be started.",
        ["text.cisco_konfigurator_wurde_durch_einen_fehler_gestoppt"] = "Cisco Configurator encountered an error.",
        ["text.cisco_konfigurator_fehler"] = "Cisco Configurator error",
        ["common.apply"] = "Apply",
        ["common.save"] = "Save",
        ["common.cancel"] = "Cancel",
        ["navigation.settings"] = "Settings"
    };
    private static readonly string SystemLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : DefaultLanguage;
    private static string _language = DefaultLanguage;

    public static event EventHandler? LanguageChanged;

    public static string CurrentLanguage => _language;
    public static bool IsEnglish => _language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static void SetLanguage(string? language)
    {
        try
        {
            var normalized = NormalizeLanguage(language);
            var changed = !string.Equals(_language, normalized, StringComparison.OrdinalIgnoreCase);
            _language = normalized;

            try
            {
                var culture = CultureInfo.GetCultureInfo(_language);
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteError("Applying UI culture", ex);
                _language = DefaultLanguage;
            }

            ApplyApplicationResources();
            if (!changed) return;

            TranslationCache.Clear();
            NotifyLanguageChangedSafely();
        }
        catch (Exception ex)
        {
            _language = DefaultLanguage;
            TranslationCache.Clear();
            StartupDiagnostics.WriteError("Initializing localization", ex);
        }
    }

    private static void NotifyLanguageChangedSafely()
    {
        var handlers = LanguageChanged;
        if (handlers == null) return;

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteError("LanguageChanged event handler", ex);
            }
        }
    }

    public static string Get(string key) => Get(key, _language, null);

    public static string Get(string key, string fallback) => Get(key, _language, fallback);

    public static string Get(string key, string? language, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback ?? string.Empty;

        var normalizedLanguage = NormalizeLanguage(language);
        var catalog = GetCatalog(normalizedLanguage);
        if (catalog.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        // If a language-specific catalog is incomplete, try the German source catalog
        // before displaying any technical localization key in the user interface.
        if (!normalizedLanguage.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            var fallbackCatalog = GetCatalog(DefaultLanguage);
            if (fallbackCatalog.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        DeveloperDiagnosticsService.ReportMissingTranslation(key);
        return fallback ?? HumanizeLocalizationKey(key);
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string TranslateText(string? text) => TranslateText(text, _language);

    public static string TranslateText(string? text, string? targetLanguage)
    {
        var value = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return value;

        var language = NormalizeLanguage(targetLanguage);
        return TranslationCache.GetOrAdd("UI\u001f" + language + "\u001f" + value,
            _ => TranslateTextUncached(value, language));
    }

    public static string TranslateNaturalLanguageText(string? text) =>
        TranslateNaturalLanguageText(text, _language);

    public static string TranslateNaturalLanguageText(string? text, string? targetLanguage)
    {
        var value = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return value;

        var language = NormalizeLanguage(targetLanguage);
        return TranslationCache.GetOrAdd("NL\u001f" + language + "\u001f" + value, _ =>
        {
            if (TryTranslateExact(value, language, out var exact)) return exact;
            if (!language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return value;

            var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            var result = string.Join("\n", normalized.Split('\n').Select(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return line;
                var leading = line[..(line.Length - line.TrimStart().Length)];
                var trailing = line[(line.TrimEnd().Length)..];
                return leading + TranslateNaturalLanguage(line.Trim()) + trailing;
            }));

            if (LooksGerman(result)) DeveloperDiagnosticsService.ReportMissingTranslation(value);
            return result;
        });
    }

    public static DataTemplate CreateLocalizedStringTemplate()
    {
#pragma warning disable CS0618
        var factory = new FrameworkElementFactory(typeof(TextBlock));
#pragma warning restore CS0618
        factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        factory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetBinding(TextBlock.TextProperty, new Binding { Converter = new LocalizedTextConverter() });
        return new DataTemplate { VisualTree = factory };
    }

    public static IReadOnlyList<string> ValidateEmbeddedCatalogs()
    {
        var findings = new List<string>();
        var german = GetCatalog("de-DE");
        var english = GetCatalog("en-US");

        foreach (var key in german.Keys.Except(english.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            findings.Add($"Missing English localization key: {key}");
        foreach (var key in english.Keys.Except(german.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            findings.Add($"Missing German localization key: {key}");
        foreach (var pair in german.Where(x => string.IsNullOrWhiteSpace(x.Value)))
            findings.Add($"Empty German localization value: {pair.Key}");
        foreach (var pair in english.Where(x => string.IsNullOrWhiteSpace(x.Value)))
            findings.Add($"Empty English localization value: {pair.Key}");

        var germanTooltips = TooltipCatalog.ForLanguage("de-DE");
        var englishTooltips = TooltipCatalog.ForLanguage("en-US");
        CompareKeys("tooltip text", germanTooltips.Texts, englishTooltips.Texts, findings);
        CompareKeys("module tooltip", germanTooltips.ModuleHelp, englishTooltips.ModuleHelp, findings);
        CompareKeys("parameter tooltip", germanTooltips.ParameterHelp, englishTooltips.ParameterHelp, findings);
        CompareKeys("command alias", germanTooltips.CommandAliases, englishTooltips.CommandAliases, findings);

        return findings;
    }

    private static void CompareKeys<T>(
        string catalogName,
        IReadOnlyDictionary<string, T> german,
        IReadOnlyDictionary<string, T> english,
        ICollection<string> findings)
    {
        foreach (var key in german.Keys.Except(english.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            findings.Add($"Missing English {catalogName} key: {key}");
        foreach (var key in english.Keys.Except(german.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            findings.Add($"Missing German {catalogName} key: {key}");
    }

    public static bool LooksGerman(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Regex.IsMatch(value,
            @"[äöüÄÖÜß]|\b(der|die|das|den|dem|des|ein|eine|einen|einem|einer|keine|kein|und|oder|wird|werden|wurde|wurden|kann|können|soll|sollte|für|mit|ohne|über|unter|beim|vom|zur|zum|bitte|fehler|warnung|hinweis|gerät|geräte|projekt|konfiguration|einstellungen|ausgewählt|aktuell|eintrag|öffnen|speichern|prüfen|erzeugen|beschreibung|verbindung|netz|bereich|befehl|modul|feld|passwort|benutzer|sicherung|quelle|ziel)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string TranslateTextUncached(string value, string targetLanguage)
    {
        // Elements created before a catalog was available may temporarily contain
        // their localization key. Resolve it directly instead of showing text.xyz.
        if (LooksLikeLocalizationKey(value))
            return Get(value, targetLanguage, HumanizeLocalizationKey(value));

        if (TryTranslateExact(value, targetLanguage, out var exact)) return exact;

        var iconMatch = Regex.Match(value, @"^(?<icon>[^\p{L}\p{N}]{1,4}\s{1,3})(?<text>.+)$", RegexOptions.Singleline);
        if (iconMatch.Success)
            return iconMatch.Groups["icon"].Value + TranslateText(iconMatch.Groups["text"].Value, targetLanguage);

        if (!targetLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return value;

        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = string.Join("\n", normalized.Split('\n').Select(TranslateLine));
        if (LooksGerman(result)) DeveloperDiagnosticsService.ReportMissingTranslation(value);
        return result;
    }

    private static string TranslateLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;
        if (TryTranslateExact(line, "en-US", out var exact)) return exact;

        var leading = line[..(line.Length - line.TrimStart().Length)];
        var trailing = line[(line.TrimEnd().Length)..];
        var core = line.Trim();

        var iconMatch = Regex.Match(core, @"^(?<icon>[^\p{L}\p{N}]{1,4}\s*)(?<text>.+)$");
        var icon = string.Empty;
        if (iconMatch.Success)
        {
            icon = iconMatch.Groups["icon"].Value;
            core = iconMatch.Groups["text"].Value;
        }

        var separatorIndex = core.IndexOf(" — ", StringComparison.Ordinal);
        if (separatorIndex > 0 && LooksLikeCiscoCommand(core[..separatorIndex]))
        {
            var command = core[..separatorIndex];
            var meaning = core[(separatorIndex + 3)..];
            return leading + icon + command + " — " + TranslateNaturalLanguage(meaning) + trailing;
        }

        if (LooksLikeCiscoCommand(core))
            return leading + icon + TranslateCommandPlaceholders(core) + trailing;

        return leading + icon + TranslateNaturalLanguage(core) + trailing;
    }

    private static string TranslateCommandPlaceholders(string command) =>
        Regex.Replace(command, @"<(?<value>[^>]+)>", match =>
        {
            var source = match.Groups["value"].Value;
            var translated = TranslateNaturalLanguage(source).Replace(" ", "-");
            return "<" + translated + ">";
        });

    private static string TranslateNaturalLanguage(string value)
    {
        if (TryTranslateExact(value, "en-US", out var exact)) return exact;
        var result = value;
        var rules = GetRules();

        foreach (var replacement in rules.Phrases)
            result = ReplaceOrdinalIgnoreCase(result, replacement.Source, replacement.Target);

        foreach (var replacement in rules.Sentences)
        {
            try
            {
                result = Regex.Replace(result, replacement.Pattern, replacement.Replacement,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                DeveloperDiagnosticsService.Log("LOCALIZATION", $"Invalid regex '{replacement.Pattern}': {ex.Message}");
            }
        }

        foreach (var replacement in rules.Words)
            result = Regex.Replace(result, $@"\b{Regex.Escape(replacement.Key)}\b", replacement.Value,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return result;
    }

    private static bool TryTranslateExact(string value, string language, out string translated)
    {
        EnsureModuleTranslationIndex(language);
        if (ExactModuleTranslationCache.TryGetValue(language, out var moduleIndex) &&
            moduleIndex.TryGetValue(value, out var exactTranslation) &&
            exactTranslation is not null)
        {
            translated = exactTranslation;
            return true;
        }

        var reverse = GetReverseIndex(language);
        if (reverse.TryGetValue(value, out var key))
        {
            translated = Get(key, language, value);
            return true;
        }

        translated = value;
        return false;
    }

    private static IReadOnlyDictionary<string, string> GetCatalog(string language)
    {
        lock (Sync)
        {
            if (CatalogCache.TryGetValue(language, out var cached) && cached.Count > EmergencyCatalog.Count)
                return cached;

            // An emergency-only catalog may have been created before WPF resources
            // were fully available. Retry instead of keeping that incomplete state.
            CatalogCache.Remove(language);

            var file = $"Strings.{language}.json";
            var loaded = EmbeddedJsonResourceLoader.Load<Dictionary<string, string>>(file);

            if ((loaded == null || loaded.Count == 0) &&
                !language.Equals(DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                StartupDiagnostics.WriteWarning($"Localization catalog '{file}' is unavailable; using {DefaultLanguage}.");
                loaded = EmbeddedJsonResourceLoader.Load<Dictionary<string, string>>($"Strings.{DefaultLanguage}.json");
            }

            var catalog = loaded == null || loaded.Count == 0
                ? new Dictionary<string, string>(EmergencyCatalog, StringComparer.Ordinal)
                : new Dictionary<string, string>(loaded, StringComparer.Ordinal);

            foreach (var pair in EmergencyCatalog)
                catalog.TryAdd(pair.Key, pair.Value);

            CatalogCache[language] = catalog;
            return catalog;
        }
    }

    private static void EnsureModuleTranslationIndex(string targetLanguage)
    {
        lock (Sync)
        {
            if (ExactModuleTranslationCache.ContainsKey(targetLanguage)) return;

            var targetModules = EmbeddedJsonResourceLoader.Load<List<ModuleDefinition>>($"Modules.{targetLanguage}.json")
                                ?? new List<ModuleDefinition>();
            var targetByName = targetModules.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var index = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var sourceLanguage in new[] { "de-DE", "en-US" })
            {
                var sourceModules = EmbeddedJsonResourceLoader.Load<List<ModuleDefinition>>($"Modules.{sourceLanguage}.json")
                                    ?? new List<ModuleDefinition>();

                foreach (var sourceModule in sourceModules)
                {
                    if (!targetByName.TryGetValue(sourceModule.Name, out var targetModule)) continue;
                    if (!string.IsNullOrWhiteSpace(sourceModule.Title) && !string.IsNullOrWhiteSpace(targetModule.Title))
                        index[sourceModule.Title] = targetModule.Title;

                    var targetFields = targetModule.Fields.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
                    foreach (var sourceField in sourceModule.Fields)
                    {
                        if (!targetFields.TryGetValue(sourceField.Name, out var targetField)) continue;
                        if (!string.IsNullOrWhiteSpace(sourceField.Label) && !string.IsNullOrWhiteSpace(targetField.Label))
                            index[sourceField.Label] = targetField.Label;
                        if (!string.IsNullOrWhiteSpace(sourceField.Help) && !string.IsNullOrWhiteSpace(targetField.Help))
                            index[sourceField.Help] = targetField.Help;

                        var itemCount = Math.Min(sourceField.Items.Count, targetField.Items.Count);
                        for (var i = 0; i < itemCount; i++)
                        {
                            var sourceItem = sourceField.Items[i];
                            var targetItem = targetField.Items[i];
                            if (!string.IsNullOrWhiteSpace(sourceItem) && !string.IsNullOrWhiteSpace(targetItem))
                                index[sourceItem] = targetItem;
                        }
                    }
                }
            }

            ExactModuleTranslationCache[targetLanguage] = index;
            StartupDiagnostics.WriteInfo($"Module translation index ready: {index.Count} entries for '{targetLanguage}'.");
        }
    }

    private static IReadOnlyDictionary<string, string> GetReverseIndex(string targetLanguage)
    {
        lock (Sync)
        {
            if (ReverseCache.TryGetValue(targetLanguage, out var cached)) return cached;

            var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var language in new[] { "de-DE", "en-US" })
            {
                foreach (var pair in GetCatalog(language))
                {
                    if (!string.IsNullOrEmpty(pair.Value)) reverse.TryAdd(pair.Value, pair.Key);
                }
            }

            EnsureModuleTranslationIndex(targetLanguage);

            ReverseCache[targetLanguage] = reverse;
            return reverse;
        }
    }

    private static TranslationRuleSet GetRules()
    {
        lock (Sync)
        {
            return _rules ??= EmbeddedJsonResourceLoader.Load<TranslationRuleSet>("TranslationRules.en-US.json")
                              ?? new TranslationRuleSet();
        }
    }

    private static void ApplyApplicationResources()
    {
        try
        {
            if (Application.Current?.Resources == null) return;
            var catalog = GetCatalog(_language);
            foreach (var pair in catalog)
                Application.Current.Resources["Loc." + pair.Key] = pair.Value;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteError("Applying localization resources", ex);
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        var requested = (language ?? "system").Trim();
        if (requested.Equals("system", StringComparison.OrdinalIgnoreCase) ||
            requested.Equals("Systemsprache", StringComparison.OrdinalIgnoreCase))
        {
            requested = SystemLanguage;
        }

        return requested.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : DefaultLanguage;
    }


    private static bool LooksLikeLocalizationKey(string value) =>
        Regex.IsMatch(value.Trim(),
            @"^(app|header|common|navigation|settings|text|status|tab|import|ipam|diagram|report|validation|security|command)\.[a-z0-9_.-]+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string HumanizeLocalizationKey(string key)
    {
        var value = key.Trim();
        var separator = value.IndexOf('.');
        if (separator >= 0 && separator + 1 < value.Length)
            value = value[(separator + 1)..];

        value = value.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ');
        value = Regex.Replace(value, @"\s+", " ").Trim();
        if (value.Length == 0) return key;

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string ReplaceOrdinalIgnoreCase(string input, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue)) return input;
        var escaped = Regex.Escape(oldValue);
        var pattern = Regex.IsMatch(oldValue, @"^[\p{L}\p{N}]+$", RegexOptions.CultureInvariant)
            ? $@"\b{escaped}\b"
            : escaped;
        return Regex.Replace(input, pattern, _ => newValue,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeCiscoCommand(string value)
    {
        var text = value.Trim().TrimStart('•', '-', '*').Trim();
        if (text.Length == 0) return false;
        if (Regex.IsMatch(text, @"^<[^>]+>\s*=", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(text, @"[.!?]$", RegexOptions.CultureInvariant) && LooksGerman(text)) return false;

        return Regex.IsMatch(text,
            @"^(enable|configure terminal|conf t|hostname|ip\s|ipv6\s|router\s|interface\s|line\s|aaa\s|crypto\s|service\s|security\s|no\s|show\s|clear\s|debug\s|spanning-tree\s|switchport\s|vlan\s|clock\s|ntp\s|snmp-server\s|logging\s|username\s|banner\s|transport\s|login(?:\s|$)|exec-timeout\s|description\s|network\s|neighbor\s|address-family\s|redistribute\s|route-map\s|set\s|match\s|track\s|standby\s|channel-group\s|encapsulation\s|mpls\s|tunnel\s|access-list\s|permit\s|deny\s|default-interface\s|default\s|copy\s|write(?:\s|$)|end$|exit$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

public sealed class TranslationRuleSet
{
    public List<PhraseReplacement> Phrases { get; set; } = new();
    public List<SentenceReplacement> Sentences { get; set; } = new();
    public Dictionary<string, string> Words { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PhraseReplacement
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}

public sealed class SentenceReplacement
{
    public string Pattern { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
}

public sealed record LocalizationSource(string Text, string? Key = null);

public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        LocalizationService.TranslateText(value?.ToString() ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class CommandDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        LocalizationService.TranslateNaturalLanguageText(value?.ToString() ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
