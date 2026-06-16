using System.Collections.ObjectModel;

namespace CiscoConfigGuiWpf;

public sealed class NetworkProject
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Neues Netzwerkprojekt";
    public string Description { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public ObservableCollection<ProjectDeviceSnapshot> Devices { get; set; } = new();
    public ObservableCollection<IpamEntry> IpamEntries { get; set; } = new();
    public ObservableCollection<ProjectLink> Links { get; set; } = new();
    public ObservableCollection<BackupRecord> Backups { get; set; } = new();
}

public sealed class ProjectDeviceSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neues Gerät";
    public string DeviceType { get; set; } = "Router";
    public string ConfigMode { get; set; } = "Ohne VRF";
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string GeneratedConfiguration { get; set; } = "";
    public double? DiagramX { get; set; }
    public double? DiagramY { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public string Status => string.IsNullOrWhiteSpace(GeneratedConfiguration) ? "Entwurf" : "Konfiguration vorhanden";
}

public sealed class IpamEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Network { get; set; } = "";
    public int PrefixLength { get; set; } = 24;
    public string Vlan { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string DhcpStart { get; set; } = "";
    public string DhcpEnd { get; set; } = "";
    public string Device { get; set; } = "";
    public string Interface { get; set; } = "";
    public string Description { get; set; } = "";
    public string AddressFamily => Network.Contains(':') ? "IPv6" : "IPv4";
}

public sealed class ProjectLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceDeviceId { get; set; } = "";
    public string SourceInterface { get; set; } = "";
    public string TargetDeviceId { get; set; } = "";
    public string TargetInterface { get; set; } = "";
    public string LinkType { get; set; } = "Ethernet";
    public string Description { get; set; } = "";
}

public sealed class BackupRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = "";
    public string BackupType { get; set; } = "Running-Config";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "";
    public string Content { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string DisplayCreated => CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
}

public sealed record DependencyFinding(
    string Severity,
    string Area,
    string Message,
    string FixKey = "",
    string NavigationModule = "",
    string NavigationField = "");

public sealed record SecurityFinding(
    string Severity,
    string Category,
    string Message,
    string Recommendation);

public sealed record ConfigDiffLine(
    string Change,
    string Context,
    string Line,
    int OldLine,
    int NewLine);

public sealed record PortPlanEntry(
    string Interface,
    string Description,
    string Mode,
    string AccessVlan,
    string VoiceVlan,
    string AllowedVlans,
    string NativeVlan,
    string ChannelGroup,
    string IpAddress,
    string State,
    string StpProtection);

public sealed record GlobalSearchResult(
    string Kind,
    string Title,
    string Detail,
    string Tab,
    string ModuleName,
    string FieldName,
    string Command);

public sealed record CommandAnalysisResult(
    string Input,
    string MatchedPattern,
    string Module,
    string Mode,
    string Meaning,
    IReadOnlyList<string> Parts,
    double Confidence);

public sealed record SshConnectionSettings(
    string Host,
    int Port,
    string Username,
    string AuthenticationMode,
    string PrivateKeyPath,
    string Password,
    int LineDelayMs,
    bool SaveAfterTransfer,
    int ConnectionTimeoutSeconds = 15,
    int CommandTimeoutSeconds = 180,
    bool AbortOnError = true);

public sealed record SshOperationResult(bool Success, string Output, string Error, int ExitCode);

public sealed class AutoSaveState
{
    public NetworkProject Project { get; set; } = new();
    public TemplateData CurrentDevice { get; set; } = new();
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
}
