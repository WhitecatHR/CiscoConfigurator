using System.IO;
using System.Text;

namespace CiscoConfigGuiWpf;

/// <summary>
/// Minimal startup diagnostics that work before settings, localization and the main window are available.
/// Logging is intentionally independent from developer mode.
/// </summary>
internal static class StartupDiagnostics
{
    private static readonly object Sync = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CiscoKonfigurator",
        "Logs");

    public static string StartupLogPath => Path.Combine(LogDirectory, "startup.log");
    public static string StartupErrorLogPath => Path.Combine(LogDirectory, "startup_error.log");

    public static void StartSession()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var builder = new StringBuilder();
            builder.AppendLine("Cisco Configuration Tool - Startup Log");
            builder.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.AppendLine("Process: " + (Environment.ProcessPath ?? "unknown"));
            builder.AppendLine("Base directory: " + AppContext.BaseDirectory);
            builder.AppendLine("OS: " + Environment.OSVersion);
            builder.AppendLine(".NET: " + Environment.Version);
            builder.AppendLine(new string('-', 72));
            File.WriteAllText(StartupLogPath, builder.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // Startup diagnostics must never prevent application startup.
        }
    }

    public static void WriteInfo(string message) => Append("INFO", message, null);

    public static void WriteWarning(string message) => Append("WARN", message, null);

    public static void WriteError(string phase, Exception exception) => Append("ERROR", phase, exception);

    private static void Append(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}" + Environment.NewLine;
                File.AppendAllText(StartupLogPath, line, new UTF8Encoding(false));

                if (exception == null) return;

                var error = new StringBuilder();
                error.AppendLine("Cisco Configuration Tool - Startup Error");
                error.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                error.AppendLine("Phase: " + message);
                error.AppendLine();
                error.AppendLine(exception.ToString());
                File.WriteAllText(StartupErrorLogPath, error.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            TryWriteFallback(level, message, exception);
        }
    }

    private static void TryWriteFallback(string level, string message, Exception? exception)
    {
        try
        {
            var fallback = Path.Combine(Path.GetTempPath(), "CiscoKonfigurator_startup_error.log");
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}{exception}";
            File.WriteAllText(fallback, text, new UTF8Encoding(false));
        }
        catch
        {
            // No further fallback is available.
        }
    }
}
