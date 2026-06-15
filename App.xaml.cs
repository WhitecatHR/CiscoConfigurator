using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CiscoConfigGuiWpf;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CiscoKonfigurator",
        "startup_error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrashLog("UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(
                "Cisco Konfigurator wurde durch einen Fehler gestoppt.\n\n" +
                args.Exception.Message + "\n\nLogdatei:\n" + LogPath,
                "Cisco Konfigurator - Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            WriteCrashLog("Startup", ex);
            MessageBox.Show(
                "Cisco Konfigurator konnte nicht gestartet werden.\n\n" +
                ex.Message + "\n\nLogdatei:\n" + LogPath,
                "Cisco Konfigurator - Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void WriteCrashLog(string phase, Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("Cisco Konfigurator - Fehlerprotokoll");
            sb.AppendLine("Zeit: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Phase: " + phase);
            sb.AppendLine();
            sb.AppendLine(ex.ToString());

            File.WriteAllText(LogPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Logging darf den Programmstart nicht zusätzlich verhindern.
        }
    }
}
