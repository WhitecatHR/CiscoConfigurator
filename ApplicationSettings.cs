using System.IO;
using System.Text;
using System.Text.Json;

namespace CiscoConfigGuiWpf;

public sealed class ApplicationSettings
{
    public int FormatVersion { get; set; } = 2;

    // Allgemein / Sprache
    public string Language { get; set; } = "system";
    public string ReportLanguage { get; set; } = "system";
    public string StartPage { get; set; } = "Übersicht";
    public bool LoadLastProject { get; set; }
    public bool ConfirmReset { get; set; } = true;

    // Darstellung
    public string Theme { get; set; } = "Dunkel";
    public string AccentColor { get; set; } = "Orange";
    public string FontSize { get; set; } = "Normal";
    public bool CompactMode { get; set; }
    public bool AnimationsEnabled { get; set; } = true;
    public bool TooltipsEnabled { get; set; } = true;
    public bool LivePreviewEnabled { get; set; } = true;
    public bool CollapsibleNavigation { get; set; } = true;

    // Konfiguration
    public string DefaultDeviceType { get; set; } = "Router";
    public string DefaultConfigMode { get; set; } = "Ohne VRF";
    public string DefaultPlatform { get; set; } = "IOS-XE";
    public string DefaultInterfacePattern { get; set; } = "GigabitEthernet1/0/1";
    public string DefaultIpStack { get; set; } = "Dual Stack";
    public bool IncludeComments { get; set; } = true;
    public bool IncludeSectionSeparators { get; set; } = true;
    public bool IncludeEnable { get; set; } = true;
    public bool IncludeConfigureTerminal { get; set; } = true;
    public bool IncludeEnd { get; set; } = true;
    public bool IncludeWriteMemory { get; set; } = true;
    public string ValidationMode { get; set; } = "Streng";
    public string AutoFixMode { get; set; } = "Nur vorschlagen";

    // Import / Export
    public string DefaultExportFolder { get; set; } = "";
    public string ExportFileNamePattern { get; set; } = "cisco_config_{hostname}";
    public string LineEndings { get; set; } = "Windows (CRLF)";
    public bool SortConfigurationByModules { get; set; } = true;
    public bool KeepUnknownCommands { get; set; } = true;
    public bool IncludeCustomCommands { get; set; } = true;
    public bool TimestampInFileName { get; set; }
    public bool ExportPeerConfiguration { get; set; }
    public bool GenerateRollbackFile { get; set; }
    public bool ExportReportsTogether { get; set; }

    // Projekte / Sicherung
    public bool AutoSaveEnabled { get; set; } = false;
    public int AutoSaveIntervalSeconds { get; set; } = 60;
    public int BackupCount { get; set; } = 20;
    public string BackupFolder { get; set; } = "";
    public bool SaveProjectOnExit { get; set; } = true;
    public bool RestoreAfterCrash { get; set; } = true;
    public bool HistoryEnabled { get; set; } = true;
    public int HistoryLimit { get; set; } = 50;

    // SSH / Betrieb
    public int DefaultSshPort { get; set; } = 22;
    public int ConnectionTimeoutSeconds { get; set; } = 15;
    public int CommandTimeoutSeconds { get; set; } = 180;
    public int CommandDelayMilliseconds { get; set; } = 45;
    public bool BackupBeforeTransfer { get; set; } = true;
    public bool AbortTransferOnError { get; set; } = true;
    public bool ShowCommandsBeforeSend { get; set; } = true;
    public bool StorePasswords { get; set; }
    public bool SessionLoggingEnabled { get; set; } = true;
    public string DeviceBackupFolder { get; set; } = "";

    // Diagramm / Bericht
    public bool AutomaticDiagramLayout { get; set; } = true;
    public bool SnapDiagramToGrid { get; set; } = true;
    public bool ShowConnectionTypes { get; set; } = true;
    public bool ShowInterfaceNames { get; set; } = true;
    public bool ShowIpAddresses { get; set; }
    public bool ShowVlans { get; set; }
    public bool ShowRoutingDetails { get; set; } = true;
    public string DefaultReportFormat { get; set; } = "PDF";
    public string CompanyName { get; set; } = "";
    public string ProjectManager { get; set; } = "";
    public string ReportLogoPath { get; set; } = "";
    public string PageSize { get; set; } = "A4";
    public string PageOrientation { get; set; } = "Hochformat";

    // Erweitert
    public bool StrictImportValidation { get; set; } = true;
    public bool DeveloperMode { get; set; }
    public bool IncludeDiagnosticDetails { get; set; } = true;

    // Plugins
    public List<string> DisabledPluginIds { get; set; } = new();
    public bool ValidatePluginsOnStartup { get; set; } = true;
}

public static class ApplicationSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CiscoKonfigurator");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static ApplicationSettings Current { get; private set; } = Load();

    public static ApplicationSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new ApplicationSettings();
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8), JsonOptions)
                           ?? new ApplicationSettings();
            settings.DisabledPluginIds ??= new List<string>();
            return settings;
        }
        catch
        {
            return new ApplicationSettings();
        }
    }

    public static void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        Current = settings;
    }

    public static void Import(string fileName)
    {
        var settings = JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(fileName, Encoding.UTF8), JsonOptions)
                       ?? throw new InvalidDataException("Die Einstellungsdatei enthält keine gültigen Daten.");
        settings.DisabledPluginIds ??= new List<string>();
        Save(settings);
    }

    public static void Export(string fileName, ApplicationSettings settings)
    {
        File.WriteAllText(fileName, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
    }
}
