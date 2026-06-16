using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public sealed record DiscoveryImportResult(int AddedLinks, int SkippedLinks, IReadOnlyList<string> UnresolvedDevices);

public static class TopologyPlanningService
{
    private static readonly string[] RoleOrder = { "WAN", "Core", "Distribution", "Access", "Other" };

    public static void ApplySmartLayout(NetworkProject project, double canvasWidth, double canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Devices.Count == 0) return;

        var siteGroups = project.Devices
            .GroupBy(InferSite, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        const double nodeWidth = 220;
        const double nodeHeight = 170;
        const double leftMargin = 35;
        const double topMargin = 48;
        const double horizontalGap = 28;
        const double verticalGap = 34;

        var usableWidth = Math.Max(400, canvasWidth - leftMargin * 2);
        var siteWidth = Math.Max(260, usableWidth / Math.Max(1, siteGroups.Count));
        var rowHeight = Math.Max(nodeHeight + verticalGap, (canvasHeight - topMargin * 2) / RoleOrder.Length);

        for (var siteIndex = 0; siteIndex < siteGroups.Count; siteIndex++)
        {
            var site = siteGroups[siteIndex];
            var siteX = leftMargin + siteIndex * siteWidth;

            foreach (var role in RoleOrder)
            {
                var devices = site
                    .Where(device => InferRole(device).Equals(role, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (devices.Count == 0) continue;

                var roleIndex = Array.IndexOf(RoleOrder, role);
                var y = topMargin + roleIndex * rowHeight;
                var totalWidth = devices.Count * nodeWidth + Math.Max(0, devices.Count - 1) * horizontalGap;
                var firstX = siteX + Math.Max(8, (siteWidth - totalWidth) / 2);

                for (var index = 0; index < devices.Count; index++)
                {
                    var device = devices[index];
                    device.Site = site.Key;
                    if (string.IsNullOrWhiteSpace(device.TopologyRole) || device.TopologyRole.Equals("Automatisch", StringComparison.OrdinalIgnoreCase))
                        device.TopologyRole = role;
                    device.DiagramX = Math.Clamp(firstX + index * (nodeWidth + horizontalGap), 5, Math.Max(5, canvasWidth - nodeWidth - 5));
                    device.DiagramY = Math.Clamp(y, 35, Math.Max(35, canvasHeight - nodeHeight - 5));
                }
            }
        }
    }

    public static string InferRole(ProjectDeviceSnapshot device)
    {
        if (!string.IsNullOrWhiteSpace(device.TopologyRole) &&
            !device.TopologyRole.Equals("Automatisch", StringComparison.OrdinalIgnoreCase))
            return NormalizeRole(device.TopologyRole);

        var name = device.Name ?? string.Empty;
        if (Regex.IsMatch(name, @"(?i)(wan|edge|internet|provider|isp|border)")) return "WAN";
        if (Regex.IsMatch(name, @"(?i)(core|backbone)")) return "Core";
        if (Regex.IsMatch(name, @"(?i)(dist|distribution|aggregation|agg)")) return "Distribution";
        if (Regex.IsMatch(name, @"(?i)(access|acc|floor|etage)")) return "Access";

        if ((device.DeviceType ?? string.Empty).Contains("L2", StringComparison.OrdinalIgnoreCase)) return "Access";
        if ((device.DeviceType ?? string.Empty).Contains("L3", StringComparison.OrdinalIgnoreCase)) return "Core";
        if ((device.DeviceType ?? string.Empty).Contains("Router", StringComparison.OrdinalIgnoreCase)) return "WAN";
        return "Other";
    }

    public static string InferSite(ProjectDeviceSnapshot device)
    {
        if (!string.IsNullOrWhiteSpace(device.Site)) return device.Site.Trim();
        var match = Regex.Match(device.Name ?? string.Empty, @"^([A-Za-z]{2,8})[-_]");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "Standard";
    }

    public static DiscoveryImportResult AddDiscoveredLinks(NetworkProject project, ProjectDeviceSnapshot sourceDevice, string neighborOutput)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sourceDevice);

        var neighbors = ParseDiscoveryNeighbors(neighborOutput).ToList();
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var skipped = 0;

        foreach (var neighbor in neighbors)
        {
            var target = FindDevice(project, neighbor.RemoteDevice);
            if (target == null)
            {
                unresolved.Add(neighbor.RemoteDevice);
                continue;
            }

            var duplicate = project.Links.Any(link =>
                ((link.SourceDeviceId.Equals(sourceDevice.Id, StringComparison.OrdinalIgnoreCase) &&
                  link.TargetDeviceId.Equals(target.Id, StringComparison.OrdinalIgnoreCase) &&
                  InterfacesEqual(link.SourceInterface, neighbor.LocalInterface) &&
                  InterfacesEqual(link.TargetInterface, neighbor.RemoteInterface)) ||
                 (link.SourceDeviceId.Equals(target.Id, StringComparison.OrdinalIgnoreCase) &&
                  link.TargetDeviceId.Equals(sourceDevice.Id, StringComparison.OrdinalIgnoreCase) &&
                  InterfacesEqual(link.SourceInterface, neighbor.RemoteInterface) &&
                  InterfacesEqual(link.TargetInterface, neighbor.LocalInterface))));

            if (duplicate)
            {
                skipped++;
                continue;
            }

            project.Links.Add(new ProjectLink
            {
                SourceDeviceId = sourceDevice.Id,
                SourceInterface = neighbor.LocalInterface,
                TargetDeviceId = target.Id,
                TargetInterface = neighbor.RemoteInterface,
                LinkType = "Ethernet",
                Description = $"Automatisch über {neighbor.Protocol} erkannt",
                DiscoverySource = neighbor.Protocol
            });
            added++;
        }

        return new DiscoveryImportResult(added, skipped, unresolved.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static IEnumerable<DiscoveryNeighbor> ParseDiscoveryNeighbors(string output)
    {
        var text = (output ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(text)) yield break;

        foreach (Match match in Regex.Matches(text,
                     @"(?ims)Device ID:\s*(?<remote>[^\n]+).*?Interface:\s*(?<local>[^,\n]+),\s*Port ID \(outgoing port\):\s*(?<remoteIf>[^\n]+)"))
        {
            yield return new DiscoveryNeighbor(
                "CDP",
                CleanDeviceName(match.Groups["remote"].Value),
                match.Groups["local"].Value.Trim(),
                match.Groups["remoteIf"].Value.Trim());
        }

        var blocks = Regex.Split(text, @"^-{3,}\s*$|^Chassis id:\s*", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(block => !string.IsNullOrWhiteSpace(block));
        foreach (var block in blocks)
        {
            var systemName = Regex.Match(block, @"(?im)^\s*System Name:\s*(?<value>[^\n]+)");
            var local = Regex.Match(block, @"(?im)^\s*Local (?:Intf|Interface):\s*(?<value>[^\n]+)");
            var remotePort = Regex.Match(block, @"(?im)^\s*Port id:\s*(?<value>[^\n]+)");
            if (!systemName.Success || !local.Success || !remotePort.Success) continue;
            yield return new DiscoveryNeighbor(
                "LLDP",
                CleanDeviceName(systemName.Groups["value"].Value),
                local.Groups["value"].Value.Trim(),
                remotePort.Groups["value"].Value.Trim());
        }
    }

    private static ProjectDeviceSnapshot? FindDevice(NetworkProject project, string deviceName)
    {
        var clean = CleanDeviceName(deviceName);
        return project.Devices.FirstOrDefault(device =>
            CleanDeviceName(device.Name).Equals(clean, StringComparison.OrdinalIgnoreCase));
    }

    private static bool InterfacesEqual(string left, string right) =>
        NormalizeInterface(left).Equals(NormalizeInterface(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeInterface(string value) => Regex.Replace(value ?? string.Empty, @"\s+", string.Empty)
        .Replace("GigabitEthernet", "Gi", StringComparison.OrdinalIgnoreCase)
        .Replace("TenGigabitEthernet", "Te", StringComparison.OrdinalIgnoreCase)
        .Replace("FastEthernet", "Fa", StringComparison.OrdinalIgnoreCase)
        .Replace("Ethernet", "Eth", StringComparison.OrdinalIgnoreCase);

    private static string CleanDeviceName(string value)
    {
        var cleaned = (value ?? string.Empty).Trim().TrimEnd('.');
        var dot = cleaned.IndexOf('.');
        return dot > 0 ? cleaned[..dot] : cleaned;
    }

    private static string NormalizeRole(string value)
    {
        if (value.Equals("WAN", StringComparison.OrdinalIgnoreCase)) return "WAN";
        if (value.Equals("Core", StringComparison.OrdinalIgnoreCase)) return "Core";
        if (value.StartsWith("Dist", StringComparison.OrdinalIgnoreCase)) return "Distribution";
        if (value.Equals("Access", StringComparison.OrdinalIgnoreCase)) return "Access";
        return "Other";
    }
}

public sealed record DiscoveryNeighbor(string Protocol, string RemoteDevice, string LocalInterface, string RemoteInterface);
public sealed record StaticRouteInfo(string Device, string Vrf, string Network, string MaskOrPrefix, string NextHop, string AddressFamily);
public sealed record RoutingProtocolInfo(string Device, string Protocol, string Instance, string Details);

public static class RoutingVisualizationService
{
    public static IReadOnlyList<string> GetDeviceDetails(ProjectDeviceSnapshot device)
    {
        var config = device.GeneratedConfiguration ?? string.Empty;
        var details = new List<string>();

        var vrfs = Regex.Matches(config, @"(?im)^\s*(?:vrf definition|ip vrf)\s+(\S+)")
            .Cast<Match>().Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        if (vrfs.Count > 0) details.Add("VRF: " + string.Join(",", vrfs));

        var ospfProcesses = Regex.Matches(config, @"(?im)^\s*router ospf\s+(\S+)")
            .Cast<Match>().Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ospfAreas = Regex.Matches(config, @"(?im)\barea\s+(\S+)")
            .Cast<Match>().Select(match => match.Groups[1].Value.TrimEnd('!'))
            .Where(value => value.Length < 24)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        if (ospfProcesses.Count > 0)
            details.Add($"OSPF {string.Join(",", ospfProcesses)}" + (ospfAreas.Count > 0 ? $" · Area {string.Join(",", ospfAreas)}" : string.Empty));

        var bgp = Regex.Match(config, @"(?im)^\s*router bgp\s+(\S+)");
        if (bgp.Success)
        {
            var peers = Regex.Matches(config, @"(?im)^\s*neighbor\s+(\S+)\s+remote-as\s+(\S+)")
                .Cast<Match>()
                .Select(match => $"{match.Groups[1].Value}/{match.Groups[2].Value}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            details.Add("BGP AS " + bgp.Groups[1].Value + (peers.Count > 0 ? " · Peer " + string.Join(",", peers) : string.Empty));
        }

        var eigrp = Regex.Match(config, @"(?im)^\s*router eigrp\s+(\S+)");
        if (eigrp.Success) details.Add("EIGRP " + eigrp.Groups[1].Value);

        var isis = Regex.Match(config, @"(?im)^\s*router isis(?:\s+(\S+))?");
        if (isis.Success) details.Add("IS-IS" + (string.IsNullOrWhiteSpace(isis.Groups[1].Value) ? string.Empty : " " + isis.Groups[1].Value));

        var hsrp = Regex.Matches(config, @"(?im)^\s*standby\s+(\d+)\s+ip\s+(\S+)")
            .Cast<Match>().Select(match => $"{match.Groups[1].Value}:{match.Groups[2].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        if (hsrp.Count > 0) details.Add("HSRP " + string.Join(", ", hsrp));

        return details;
    }

    public static string GetDeviceSummary(ProjectDeviceSnapshot device, int maxLines = 3) =>
        string.Join("\n", GetDeviceDetails(device).Take(Math.Max(1, maxLines)));

    public static string GetLinkSummary(NetworkProject project, ProjectLink link)
    {
        var source = project.Devices.FirstOrDefault(device => device.Id.Equals(link.SourceDeviceId, StringComparison.OrdinalIgnoreCase));
        var target = project.Devices.FirstOrDefault(device => device.Id.Equals(link.TargetDeviceId, StringComparison.OrdinalIgnoreCase));
        if (source == null || target == null) return string.Empty;

        var sourceInfo = ExtractInterfaceRouting(source.GeneratedConfiguration, link.SourceInterface);
        var targetInfo = ExtractInterfaceRouting(target.GeneratedConfiguration, link.TargetInterface);
        var details = new List<string>();

        var network = FirstNonEmpty(sourceInfo.Address, targetInfo.Address);
        if (!string.IsNullOrWhiteSpace(network)) details.Add(network);

        var area = FirstNonEmpty(sourceInfo.OspfArea, targetInfo.OspfArea);
        if (!string.IsNullOrWhiteSpace(area)) details.Add("OSPF Area " + area);

        var vrf = FirstNonEmpty(sourceInfo.Vrf, targetInfo.Vrf);
        if (!string.IsNullOrWhiteSpace(vrf)) details.Add("VRF " + vrf);

        return string.Join(" · ", details.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<StaticRouteInfo> ExtractStaticRoutes(NetworkProject project)
    {
        var result = new List<StaticRouteInfo>();
        foreach (var device in project.Devices)
        {
            foreach (Match match in Regex.Matches(device.GeneratedConfiguration ?? string.Empty,
                         @"(?im)^\s*ip route(?: vrf (\S+))?\s+(\S+)\s+(\S+)\s+(\S+).*$"))
            {
                result.Add(new StaticRouteInfo(
                    device.Name,
                    string.IsNullOrWhiteSpace(match.Groups[1].Value) ? "Global" : match.Groups[1].Value,
                    match.Groups[2].Value,
                    match.Groups[3].Value,
                    match.Groups[4].Value,
                    "IPv4"));
            }

            foreach (Match match in Regex.Matches(device.GeneratedConfiguration ?? string.Empty,
                         @"(?im)^\s*ipv6 route(?: vrf (\S+))?\s+(\S+)\s+(\S+).*$"))
            {
                result.Add(new StaticRouteInfo(
                    device.Name,
                    string.IsNullOrWhiteSpace(match.Groups[1].Value) ? "Global" : match.Groups[1].Value,
                    match.Groups[2].Value,
                    string.Empty,
                    match.Groups[3].Value,
                    "IPv6"));
            }
        }
        return result;
    }

    public static IReadOnlyList<RoutingProtocolInfo> ExtractProtocols(NetworkProject project)
    {
        var result = new List<RoutingProtocolInfo>();
        foreach (var device in project.Devices)
        {
            var config = device.GeneratedConfiguration ?? string.Empty;
            var areas = Regex.Matches(config, @"(?im)\barea\s+(\S+)")
                .Cast<Match>().Select(match => match.Groups[1].Value.TrimEnd('!'))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (Match match in Regex.Matches(config, @"(?im)^\s*router ospf\s+(\S+)"))
                result.Add(new RoutingProtocolInfo(device.Name, "OSPF", match.Groups[1].Value, areas.Count == 0 ? string.Empty : "Areas: " + string.Join(",", areas)));

            foreach (Match match in Regex.Matches(config, @"(?im)^\s*router eigrp\s+(\S+)"))
                result.Add(new RoutingProtocolInfo(device.Name, "EIGRP", match.Groups[1].Value, string.Empty));

            var peers = Regex.Matches(config, @"(?im)^\s*neighbor\s+(\S+)\s+remote-as\s+(\S+)")
                .Cast<Match>().Select(match => $"{match.Groups[1].Value}/{match.Groups[2].Value}")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (Match match in Regex.Matches(config, @"(?im)^\s*router bgp\s+(\S+)"))
                result.Add(new RoutingProtocolInfo(device.Name, "BGP", match.Groups[1].Value, peers.Count == 0 ? string.Empty : "Peers: " + string.Join(",", peers)));

            foreach (Match match in Regex.Matches(config, @"(?im)^\s*router isis(?:\s+(\S+))?"))
                result.Add(new RoutingProtocolInfo(device.Name, "IS-IS", match.Groups[1].Value, string.Empty));

            foreach (Match match in Regex.Matches(config, @"(?im)^\s*ipv6 router ospf\s+(\S+)"))
                result.Add(new RoutingProtocolInfo(device.Name, "OSPFv3", match.Groups[1].Value, areas.Count == 0 ? string.Empty : "Areas: " + string.Join(",", areas)));

            var hsrp = Regex.Matches(config, @"(?im)^\s*standby\s+(\d+)\s+ip\s+(\S+)")
                .Cast<Match>().Select(match => $"Group {match.Groups[1].Value}: {match.Groups[2].Value}")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (hsrp.Count > 0)
                result.Add(new RoutingProtocolInfo(device.Name, "HSRP", string.Empty, string.Join(", ", hsrp)));
        }
        return result;
    }

    private sealed record InterfaceRoutingInfo(string Address, string OspfArea, string Vrf);

    private static InterfaceRoutingInfo ExtractInterfaceRouting(string config, string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(interfaceName))
            return new InterfaceRoutingInfo(string.Empty, string.Empty, string.Empty);

        var normalizedTarget = NormalizeInterface(interfaceName);
        string currentInterface = string.Empty;
        var block = new List<string>();
        foreach (var raw in config.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
            {
                if (NormalizeInterface(currentInterface).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase)) break;
                currentInterface = line[10..].Trim();
                block.Clear();
                continue;
            }
            if (NormalizeInterface(currentInterface).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                if (line == "!" || line.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
                block.Add(line);
            }
        }

        var joined = string.Join("\n", block);
        var address = Regex.Match(joined, @"(?im)^\s*ip address\s+(\S+)\s+(\S+)");
        var ipv6 = Regex.Match(joined, @"(?im)^\s*ipv6 address\s+(\S+)");
        var area = Regex.Match(joined, @"(?im)^\s*ip ospf\s+\S+\s+area\s+(\S+)");
        var vrf = Regex.Match(joined, @"(?im)^\s*(?:vrf forwarding|ip vrf forwarding)\s+(\S+)");
        var addressText = address.Success ? $"{address.Groups[1].Value} {address.Groups[2].Value}" : ipv6.Success ? ipv6.Groups[1].Value : string.Empty;
        return new InterfaceRoutingInfo(addressText, area.Success ? area.Groups[1].Value : string.Empty, vrf.Success ? vrf.Groups[1].Value : string.Empty);
    }

    private static string NormalizeInterface(string value) => Regex.Replace(value ?? string.Empty, @"\s+", string.Empty)
        .Replace("GigabitEthernet", "Gi", StringComparison.OrdinalIgnoreCase)
        .Replace("TenGigabitEthernet", "Te", StringComparison.OrdinalIgnoreCase)
        .Replace("FastEthernet", "Fa", StringComparison.OrdinalIgnoreCase)
        .Replace("Ethernet", "Eth", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public static class AclWorkspaceService
{
    public static (IReadOnlyList<ProjectAclRule> Rules, IReadOnlyList<ProjectAclBinding> Bindings) ImportFromProject(NetworkProject project)
    {
        var rules = new List<ProjectAclRule>();
        var bindings = new List<ProjectAclBinding>();
        foreach (var device in project.Devices)
        {
            ParseRules(device.Name, device.GeneratedConfiguration, rules);
            ParseBindings(device.Name, device.GeneratedConfiguration, bindings);
        }
        return (rules, bindings);
    }

    public static IReadOnlyList<AclFinding> Analyze(IEnumerable<ProjectAclRule> rules, IEnumerable<ProjectAclBinding> bindings)
    {
        var ruleList = rules.Where(rule => rule.Enabled).ToList();
        var bindingList = bindings.ToList();
        var findings = new List<AclFinding>();

        foreach (var group in ruleList.GroupBy(rule => $"{rule.DeviceName}\u001f{rule.AclName}", StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(rule => rule.Sequence).ThenBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase).ToList();
            var seenRules = new Dictionary<string, ProjectAclRule>(StringComparer.OrdinalIgnoreCase);
            var seenSequences = new HashSet<int>();
            var shadowingRule = (ProjectAclRule?)null;

            foreach (var rule in ordered)
            {
                if (!seenSequences.Add(rule.Sequence))
                    findings.Add(new AclFinding("Warnung", rule.DeviceName, rule.AclName, rule.Sequence, "Sequenznummer ist mehrfach vorhanden.", "Sequenznummern eindeutig vergeben."));

                var normalized = NormalizeRule(rule);
                if (seenRules.TryGetValue(normalized, out var duplicate))
                    findings.Add(new AclFinding("Warnung", rule.DeviceName, rule.AclName, rule.Sequence, $"Regel ist redundant zu Sequenz {duplicate.Sequence}.", "Doppelte Regel entfernen."));
                else
                    seenRules[normalized] = rule;

                if (shadowingRule != null)
                    findings.Add(new AclFinding("Fehler", rule.DeviceName, rule.AclName, rule.Sequence, $"Regel wird durch Sequenz {shadowingRule.Sequence} vollständig überschattet.", "Reihenfolge ändern oder überschattete Regel entfernen."));

                if (IsAnyAny(rule))
                {
                    if (rule.Action.Equals("permit", StringComparison.OrdinalIgnoreCase))
                        findings.Add(new AclFinding("Kritisch", rule.DeviceName, rule.AclName, rule.Sequence, "Sehr breite Freigabe 'permit ... any any'.", "Quell- und Zielbereich einschränken."));
                    shadowingRule ??= rule;
                }
            }

            var first = ordered.FirstOrDefault();
            if (first != null && !bindingList.Any(binding =>
                    binding.DeviceName.Equals(first.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                    binding.AclName.Equals(first.AclName, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new AclFinding("Hinweis", first.DeviceName, first.AclName, 0, "ACL ist keinem Interface zugeordnet.", "ACL binden oder nicht benötigte ACL entfernen."));
        }

        foreach (var binding in bindingList)
        {
            if (!ruleList.Any(rule => rule.DeviceName.Equals(binding.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                                      rule.AclName.Equals(binding.AclName, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new AclFinding("Fehler", binding.DeviceName, binding.AclName, 0, $"Interface {binding.Interface} verweist auf eine nicht vorhandene ACL.", "ACL anlegen oder Interface-Zuordnung korrigieren."));
        }

        return findings.OrderByDescending(finding => SeverityRank(finding.Severity)).ThenBy(finding => finding.DeviceName).ThenBy(finding => finding.AclName).ThenBy(finding => finding.Sequence).ToList();
    }

    public static string BuildConfiguration(IEnumerable<ProjectAclRule> rules, IEnumerable<ProjectAclBinding>? bindings = null)
    {
        var sb = new StringBuilder();
        foreach (var group in rules.Where(rule => rule.Enabled)
                     .GroupBy(rule => new { rule.DeviceName, rule.AddressFamily, rule.AclType, rule.AclName })
                     .OrderBy(group => group.Key.DeviceName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(group => group.Key.AclName, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"! Gerät: {group.Key.DeviceName}");
            if (group.Key.AddressFamily.Equals("IPv6", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"ipv6 access-list {group.Key.AclName}");
            else
                sb.AppendLine($"ip access-list {group.Key.AclType.ToLowerInvariant()} {group.Key.AclName}");

            foreach (var rule in group.OrderBy(rule => rule.Sequence))
            {
                if (!string.IsNullOrWhiteSpace(rule.Remark))
                {
                    sb.AppendLine($" {rule.Sequence} remark {rule.Remark.Trim()}");
                    continue;
                }

                var isStandard = group.Key.AclType.Equals("Standard", StringComparison.OrdinalIgnoreCase);
                var source = FormatEndpoint(rule.Source, rule.SourceWildcard);
                var protocol = isStandard ? string.Empty : " " + rule.Protocol.ToLowerInvariant();
                var destination = isStandard ? string.Empty : " " + FormatEndpoint(rule.Destination, rule.DestinationWildcard);
                var service = string.IsNullOrWhiteSpace(rule.Service) ? string.Empty : " " + rule.Service.Trim();
                sb.AppendLine($" {rule.Sequence} {rule.Action.ToLowerInvariant()}{protocol} {source}{destination}{service}".TrimEnd());
            }
            sb.AppendLine("exit");
            sb.AppendLine("!");
        }

        if (bindings != null)
        {
            foreach (var binding in bindings
                         .Where(binding => !string.IsNullOrWhiteSpace(binding.Interface) && !string.IsNullOrWhiteSpace(binding.AclName))
                         .OrderBy(binding => binding.DeviceName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(binding => binding.Interface, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"! Gerät: {binding.DeviceName}");
                sb.AppendLine($"interface {binding.Interface}");
                var command = binding.AddressFamily.Equals("IPv6", StringComparison.OrdinalIgnoreCase)
                    ? $" ipv6 traffic-filter {binding.AclName} {binding.Direction.ToLowerInvariant()}"
                    : $" ip access-group {binding.AclName} {binding.Direction.ToLowerInvariant()}";
                sb.AppendLine(command);
                sb.AppendLine("exit");
                sb.AppendLine("!");
            }
        }
        return sb.ToString();
    }

    private static void ParseRules(string deviceName, string config, List<ProjectAclRule> target)
    {
        string currentName = string.Empty;
        string currentType = "Extended";
        string addressFamily = "IPv4";
        var nextSequence = 10;

        foreach (var raw in (config ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            var named = Regex.Match(line, @"^ip access-list\s+(standard|extended)\s+(\S+)", RegexOptions.IgnoreCase);
            if (named.Success)
            {
                currentType = Capitalize(named.Groups[1].Value);
                currentName = named.Groups[2].Value;
                addressFamily = "IPv4";
                nextSequence = 10;
                continue;
            }
            var ipv6 = Regex.Match(line, @"^ipv6 access-list\s+(\S+)", RegexOptions.IgnoreCase);
            if (ipv6.Success)
            {
                currentType = "Extended";
                currentName = ipv6.Groups[1].Value;
                addressFamily = "IPv6";
                nextSequence = 10;
                continue;
            }
            if (line is "!" or "exit" or "end")
            {
                currentName = string.Empty;
                continue;
            }

            var legacy = Regex.Match(line, @"^access-list\s+(\S+)\s+(permit|deny|remark)\s+(.+)$", RegexOptions.IgnoreCase);
            if (legacy.Success)
            {
                ParseRuleBody(deviceName, legacy.Groups[1].Value, InferLegacyAclType(legacy.Groups[1].Value), "IPv4", nextSequence, legacy.Groups[2].Value + " " + legacy.Groups[3].Value, target);
                nextSequence += 10;
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentName)) continue;
            var sequenceMatch = Regex.Match(line, @"^(\d+)\s+(.+)$");
            var sequence = sequenceMatch.Success && int.TryParse(sequenceMatch.Groups[1].Value, out var parsed) ? parsed : nextSequence;
            var body = sequenceMatch.Success ? sequenceMatch.Groups[2].Value : line;
            ParseRuleBody(deviceName, currentName, currentType, addressFamily, sequence, body, target);
            nextSequence = Math.Max(nextSequence + 10, sequence + 10);
        }
    }

    private static void ParseRuleBody(string deviceName, string aclName, string aclType, string addressFamily, int sequence, string body, List<ProjectAclRule> target)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith("remark ", StringComparison.OrdinalIgnoreCase))
        {
            target.Add(new ProjectAclRule { DeviceName = deviceName, AclName = aclName, AclType = aclType, AddressFamily = addressFamily, Sequence = sequence, Remark = trimmed[7..].Trim() });
            return;
        }

        var tokens = Regex.Matches(trimmed, @"\S+").Cast<Match>().Select(match => match.Value).ToList();
        if (tokens.Count < 2 || !(tokens[0].Equals("permit", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("deny", StringComparison.OrdinalIgnoreCase))) return;

        var rule = new ProjectAclRule
        {
            DeviceName = deviceName,
            AclName = aclName,
            AclType = aclType,
            AddressFamily = addressFamily,
            Sequence = sequence,
            Action = tokens[0].ToLowerInvariant()
        };

        var index = 1;
        if (aclType.Equals("Standard", StringComparison.OrdinalIgnoreCase))
        {
            rule.Protocol = "ip";
            var sourceEndpoint = ParseEndpoint(tokens, index);
            rule.Source = sourceEndpoint.Address;
            rule.SourceWildcard = sourceEndpoint.Wildcard;
            index = sourceEndpoint.NextIndex;
            rule.Destination = "any";
        }
        else
        {
            rule.Protocol = index < tokens.Count ? tokens[index++] : "ip";
            var sourceEndpoint = ParseEndpoint(tokens, index);
            rule.Source = sourceEndpoint.Address;
            rule.SourceWildcard = sourceEndpoint.Wildcard;
            index = sourceEndpoint.NextIndex;
            var destinationEndpoint = ParseEndpoint(tokens, index);
            rule.Destination = destinationEndpoint.Address;
            rule.DestinationWildcard = destinationEndpoint.Wildcard;
            index = destinationEndpoint.NextIndex;
            if (index < tokens.Count) rule.Service = string.Join(" ", tokens.Skip(index));
        }
        target.Add(rule);
    }

    private static (string Address, string Wildcard, int NextIndex) ParseEndpoint(IReadOnlyList<string> tokens, int index)
    {
        if (index >= tokens.Count) return ("any", string.Empty, index);
        if (tokens[index].Equals("any", StringComparison.OrdinalIgnoreCase)) return ("any", string.Empty, index + 1);
        if (tokens[index].Equals("host", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Count) return ("host " + tokens[index + 1], string.Empty, index + 2);
        var address = tokens[index];
        if (index + 1 < tokens.Count && (Regex.IsMatch(tokens[index + 1], @"^\d+\.\d+\.\d+\.\d+$") || tokens[index + 1].StartsWith("/")))
            return (address, tokens[index + 1], index + 2);
        return (address, string.Empty, index + 1);
    }

    private static void ParseBindings(string deviceName, string config, List<ProjectAclBinding> target)
    {
        string currentInterface = string.Empty;
        foreach (var raw in (config ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
            {
                currentInterface = line[10..].Trim();
                continue;
            }
            if (line is "!" or "exit" or "end")
            {
                currentInterface = string.Empty;
                continue;
            }

            var ipv4 = Regex.Match(line, @"^ip access-group\s+(\S+)\s+(in|out)", RegexOptions.IgnoreCase);
            if (ipv4.Success)
                target.Add(new ProjectAclBinding { DeviceName = deviceName, Interface = currentInterface, AclName = ipv4.Groups[1].Value, Direction = ipv4.Groups[2].Value.ToUpperInvariant(), AddressFamily = "IPv4" });
            var ipv6 = Regex.Match(line, @"^ipv6 traffic-filter\s+(\S+)\s+(in|out)", RegexOptions.IgnoreCase);
            if (ipv6.Success)
                target.Add(new ProjectAclBinding { DeviceName = deviceName, Interface = currentInterface, AclName = ipv6.Groups[1].Value, Direction = ipv6.Groups[2].Value.ToUpperInvariant(), AddressFamily = "IPv6" });
        }
    }

    private static bool IsAnyAny(ProjectAclRule rule) =>
        EndpointIsAny(rule.Source) && EndpointIsAny(rule.Destination) &&
        (rule.Protocol.Equals("ip", StringComparison.OrdinalIgnoreCase) || rule.Protocol.Equals("ipv6", StringComparison.OrdinalIgnoreCase));

    private static bool EndpointIsAny(string value) => string.IsNullOrWhiteSpace(value) || value.Trim().Equals("any", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeRule(ProjectAclRule rule) => string.Join("|", rule.Action, rule.Protocol, rule.Source, rule.SourceWildcard, rule.Destination, rule.DestinationWildcard, rule.Service).ToLowerInvariant().Trim();
    private static int SeverityRank(string severity) => severity switch { "Kritisch" => 4, "Fehler" => 3, "Warnung" => 2, _ => 1 };
    private static string FormatEndpoint(string address, string wildcard) => string.Join(" ", new[] { string.IsNullOrWhiteSpace(address) ? "any" : address.Trim(), wildcard?.Trim() ?? string.Empty }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string Capitalize(string value) => string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    private static string InferLegacyAclType(string value) => int.TryParse(value, out var number) && (number is >= 1 and <= 99 or >= 1300 and <= 1999) ? "Standard" : "Extended";
}

public static class ProjectPackageExportService
{
    public static void Export(string zipPath, NetworkProject project, IEnumerable<DependencyFinding> dependencies, IEnumerable<SecurityFinding> security)
    {
        ArgumentNullException.ThrowIfNull(project);
        var tempRoot = Path.Combine(Path.GetTempPath(), "CiscoKonfigurator_Package_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var deviceFolder = Directory.CreateDirectory(Path.Combine(tempRoot, "devices")).FullName;
            var rollbackFolder = Directory.CreateDirectory(Path.Combine(tempRoot, "rollback")).FullName;
            var tableFolder = Directory.CreateDirectory(Path.Combine(tempRoot, "tables")).FullName;
            var analysisFolder = Directory.CreateDirectory(Path.Combine(tempRoot, "analysis")).FullName;
            var networkPlanFolder = Directory.CreateDirectory(Path.Combine(tempRoot, "network-plan")).FullName;

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(tempRoot, "project.ciscoproject.json"), JsonSerializer.Serialize(project, jsonOptions), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(tempRoot, "PACKAGE_CONTENTS.md"), BuildPackageReadme(project), new UTF8Encoding(false));

            foreach (var device in project.Devices)
            {
                var safeName = SafeName(device.Name);
                File.WriteAllText(Path.Combine(deviceFolder, safeName + ".txt"), device.GeneratedConfiguration ?? string.Empty, new UTF8Encoding(false));

                var latestBackup = project.Backups
                    .Where(backup => backup.DeviceName.Equals(device.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(backup.Content))
                    .OrderByDescending(backup => backup.CreatedUtc)
                    .FirstOrDefault();
                if (latestBackup != null)
                {
                    var diff = ConfigDiffService.Compare(latestBackup.Content, device.GeneratedConfiguration ?? string.Empty);
                    File.WriteAllText(Path.Combine(rollbackFolder, safeName + "_rollback.txt"), ConfigDiffService.CreateRollback(diff), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(rollbackFolder, safeName + "_previous_config.txt"), latestBackup.Content, new UTF8Encoding(false));
                }
            }

            ReportExportService.ExportHtml(Path.Combine(networkPlanFolder, "network-plan.html"), project, dependencies, security);
            File.WriteAllText(Path.Combine(networkPlanFolder, "network-diagram.svg"), NetworkDiagramService.BuildSvg(project), new UTF8Encoding(false));

            WriteIpamCsv(Path.Combine(tableFolder, "ipam.csv"), project.IpamEntries);
            WriteLinksCsv(Path.Combine(tableFolder, "links.csv"), project);
            WriteAclCsv(Path.Combine(tableFolder, "acl-rules.csv"), project.AclRules);
            WriteAclBindingsCsv(Path.Combine(tableFolder, "acl-bindings.csv"), project.AclBindings);
            WriteRoutesCsv(Path.Combine(tableFolder, "routes.csv"), RoutingVisualizationService.ExtractStaticRoutes(project));
            WriteProtocolsCsv(Path.Combine(tableFolder, "routing-protocols.csv"), RoutingVisualizationService.ExtractProtocols(project));

            File.WriteAllLines(Path.Combine(analysisFolder, "dependencies.txt"), dependencies.Select(item => $"[{item.Severity}] {item.Area}: {item.Message}"), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(analysisFolder, "security.txt"), security.Select(item => $"[{item.Severity}] {item.Category}: {item.Message} | {item.Recommendation}"), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(analysisFolder, "acl-analysis.txt"), AclWorkspaceService.Analyze(project.AclRules, project.AclBindings).Select(item => $"[{item.Severity}] {item.DeviceName} / {item.AclName} / {item.Sequence}: {item.Message} | {item.Recommendation}"), new UTF8Encoding(false));

            var manifest = new
            {
                project = project.Name,
                projectNumber = project.ProjectInfo?.ProjectNumber ?? string.Empty,
                exportedUtc = DateTime.UtcNow,
                devices = project.Devices.Count,
                links = project.Links.Count,
                ipamEntries = project.IpamEntries.Count,
                aclRules = project.AclRules.Count,
                files = Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(tempRoot, file).Replace('\\', '/')).OrderBy(file => file).ToList()
            };
            File.WriteAllText(Path.Combine(tempRoot, "manifest.json"), JsonSerializer.Serialize(manifest, jsonOptions), new UTF8Encoding(false));

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tempRoot, zipPath, CompressionLevel.Optimal, false);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string BuildPackageReadme(NetworkProject project) => $"""
# Cisco-Konfigurator – Projektpaket

Projekt: {project.Name}
Exportiert: {DateTime.Now:yyyy-MM-dd HH:mm}

## Inhalt

- `project.ciscoproject.json`: vollständige Projektdatei
- `devices/`: erzeugte Gerätekonfigurationen
- `rollback/`: Rollback-Entwürfe und vorherige Konfigurationen, sofern Backups vorhanden sind
- `network-plan/`: HTML-Netzplan und SVG-Diagramm
- `tables/`: IPAM, Links, ACLs, Routen und Routingprotokolle als CSV
- `analysis/`: Abhängigkeits-, Sicherheits- und ACL-Analyse
- `manifest.json`: maschinenlesbare Paketübersicht

> Alle Konfigurationen und Rollback-Entwürfe müssen vor einem produktiven Einsatz geprüft werden.
""";

    private static void WriteIpamCsv(string path, IEnumerable<IpamEntry> entries)
    {
        var lines = new List<string> { "Network;Prefix;VLAN;Gateway;DHCPStart;DHCPEnd;Device;Interface;Description" };
        lines.AddRange(entries.Select(item => string.Join(";", Csv(item.Network), item.PrefixLength.ToString(), Csv(item.Vlan), Csv(item.Gateway), Csv(item.DhcpStart), Csv(item.DhcpEnd), Csv(item.Device), Csv(item.Interface), Csv(item.Description))));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static void WriteLinksCsv(string path, NetworkProject project)
    {
        var lines = new List<string> { "SourceDevice;SourceInterface;TargetDevice;TargetInterface;Type;Description;DiscoverySource" };
        foreach (var link in project.Links)
        {
            var source = project.Devices.FirstOrDefault(device => device.Id == link.SourceDeviceId)?.Name ?? link.SourceDeviceId;
            var target = project.Devices.FirstOrDefault(device => device.Id == link.TargetDeviceId)?.Name ?? link.TargetDeviceId;
            lines.Add(string.Join(";", Csv(source), Csv(link.SourceInterface), Csv(target), Csv(link.TargetInterface), Csv(link.LinkType), Csv(link.Description), Csv(link.DiscoverySource)));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static void WriteAclCsv(string path, IEnumerable<ProjectAclRule> rules)
    {
        var lines = new List<string> { "Device;ACL;Family;Type;Sequence;Action;Protocol;Source;SourceWildcard;Destination;DestinationWildcard;Service;Remark;Enabled" };
        lines.AddRange(rules.Select(rule => string.Join(";", Csv(rule.DeviceName), Csv(rule.AclName), Csv(rule.AddressFamily), Csv(rule.AclType), rule.Sequence.ToString(), Csv(rule.Action), Csv(rule.Protocol), Csv(rule.Source), Csv(rule.SourceWildcard), Csv(rule.Destination), Csv(rule.DestinationWildcard), Csv(rule.Service), Csv(rule.Remark), rule.Enabled.ToString())));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static void WriteAclBindingsCsv(string path, IEnumerable<ProjectAclBinding> bindings)
    {
        var lines = new List<string> { "Device;Interface;ACL;Direction;Family" };
        lines.AddRange(bindings.Select(binding => string.Join(";", Csv(binding.DeviceName), Csv(binding.Interface), Csv(binding.AclName), Csv(binding.Direction), Csv(binding.AddressFamily))));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static void WriteRoutesCsv(string path, IEnumerable<StaticRouteInfo> routes)
    {
        var lines = new List<string> { "Device;VRF;Network;MaskOrPrefix;NextHop;Family" };
        lines.AddRange(routes.Select(route => string.Join(";", Csv(route.Device), Csv(route.Vrf), Csv(route.Network), Csv(route.MaskOrPrefix), Csv(route.NextHop), Csv(route.AddressFamily))));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static void WriteProtocolsCsv(string path, IEnumerable<RoutingProtocolInfo> protocols)
    {
        var lines = new List<string> { "Device;Protocol;Instance;Details" };
        lines.AddRange(protocols.Select(protocol => string.Join(";", Csv(protocol.Device), Csv(protocol.Protocol), Csv(protocol.Instance), Csv(protocol.Details))));
        File.WriteAllLines(path, lines, new UTF8Encoding(true));
    }

    private static string Csv(object? value) => '"' + (value?.ToString() ?? string.Empty).Replace("\"", "\"\"") + '"';
    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string((value ?? "device").Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "device" : result;
    }
}
