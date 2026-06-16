using System.Diagnostics;
using System.IO;
using System.Text;

namespace CiscoConfigGuiWpf;

public static class DeveloperDiagnosticsService
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> MissingTranslations = new(StringComparer.Ordinal);

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CiscoKonfigurator",
        "Logs");

    public static string DeveloperLogPath => Path.Combine(LogDirectory, "developer.log");
    public static string TranslationAuditPath => Path.Combine(LogDirectory, "translation_audit.txt");

    public static bool Enabled => ApplicationSettingsService.Current.DeveloperMode;

    public static void Log(string category, string message)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(message)) return;
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    DeveloperLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnoseprotokollierung darf die Anwendung nicht beeinflussen.
        }
    }

    public static void ReportMissingTranslation(string source)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(source)) return;
        var normalized = source.Replace("\r\n", "\n").Trim();
        if (normalized.Length < 2) return;

        lock (Sync)
        {
            if (!MissingTranslations.Add(normalized)) return;
        }

        Log("LOCALIZATION", "Missing or incomplete English translation: " + normalized.Replace("\n", " "));
    }

    public static void WriteTranslationAudit(IEnumerable<string> findings)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var distinct = findings
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Replace("\r\n", "\n").Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Cisco Configurator - Translation Audit");
            sb.AppendLine("Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Language: " + LocalizationService.CurrentLanguage);
            sb.AppendLine("Findings: " + distinct.Count);
            sb.AppendLine();
            foreach (var item in distinct)
            {
                sb.AppendLine("---");
                sb.AppendLine(item);
            }

            File.WriteAllText(TranslationAuditPath, sb.ToString(), new UTF8Encoding(false));
            Log("LOCALIZATION", $"Translation audit written with {distinct.Count} finding(s).");
        }
        catch
        {
            // Diagnoseprotokollierung darf die Anwendung nicht beeinflussen.
        }
    }

    public static void OpenLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = LogDirectory,
            UseShellExecute = true
        });
    }
}
