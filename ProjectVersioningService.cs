using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CiscoConfigGuiWpf;

public static class ProjectVersioningService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static ProjectVersionEntry? CreateVersion(
        NetworkProject project,
        string label,
        string comment,
        bool automatic,
        int historyLimit,
        bool skipDuplicate = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.VersionHistory ??= new ObservableCollection<ProjectVersionEntry>();

        var snapshotJson = SerializeSnapshot(project);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
        if (skipDuplicate && string.Equals(project.VersionHistory.FirstOrDefault()?.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
            return null;

        var entry = new ProjectVersionEntry
        {
            Label = string.IsNullOrWhiteSpace(label) ? $"Version {project.VersionHistory.Count + 1}" : label.Trim(),
            Comment = comment?.Trim() ?? string.Empty,
            IsAutomatic = automatic,
            ContentHash = hash,
            SnapshotJson = snapshotJson,
            DeviceCount = project.Devices?.Count ?? 0,
            LinkCount = project.Links?.Count ?? 0,
            IpamCount = project.IpamEntries?.Count ?? 0,
            AclRuleCount = project.AclRules?.Count ?? 0,
            CreatedUtc = DateTime.UtcNow
        };

        project.VersionHistory.Insert(0, entry);
        var limit = Math.Clamp(historyLimit, 1, 500);
        while (project.VersionHistory.Count > limit)
            project.VersionHistory.RemoveAt(project.VersionHistory.Count - 1);
        return entry;
    }

    public static NetworkProject RestoreVersion(NetworkProject currentProject, ProjectVersionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(currentProject);
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.SnapshotJson))
            throw new InvalidDataException(LocalizationService.Get("versioning.error_empty", "Der ausgewählte Versionsstand enthält keine Projektdaten."));

        var restored = JsonSerializer.Deserialize<NetworkProject>(entry.SnapshotJson, JsonOptions)
                       ?? throw new InvalidDataException(LocalizationService.Get("versioning.error_read", "Der ausgewählte Versionsstand konnte nicht gelesen werden."));
        restored.VersionHistory = currentProject.VersionHistory ?? new ObservableCollection<ProjectVersionEntry>();
        restored.ModifiedUtc = DateTime.UtcNow;
        Normalize(restored);
        return restored;
    }

    public static string Compare(NetworkProject currentProject, ProjectVersionEntry entry, bool english)
    {
        ArgumentNullException.ThrowIfNull(currentProject);
        ArgumentNullException.ThrowIfNull(entry);
        var previous = JsonSerializer.Deserialize<NetworkProject>(entry.SnapshotJson, JsonOptions)
                       ?? throw new InvalidDataException(english
                           ? "The selected project version could not be read."
                           : "Der ausgewählte Versionsstand konnte nicht gelesen werden.");
        Normalize(previous);
        Normalize(currentProject);

        string R(string de, string en) => english ? en : de;
        var sb = new StringBuilder();
        sb.AppendLine(R("Versionsvergleich", "Version comparison"));
        sb.AppendLine(new string('=', 72));
        sb.AppendLine($"{R("Version", "Version")}: {entry.Label}");
        sb.AppendLine($"{R("Zeitpunkt", "Created")}: {entry.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        if (!string.IsNullOrWhiteSpace(entry.Comment)) sb.AppendLine($"{R("Kommentar", "Comment")}: {entry.Comment}");
        sb.AppendLine();

        CompareValue(sb, R("Projektname", "Project name"), previous.Name, currentProject.Name, english);
        CompareValue(sb, R("Beschreibung", "Description"), previous.Description, currentProject.Description, english);
        CompareValue(sb, R("Projektnummer", "Project number"), previous.ProjectInfo.ProjectNumber, currentProject.ProjectInfo.ProjectNumber, english);
        CompareValue(sb, R("Status", "Status"), previous.ProjectInfo.Status, currentProject.ProjectInfo.Status, english);
        CompareValue(sb, R("Projektversion", "Project version"), previous.ProjectInfo.Version, currentProject.ProjectInfo.Version, english);

        sb.AppendLine();
        sb.AppendLine(R("GERÄTE", "DEVICES"));
        CompareDevices(sb, previous.Devices, currentProject.Devices, english);

        sb.AppendLine();
        sb.AppendLine(R("PROJEKTUMFANG", "PROJECT SCOPE"));
        AppendCount(sb, R("Geräte", "Devices"), previous.Devices.Count, currentProject.Devices.Count);
        AppendCount(sb, R("Verbindungen", "Connections"), previous.Links.Count, currentProject.Links.Count);
        AppendCount(sb, "IPAM", previous.IpamEntries.Count, currentProject.IpamEntries.Count);
        AppendCount(sb, R("ACL-Regeln", "ACL rules"), previous.AclRules.Count, currentProject.AclRules.Count);
        AppendCount(sb, R("ACL-Zuordnungen", "ACL assignments"), previous.AclBindings.Count, currentProject.AclBindings.Count);
        AppendCount(sb, R("Backups", "Backups"), previous.Backups.Count, currentProject.Backups.Count);

        sb.AppendLine();
        sb.AppendLine(R("DETAILÄNDERUNGEN", "DETAILED CHANGES"));
        CompareCollectionKeys(sb, R("IPAM", "IPAM"),
            previous.IpamEntries.Select(x => $"{x.Network}/{x.PrefixLength}|{x.Device}|{x.Interface}"),
            currentProject.IpamEntries.Select(x => $"{x.Network}/{x.PrefixLength}|{x.Device}|{x.Interface}"), english);
        CompareCollectionKeys(sb, R("Verbindungen", "Connections"),
            previous.Links.Select(LinkKey), currentProject.Links.Select(LinkKey), english);
        CompareCollectionKeys(sb, R("ACL-Regeln", "ACL rules"),
            previous.AclRules.Select(AclKey), currentProject.AclRules.Select(AclKey), english);

        return sb.ToString();
    }

    public static string SerializeSnapshot(NetworkProject project)
    {
        var snapshot = new NetworkProject
        {
            FormatVersion = Math.Max(project.FormatVersion, 2),
            Name = project.Name,
            Description = project.Description,
            CreatedUtc = project.CreatedUtc,
            ModifiedUtc = project.ModifiedUtc,
            Devices = project.Devices ?? new ObservableCollection<ProjectDeviceSnapshot>(),
            IpamEntries = project.IpamEntries ?? new ObservableCollection<IpamEntry>(),
            Links = project.Links ?? new ObservableCollection<ProjectLink>(),
            Backups = project.Backups ?? new ObservableCollection<BackupRecord>(),
            AclRules = project.AclRules ?? new ObservableCollection<ProjectAclRule>(),
            AclBindings = project.AclBindings ?? new ObservableCollection<ProjectAclBinding>(),
            ProjectInfo = project.ProjectInfo ?? new ProjectPlanInfo(),
            VersionHistory = new ObservableCollection<ProjectVersionEntry>()
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    private static void CompareDevices(
        StringBuilder sb,
        IEnumerable<ProjectDeviceSnapshot> oldDevices,
        IEnumerable<ProjectDeviceSnapshot> newDevices,
        bool english)
    {
        string R(string de, string en) => english ? en : de;
        var oldMap = oldDevices.ToDictionary(DeviceKey, StringComparer.OrdinalIgnoreCase);
        var newMap = newDevices.ToDictionary(DeviceKey, StringComparer.OrdinalIgnoreCase);
        var added = newMap.Keys.Except(oldMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removed = oldMap.Keys.Except(newMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var common = oldMap.Keys.Intersect(newMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        foreach (var key in added) sb.AppendLine($"+ {R("Hinzugefügt", "Added")}: {newMap[key].Name} ({newMap[key].DeviceType})");
        foreach (var key in removed) sb.AppendLine($"- {R("Entfernt", "Removed")}: {oldMap[key].Name} ({oldMap[key].DeviceType})");
        foreach (var key in common)
        {
            var oldDevice = oldMap[key];
            var newDevice = newMap[key];
            var changes = new List<string>();
            if (!string.Equals(oldDevice.DeviceType, newDevice.DeviceType, StringComparison.OrdinalIgnoreCase)) changes.Add(R("Gerätetyp", "device type"));
            if (!string.Equals(oldDevice.ConfigMode, newDevice.ConfigMode, StringComparison.OrdinalIgnoreCase)) changes.Add(R("Modus", "mode"));
            if (!string.Equals(oldDevice.Site, newDevice.Site, StringComparison.OrdinalIgnoreCase)) changes.Add(R("Standort", "site"));
            if (!string.Equals(oldDevice.TopologyRole, newDevice.TopologyRole, StringComparison.OrdinalIgnoreCase)) changes.Add(R("Topologierolle", "topology role"));
            if (!string.Equals(NormalizeConfig(oldDevice.GeneratedConfiguration), NormalizeConfig(newDevice.GeneratedConfiguration), StringComparison.Ordinal))
            {
                var diffCount = ConfigDiffService.Compare(oldDevice.GeneratedConfiguration, newDevice.GeneratedConfiguration).Count;
                changes.Add($"{R("Konfiguration", "configuration")} ({diffCount})");
            }
            if (changes.Count > 0) sb.AppendLine($"~ {newDevice.Name}: {string.Join(", ", changes)}");
        }
        if (added.Count == 0 && removed.Count == 0 && common.All(key =>
                string.Equals(NormalizeConfig(oldMap[key].GeneratedConfiguration), NormalizeConfig(newMap[key].GeneratedConfiguration), StringComparison.Ordinal) &&
                string.Equals(oldMap[key].Site, newMap[key].Site, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(oldMap[key].TopologyRole, newMap[key].TopologyRole, StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine(R("Keine Geräteänderungen.", "No device changes."));
    }

    private static void CompareValue(StringBuilder sb, string label, string oldValue, string newValue, bool english)
    {
        if (string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal)) return;
        sb.AppendLine($"~ {label}: '{oldValue}' -> '{newValue}'");
    }

    private static void AppendCount(StringBuilder sb, string label, int oldCount, int newCount) =>
        sb.AppendLine($"- {label}: {oldCount} -> {newCount} ({newCount - oldCount:+#;-#;0})");

    private static void CompareCollectionKeys(StringBuilder sb, string label, IEnumerable<string> oldValues, IEnumerable<string> newValues, bool english)
    {
        var oldSet = new HashSet<string>(oldValues, StringComparer.OrdinalIgnoreCase);
        var newSet = new HashSet<string>(newValues, StringComparer.OrdinalIgnoreCase);
        var added = newSet.Except(oldSet, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = oldSet.Except(newSet, StringComparer.OrdinalIgnoreCase).ToList();
        sb.AppendLine($"{label}: +{added.Count} / -{removed.Count}");
        foreach (var item in added.Take(20)) sb.AppendLine($"  + {item}");
        foreach (var item in removed.Take(20)) sb.AppendLine($"  - {item}");
        if (added.Count > 20 || removed.Count > 20)
            sb.AppendLine(english ? "  Further changes omitted." : "  Weitere Änderungen wurden gekürzt.");
    }

    private static string DeviceKey(ProjectDeviceSnapshot device) =>
        string.IsNullOrWhiteSpace(device.Id) ? device.Name : device.Id;

    private static string LinkKey(ProjectLink link) =>
        $"{link.SourceDeviceId}|{link.SourceInterface}|{link.TargetDeviceId}|{link.TargetInterface}|{link.LinkType}";

    private static string AclKey(ProjectAclRule rule) =>
        $"{rule.DeviceName}|{rule.AclName}|{rule.Sequence}|{rule.Action}|{rule.Protocol}|{rule.Source}|{rule.Destination}|{rule.Service}";

    private static string NormalizeConfig(string value) =>
        string.Join("\n", (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(x => x.TrimEnd()));

    private static void Normalize(NetworkProject project)
    {
        project.FormatVersion = Math.Max(project.FormatVersion, 2);
        project.Devices ??= new ObservableCollection<ProjectDeviceSnapshot>();
        project.IpamEntries ??= new ObservableCollection<IpamEntry>();
        project.Links ??= new ObservableCollection<ProjectLink>();
        project.Backups ??= new ObservableCollection<BackupRecord>();
        project.AclRules ??= new ObservableCollection<ProjectAclRule>();
        project.AclBindings ??= new ObservableCollection<ProjectAclBinding>();
        project.VersionHistory ??= new ObservableCollection<ProjectVersionEntry>();
        project.ProjectInfo ??= new ProjectPlanInfo();
        foreach (var device in project.Devices)
        {
            device.Values ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            device.Modules ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            device.Inventory ??= new DeviceInventorySnapshot();
        }
        foreach (var link in project.Links)
            link.RoutePoints ??= new ObservableCollection<ProjectLinkRoutePoint>();
    }
}
