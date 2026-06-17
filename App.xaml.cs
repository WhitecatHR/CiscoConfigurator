using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CiscoConfigGuiWpf;

public partial class App : Application
{
    private bool _handlersRegistered;

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupDiagnostics.StartSession();
        RegisterGlobalExceptionHandlers();
        StartupDiagnostics.WriteInfo("OnStartup entered.");

        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            StartupDiagnostics.WriteInfo("WPF application startup initialized.");

            var settings = ApplicationSettingsService.Current;
            StartupDiagnostics.WriteInfo("Application settings loaded.");

            LocalizationService.SetLanguage(settings.Language);
            StartupDiagnostics.WriteInfo($"Localization initialized: {LocalizationService.CurrentLanguage}.");
            LocalizationCatalogValidator.ValidateModuleCatalogs();
            if (settings.ValidatePluginsOnStartup)
            {
                var pluginStatuses = PluginModuleService.GetPluginStatuses();
                StartupDiagnostics.WriteInfo($"Plugin validation completed: {pluginStatuses.Count} plugin file(s), {pluginStatuses.Count(x => x.Valid && x.Enabled)} enabled.");
            }

            var window = new MainWindow();
            MainWindow = window;
            StartupDiagnostics.WriteInfo("MainWindow constructed.");

            window.Show();
            StartupDiagnostics.WriteInfo("MainWindow shown successfully.");
            DeveloperDiagnosticsService.Log("STARTUP", "Application startup completed.");
        }
        catch (Exception ex)
        {
            HandleFatalStartupException("Startup", ex);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        if (_handlersRegistered) return;
        _handlersRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                StartupDiagnostics.WriteError("AppDomain.UnhandledException", exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            StartupDiagnostics.WriteError("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            StartupDiagnostics.WriteError("DispatcherUnhandledException", args.Exception);
            ShowErrorDialog(
                SafeText("text.cisco_konfigurator_wurde_durch_einen_fehler_gestoppt", "Cisco Configurator encountered an error."),
                args.Exception,
                SafeText("text.cisco_konfigurator_fehler", "Cisco Configurator error"));

            args.Handled = true;
            if (MainWindow == null || !MainWindow.IsLoaded)
                Shutdown(-1);
        };
    }

    private void HandleFatalStartupException(string phase, Exception exception)
    {
        StartupDiagnostics.WriteError(phase, exception);
        ShowErrorDialog(
            SafeText("text.cisco_konfigurator_konnte_nicht_gestartet_werden", "Cisco Configurator could not be started."),
            exception,
            SafeText("app.startup_error_title", "Startup error"));
        Shutdown(-1);
    }

    private static void ShowErrorDialog(string message, Exception exception, string title)
    {
        try
        {
            var details = ShouldIncludeDiagnosticDetails() ? exception.ToString() : exception.Message;
            MessageBox.Show(
                message + "\n\n" + details + "\n\nLog:\n" + StartupDiagnostics.StartupErrorLogPath,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The log remains available even if a dialog cannot be displayed.
        }
    }

    private static bool ShouldIncludeDiagnosticDetails()
    {
        try
        {
            return ApplicationSettingsService.Current.IncludeDiagnosticDetails;
        }
        catch
        {
            return true;
        }
    }

    private static string SafeText(string key, string fallback)
    {
        try
        {
            return LocalizationService.Get(key, fallback);
        }
        catch
        {
            return fallback;
        }
    }
}
