using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace CiscoConfigGuiWpf;

/// <summary>
/// Loads JSON catalogs embedded as WPF resources. A manifest-resource path and
/// external development fallback are retained for compatibility with older builds.
/// </summary>
internal static class EmbeddedJsonResourceLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T? Load<T>(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return default;

        try
        {
            if (TryOpenWpfResource(fileName, out var wpfStream, out var wpfName))
            {
                using (wpfStream)
                {
                    var result = JsonSerializer.Deserialize<T>(wpfStream, Options);
                    if (result == null)
                        StartupDiagnostics.WriteWarning($"WPF resource returned no data: {wpfName}");
                    else
                        StartupDiagnostics.WriteInfo($"Loaded localization resource: {wpfName}");
                    return result;
                }
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = FindManifestResourceName(assembly, fileName);
            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    var result = JsonSerializer.Deserialize<T>(stream, Options);
                    if (result == null)
                        StartupDiagnostics.WriteWarning($"Manifest resource returned no data: {resourceName}");
                    else
                        StartupDiagnostics.WriteInfo($"Loaded manifest localization resource: {resourceName}");
                    return result;
                }

                StartupDiagnostics.WriteWarning($"Manifest resource stream could not be opened: {resourceName}");
            }

            StartupDiagnostics.WriteWarning(
                $"Embedded localization resource was not found: {fileName}. " +
                $"Manifest resources: {string.Join(", ", assembly.GetManifestResourceNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}");

            return TryLoadExternal<T>(fileName);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteError($"Loading localization resource '{fileName}'", ex);
            return TryLoadExternal<T>(fileName);
        }
    }

    private static bool TryOpenWpfResource(string fileName, out Stream stream, out string resourceName)
    {
        stream = Stream.Null;
        resourceName = string.Empty;

        try
        {
            // Relative application resource URI works for normal builds and single-file publish.
            var normalized = fileName.Replace('\\', '/').TrimStart('/');
            var candidates = new[]
            {
                $"Localization/{normalized}",
                $"/Localization/{normalized}",
                $"pack://application:,,,/Localization/{normalized}"
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var kind = candidate.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)
                        ? UriKind.Absolute
                        : UriKind.Relative;
                    var info = Application.GetResourceStream(new Uri(candidate, kind));
                    if (info?.Stream == null) continue;

                    stream = info.Stream;
                    resourceName = candidate;
                    return true;
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.WriteWarning($"WPF resource candidate '{candidate}' failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteError($"Opening WPF resource '{fileName}'", ex);
        }

        return false;
    }

    private static string? FindManifestResourceName(Assembly assembly, string fileName)
    {
        var normalizedFileName = fileName.Replace('\\', '.').Replace('/', '.');
        var names = assembly.GetManifestResourceNames();

        return names.FirstOrDefault(name => name.Equals($"CiscoConfigGuiWpf.Localization.{normalizedFileName}", StringComparison.OrdinalIgnoreCase))
               ?? names.FirstOrDefault(name => name.EndsWith($".Localization.{normalizedFileName}", StringComparison.OrdinalIgnoreCase))
               ?? names.FirstOrDefault(name => name.EndsWith($".{normalizedFileName}", StringComparison.OrdinalIgnoreCase));
    }

    private static T? TryLoadExternal<T>(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Localization", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Environment.CurrentDirectory, "Localization", fileName)
        };

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var stream = File.OpenRead(path);
                StartupDiagnostics.WriteInfo($"Using external localization fallback: {path}");
                return JsonSerializer.Deserialize<T>(stream, Options);
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteError($"Loading external localization resource '{path}'", ex);
            }
        }

        return default;
    }
}
