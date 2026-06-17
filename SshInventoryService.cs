using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public sealed record SshInventoryCollectionResult(
    bool Success,
    DeviceInventorySnapshot Inventory,
    IReadOnlyList<string> Errors);

public static class SshInventoryService
{
    private static readonly IReadOnlyList<(string Key, string Command)> InventoryCommands = new List<(string, string)>
    {
        ("hostname", "show running-config | include ^hostname"),
        ("version", "show version"),
        ("inventory", "show inventory"),
        ("interfaces-ipv4", "show ip interface brief"),
        ("interfaces-ipv6", "show ipv6 interface brief"),
        ("interfaces-status", "show interfaces status"),
        ("interfaces-description", "show interfaces description"),
        ("interface-config", "show running-config | section ^interface"),
        ("vlans", "show vlan brief"),
        ("trunks", "show interfaces trunk"),
        ("portchannels", "show etherchannel summary"),
        ("cdp", "show cdp neighbors detail"),
        ("lldp", "show lldp neighbors detail"),
        ("routes", "show ip route"),
        ("ospf", "show ip ospf neighbor"),
        ("bgp", "show bgp ipv4 unicast summary"),
        ("vrf", "show vrf"),
        ("hsrp", "show standby brief")
    };

    public static async Task<SshInventoryCollectionResult> CollectAsync(
        SshConnectionSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var index = 0;
        foreach (var (key, command) in InventoryCommands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report($"[{index}/{InventoryCommands.Count}] {command}");
            var result = await SshDeviceService.RunCommandsAsync(settings, new[] { command }, cancellationToken);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                outputs[key] = CleanOutput(result.Output, command);
            }
            else
            {
                var error = string.IsNullOrWhiteSpace(result.Error) ? $"Exit code {result.ExitCode}" : result.Error.Trim();
                errors.Add($"{command}: {error}");
                outputs[key] = result.Output ?? string.Empty;
            }
        }

        var inventory = Parse(outputs, settings.Host);
        var success = !string.IsNullOrWhiteSpace(inventory.Hostname) || !string.IsNullOrWhiteSpace(inventory.Model) || inventory.Interfaces.Count > 0;
        return new SshInventoryCollectionResult(success, inventory, errors);
    }

    public static DeviceInventorySnapshot Parse(IReadOnlyDictionary<string, string> outputs, string sourceHost)
    {
        string Get(string key) => outputs.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        var versionText = Get("version");
        var inventoryText = Get("inventory");
        var hostnameText = Get("hostname");
        var interfaceConfig = Get("interface-config");
        var snapshot = new DeviceInventorySnapshot
        {
            CollectedUtc = DateTime.UtcNow,
            SourceHost = sourceHost,
            Hostname = FirstGroup(hostnameText, @"(?im)^\s*hostname\s+(\S+)") ??
                       FirstGroup(versionText, @"(?im)^\s*([A-Za-z0-9_.-]+)\s+uptime is\s+" ) ??
                       FirstGroup(versionText, @"(?m)^([A-Za-z0-9_.-]+)[>#]") ?? sourceHost,
            SoftwareVersion = FirstGroup(versionText, @"(?im)Cisco IOS XE Software, Version\s+([^,\s]+)") ??
                              FirstGroup(versionText, @"(?im)Cisco IOS Software.*?Version\s+([^,\s]+)") ??
                              FirstGroup(versionText, @"(?im)^\s*Version\s+([^,\s]+)") ?? string.Empty,
            Model = FirstGroup(versionText, @"(?im)^\s*cisco\s+(\S+)\s+\(.+processor") ??
                    FirstGroup(versionText, @"(?im)^\s*Model Number\s*:\s*(\S+)") ??
                    FirstGroup(inventoryText, @"(?im)^\s*PID:\s*([^,\s]+)") ?? string.Empty,
            SerialNumber = FirstGroup(versionText, @"(?im)Processor board ID\s+(\S+)") ??
                           FirstGroup(inventoryText, @"(?im)SN:\s*([^,\s]+)") ?? string.Empty,
            Uptime = FirstGroup(versionText, @"(?im)^\s*\S+\s+uptime is\s+(.+)$") ?? string.Empty,
            Platform = versionText.Contains("IOS XE", StringComparison.OrdinalIgnoreCase) ? "IOS-XE" :
                       versionText.Contains("Cisco IOS", StringComparison.OrdinalIgnoreCase) ? "IOS" : string.Empty,
            InterfaceConfiguration = interfaceConfig,
            RawOutputs = outputs.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        };
        snapshot.DeviceType = InferDeviceType(snapshot.Model, Get("routes"), Get("vlans"));

        var interfaces = ParseInterfaces(Get("interfaces-ipv4"), Get("interfaces-ipv6"), Get("interfaces-status"), Get("interfaces-description"), interfaceConfig);
        foreach (var item in interfaces) snapshot.Interfaces.Add(item);
        foreach (var vlan in ParseVlans(Get("vlans"))) snapshot.Vlans.Add(vlan);
        foreach (var trunk in ParseTableLines(Get("trunks"), new[] { "Port", "Vlans allowed", "Vlans in spanning" })) snapshot.Trunks.Add(trunk);
        foreach (var channel in ParsePortChannels(Get("portchannels"))) snapshot.PortChannels.Add(channel);

        var neighborText = string.Join("\n", new[] { Get("cdp"), Get("lldp") }.Where(x => !string.IsNullOrWhiteSpace(x)));
        foreach (var neighbor in TopologyPlanningService.ParseDiscoveryNeighbors(neighborText)
                     .GroupBy(x => $"{x.Protocol}|{x.RemoteDevice}|{x.LocalInterface}|{x.RemoteInterface}", StringComparer.OrdinalIgnoreCase)
                     .Select(x => x.First()))
        {
            snapshot.Neighbors.Add(new InventoryNeighborEntry
            {
                Protocol = neighbor.Protocol,
                RemoteDevice = neighbor.RemoteDevice,
                LocalInterface = neighbor.LocalInterface,
                RemoteInterface = neighbor.RemoteInterface
            });
        }

        foreach (var protocol in DetectRoutingProtocols(outputs)) snapshot.RoutingProtocols.Add(protocol);
        return snapshot;
    }

    public static string BuildSummary(DeviceInventorySnapshot inventory, IReadOnlyList<string> errors, bool english)
    {
        string R(string de, string en) => english ? en : de;
        var sb = new StringBuilder();
        sb.AppendLine(R("SSH-Live-Inventarisierung", "SSH live inventory"));
        sb.AppendLine(new string('=', 64));
        sb.AppendLine($"{R("Hostname", "Hostname")}: {inventory.Hostname}");
        sb.AppendLine($"{R("Gerätetyp", "Device type")}: {inventory.DeviceType}");
        sb.AppendLine($"{R("Modell", "Model")}: {inventory.Model}");
        sb.AppendLine($"{R("Seriennummer", "Serial number")}: {inventory.SerialNumber}");
        sb.AppendLine($"{R("Software", "Software")}: {inventory.Platform} {inventory.SoftwareVersion}".Trim());
        sb.AppendLine($"{R("Uptime", "Uptime")}: {inventory.Uptime}");
        sb.AppendLine($"{R("Erfasst", "Collected")}: {inventory.CollectedUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"{R("Interfaces", "Interfaces")}: {inventory.Interfaces.Count}");
        sb.AppendLine($"VLANs: {inventory.Vlans.Count}");
        sb.AppendLine($"{R("Trunks", "Trunks")}: {inventory.Trunks.Count}");
        sb.AppendLine($"Port-Channels: {inventory.PortChannels.Count}");
        sb.AppendLine($"{R("Nachbarn", "Neighbors")}: {inventory.Neighbors.Count}");
        sb.AppendLine($"{R("Routingprotokolle", "Routing protocols")}: {(inventory.RoutingProtocols.Count == 0 ? "-" : string.Join(", ", inventory.RoutingProtocols))}");
        if (errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(R("Nicht erfolgreiche Befehle", "Commands with errors"));
            foreach (var error in errors) sb.AppendLine($"- {error}");
        }
        return sb.ToString();
    }

    private static IReadOnlyList<InventoryInterfaceEntry> ParseInterfaces(
        string ipv4Text,
        string ipv6Text,
        string statusText,
        string descriptionText,
        string interfaceConfig)
    {
        var map = new Dictionary<string, InventoryInterfaceEntry>(StringComparer.OrdinalIgnoreCase);
        InventoryInterfaceEntry GetOrCreate(string name)
        {
            if (!map.TryGetValue(name, out var entry))
            {
                entry = new InventoryInterfaceEntry { Name = name };
                map[name] = entry;
            }
            return entry;
        }

        foreach (var raw in Lines(ipv4Text))
        {
            var match = Regex.Match(raw, @"^\s*(\S+)\s+(\S+)\s+(?:YES|NO|NVRAM|unset|manual|DHCP)\s+\S+\s+(administratively down|up|down|reset|deleted)\s+(up|down|reset|deleted)\s*$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var entry = GetOrCreate(match.Groups[1].Value);
            entry.IpAddress = match.Groups[2].Value.Equals("unassigned", StringComparison.OrdinalIgnoreCase) ? string.Empty : match.Groups[2].Value;
            entry.Status = match.Groups[3].Value;
            entry.Protocol = match.Groups[4].Value;
        }

        string currentInterface = string.Empty;
        foreach (var raw in Lines(ipv6Text))
        {
            var interfaceMatch = Regex.Match(raw, @"^\s*(\S+)\s+\[(up|down|administratively down)[^\]]*\]", RegexOptions.IgnoreCase);
            if (interfaceMatch.Success)
            {
                currentInterface = interfaceMatch.Groups[1].Value;
                var entry = GetOrCreate(currentInterface);
                if (string.IsNullOrWhiteSpace(entry.Status)) entry.Status = interfaceMatch.Groups[2].Value;
                continue;
            }
            var addressMatch = Regex.Match(raw, @"^\s*([0-9a-f:]+(?:/\d+)?)\s*$", RegexOptions.IgnoreCase);
            if (addressMatch.Success && !string.IsNullOrWhiteSpace(currentInterface) && !addressMatch.Groups[1].Value.StartsWith("FE80", StringComparison.OrdinalIgnoreCase))
                GetOrCreate(currentInterface).Ipv6Address = addressMatch.Groups[1].Value;
        }

        foreach (var raw in Lines(descriptionText))
        {
            var match = Regex.Match(raw, @"^\s*(\S+)\s+(up|down|admin down|administratively down)\s+(up|down)\s*(.*)$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var entry = GetOrCreate(match.Groups[1].Value);
            entry.Description = match.Groups[4].Value.Trim();
        }

        foreach (var raw in Lines(statusText))
        {
            var match = Regex.Match(raw, @"^\s*(\S+)\s+(.{0,20}?)\s+(connected|notconnect|disabled|err-disabled|inactive|sfpAbsent|monitoring)\s+(\S+)\s+(\S+)\s+(\S+)\s*(.*)$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var entry = GetOrCreate(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(entry.Description)) entry.Description = match.Groups[2].Value.Trim();
            entry.Status = match.Groups[3].Value;
            entry.Vlan = match.Groups[4].Value;
            entry.Duplex = match.Groups[5].Value;
            entry.Speed = match.Groups[6].Value;
            entry.Type = match.Groups[7].Value.Trim();
        }

        foreach (var port in PortPlanService.Parse(interfaceConfig))
        {
            var entry = GetOrCreate(port.Interface);
            if (string.IsNullOrWhiteSpace(entry.Description)) entry.Description = port.Description;
            if (string.IsNullOrWhiteSpace(entry.IpAddress)) entry.IpAddress = port.IpAddress;
            if (string.IsNullOrWhiteSpace(entry.Vlan)) entry.Vlan = !string.IsNullOrWhiteSpace(port.AccessVlan) ? port.AccessVlan : port.NativeVlan;
        }

        return map.Values.OrderBy(x => NaturalKey(x.Name), StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<InventoryVlanEntry> ParseVlans(string text)
    {
        foreach (var raw in Lines(text))
        {
            var match = Regex.Match(raw, @"^\s*(\d{1,4})\s+(\S+)\s+(active|act/unsup|suspended|shutdown)\s*(.*)$", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            yield return new InventoryVlanEntry
            {
                VlanId = match.Groups[1].Value,
                Name = match.Groups[2].Value,
                Status = match.Groups[3].Value,
                Ports = match.Groups[4].Value.Trim()
            };
        }
    }

    private static IEnumerable<string> ParsePortChannels(string text)
    {
        foreach (var raw in Lines(text))
        {
            var match = Regex.Match(raw, @"^\s*(\d+)\s+(\S+\([^)]*\))\s+(\S+)\s+(.+)$");
            if (match.Success) yield return $"Group {match.Groups[1].Value}: {match.Groups[2].Value} | {match.Groups[3].Value} | {match.Groups[4].Value.Trim()}";
        }
    }

    private static IEnumerable<string> ParseTableLines(string text, IEnumerable<string> ignoredPrefixes)
    {
        foreach (var raw in Lines(text))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.All(c => c is '-' or ' ')) continue;
            if (ignoredPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
            if (Regex.IsMatch(line, @"^(show |terminal length|\S+[>#])", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(line, @"^(Gi|Te|Fa|Eth|Po|Twe|Fo|Hu)\S+", RegexOptions.IgnoreCase)) yield return line;
        }
    }

    private static IEnumerable<string> DetectRoutingProtocols(IReadOnlyDictionary<string, string> outputs)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var routes = outputs.TryGetValue("routes", out var routeText) ? routeText : string.Empty;
        if (Regex.IsMatch(routes, @"(?m)^O(?:\s| IA| E1| E2| N1| N2)", RegexOptions.IgnoreCase) || NonEmpty(outputs, "ospf")) result.Add("OSPF");
        if (Regex.IsMatch(routes, @"(?m)^D(?:\s| EX)", RegexOptions.IgnoreCase)) result.Add("EIGRP");
        if (Regex.IsMatch(routes, @"(?m)^B\s", RegexOptions.IgnoreCase) || NonEmpty(outputs, "bgp")) result.Add("BGP");
        if (Regex.IsMatch(routes, @"(?m)^i\s", RegexOptions.IgnoreCase)) result.Add("IS-IS");
        if (NonEmpty(outputs, "vrf")) result.Add("VRF");
        if (NonEmpty(outputs, "hsrp")) result.Add("HSRP");
        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private static bool NonEmpty(IReadOnlyDictionary<string, string> outputs, string key) =>
        outputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) &&
        !value.Contains("Invalid input", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("not enabled", StringComparison.OrdinalIgnoreCase);

    private static string InferDeviceType(string model, string routes, string vlans)
    {
        if (Regex.IsMatch(model ?? string.Empty, @"(?i)(ISR|ASR|C8\d{3}|C1\d{3}|RV\d+|CSR|IR\d+)")) return "Router";
        if (!string.IsNullOrWhiteSpace(routes) && !routes.Contains("IP routing not enabled", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(routes, @"(?im)^Gateway of last resort|^Codes:")) return "L3-Switch";
        if (!string.IsNullOrWhiteSpace(vlans)) return "L2-Switch";
        return "Router";
    }

    private static string CleanOutput(string output, string command)
    {
        var ansiRemoved = Regex.Replace(output ?? string.Empty, "\\x1B\\[[0-9;?]*[ -/]*[@-~]", string.Empty);
        var lines = Lines(ansiRemoved)
            .Where(line => !line.Trim().Equals("terminal length 0", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Trim().Equals(command, StringComparison.OrdinalIgnoreCase))
            .Where(line => !Regex.IsMatch(line.Trim(), @"^[A-Za-z0-9_.()-]+[>#]\s*(?:exit)?$"))
            .ToList();
        return string.Join("\n", lines).Trim();
    }

    private static string? FirstGroup(string text, string pattern)
    {
        var match = Regex.Match(text ?? string.Empty, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static IEnumerable<string> Lines(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string NaturalKey(string value) => Regex.Replace(value ?? string.Empty, @"\d+", m => m.Value.PadLeft(10, '0'));
}
