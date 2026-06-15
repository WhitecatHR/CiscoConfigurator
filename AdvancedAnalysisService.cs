using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public static class IpamService
{
    private sealed record RangeInfo(IpamEntry Entry, int Bits, BigInteger Start, BigInteger End);

    public static IReadOnlyList<string> Validate(IEnumerable<IpamEntry> entries)
    {
        var findings = new List<string>();
        var parsed = new List<RangeInfo>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Network)) continue;
            if (!IPAddress.TryParse(entry.Network.Trim(), out var ip))
            {
                findings.Add($"Ungültige Netzadresse: {entry.Network}");
                continue;
            }

            var bits = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            if (entry.PrefixLength < 0 || entry.PrefixLength > bits)
            {
                findings.Add($"Ungültiges Präfix bei {entry.Network}: /{entry.PrefixLength}");
                continue;
            }

            var value = ToBigInteger(ip);
            var hostBits = bits - entry.PrefixLength;
            var block = BigInteger.One << hostBits;
            var start = value / block * block;
            var end = start + block - 1;
            parsed.Add(new RangeInfo(entry, bits, start, end));

            ValidateAddressInRange(entry.Gateway, "Gateway", entry, bits, start, end, findings);
            ValidateAddressInRange(entry.DhcpStart, "DHCP-Start", entry, bits, start, end, findings);
            ValidateAddressInRange(entry.DhcpEnd, "DHCP-Ende", entry, bits, start, end, findings);
        }

        for (var i = 0; i < parsed.Count; i++)
        for (var j = i + 1; j < parsed.Count; j++)
        {
            var a = parsed[i];
            var b = parsed[j];
            if (a.Bits != b.Bits) continue;
            if (a.Start <= b.End && b.Start <= a.End)
                findings.Add($"Netzüberschneidung: {a.Entry.Network}/{a.Entry.PrefixLength} und {b.Entry.Network}/{b.Entry.PrefixLength}");
        }

        var duplicateGateways = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Gateway))
            .GroupBy(x => x.Gateway.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var group in duplicateGateways)
            findings.Add($"Gateway mehrfach vergeben: {group.Key} ({group.Count()} Einträge)");

        return findings;
    }

    public static IReadOnlyList<IpamEntry> ImportFromConfiguration(string config, string deviceName)
    {
        var result = new List<IpamEntry>();
        var currentInterface = string.Empty;
        foreach (var raw in Lines(config))
        {
            var line = raw.Trim();
            if (line.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
            {
                currentInterface = line[10..].Trim();
                continue;
            }

            var match4 = Regex.Match(line, @"^ip address\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase);
            if (match4.Success && IPAddress.TryParse(match4.Groups[1].Value, out var ip4) && IPAddress.TryParse(match4.Groups[2].Value, out var mask))
            {
                var prefix = MaskToPrefix(mask);
                var network = GetNetworkAddress(ip4, prefix);
                result.Add(new IpamEntry
                {
                    Network = network.ToString(), PrefixLength = prefix, Gateway = ip4.ToString(),
                    Device = deviceName, Interface = currentInterface,
                    Vlan = ExtractVlan(currentInterface), Description = "Aus Konfiguration importiert"
                });
                continue;
            }

            var match6 = Regex.Match(line, @"^ipv6 address\s+([^\s/]+)/([0-9]{1,3})", RegexOptions.IgnoreCase);
            if (match6.Success && IPAddress.TryParse(match6.Groups[1].Value, out var ip6) && int.TryParse(match6.Groups[2].Value, out var prefix6))
            {
                result.Add(new IpamEntry
                {
                    Network = GetNetworkAddress(ip6, prefix6).ToString(), PrefixLength = prefix6,
                    Gateway = ip6.ToString(), Device = deviceName, Interface = currentInterface,
                    Vlan = ExtractVlan(currentInterface), Description = "Aus Konfiguration importiert"
                });
            }
        }
        return result;
    }

    private static void ValidateAddressInRange(string address, string label, IpamEntry entry, int bits, BigInteger start, BigInteger end, List<string> findings)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        if (!IPAddress.TryParse(address.Trim(), out var ip))
        {
            findings.Add($"Ungültige Adresse bei {label}: {address}");
            return;
        }
        var addressBits = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        var value = ToBigInteger(ip);
        if (addressBits != bits || value < start || value > end)
            findings.Add($"{label} {address} liegt nicht in {entry.Network}/{entry.PrefixLength}");
    }

    private static BigInteger ToBigInteger(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        Array.Reverse(bytes);
        return new BigInteger(bytes.Concat(new byte[] { 0 }).ToArray());
    }

    private static IPAddress GetNetworkAddress(IPAddress ip, int prefix)
    {
        var bytes = ip.GetAddressBytes();
        for (var bit = prefix; bit < bytes.Length * 8; bit++)
            bytes[bit / 8] &= (byte)~(1 << (7 - bit % 8));
        return new IPAddress(bytes);
    }

    private static int MaskToPrefix(IPAddress mask)
    {
        var count = 0;
        foreach (var b in mask.GetAddressBytes())
            count += Convert.ToString(b, 2).Count(c => c == '1');
        return count;
    }

    private static string ExtractVlan(string interfaceName)
    {
        var match = Regex.Match(interfaceName ?? string.Empty, @"^Vlan(\d+)$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static IEnumerable<string> Lines(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}

public static class PortPlanService
{
    private sealed class MutablePort
    {
        public string Interface = "";
        public string Description = "";
        public string Mode = "";
        public string AccessVlan = "";
        public string VoiceVlan = "";
        public string AllowedVlans = "";
        public string NativeVlan = "";
        public string ChannelGroup = "";
        public string IpAddress = "";
        public string State = "up";
        public readonly List<string> Stp = new();
    }

    public static IReadOnlyList<PortPlanEntry> Parse(string config)
    {
        var ports = new Dictionary<string, MutablePort>(StringComparer.OrdinalIgnoreCase);
        MutablePort? current = null;
        foreach (var raw in (config ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
            {
                var name = line[10..].Trim();
                if (name.StartsWith("range ", StringComparison.OrdinalIgnoreCase)) name = "Range " + name[6..].Trim();
                if (!ports.TryGetValue(name, out current))
                {
                    current = new MutablePort { Interface = name };
                    ports[name] = current;
                }
                continue;
            }
            if (current == null || line is "!" or "exit" or "end")
            {
                if (line is "!" or "exit" or "end") current = null;
                continue;
            }

            if (line.StartsWith("description ", StringComparison.OrdinalIgnoreCase)) current.Description = line[12..];
            else if (line.Equals("switchport mode access", StringComparison.OrdinalIgnoreCase)) current.Mode = "Access";
            else if (line.Equals("switchport mode trunk", StringComparison.OrdinalIgnoreCase)) current.Mode = "Trunk";
            else if (line.Equals("no switchport", StringComparison.OrdinalIgnoreCase)) current.Mode = "Routed";
            else if (line.StartsWith("switchport access vlan ", StringComparison.OrdinalIgnoreCase)) current.AccessVlan = line[23..].Trim();
            else if (line.StartsWith("switchport voice vlan ", StringComparison.OrdinalIgnoreCase)) current.VoiceVlan = line[22..].Trim();
            else if (line.StartsWith("switchport trunk allowed vlan ", StringComparison.OrdinalIgnoreCase)) current.AllowedVlans = line[30..].Trim();
            else if (line.StartsWith("switchport trunk native vlan ", StringComparison.OrdinalIgnoreCase)) current.NativeVlan = line[29..].Trim();
            else if (line.StartsWith("channel-group ", StringComparison.OrdinalIgnoreCase)) current.ChannelGroup = line[14..].Trim();
            else if (line.StartsWith("ip address ", StringComparison.OrdinalIgnoreCase)) current.IpAddress = line[11..].Trim();
            else if (line.StartsWith("ipv6 address ", StringComparison.OrdinalIgnoreCase)) current.IpAddress = string.Join("; ", new[] { current.IpAddress, line[13..].Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)));
            else if (line.Equals("shutdown", StringComparison.OrdinalIgnoreCase)) current.State = "shutdown";
            else if (line.Equals("no shutdown", StringComparison.OrdinalIgnoreCase)) current.State = "up";
            else if (line.StartsWith("spanning-tree ", StringComparison.OrdinalIgnoreCase)) current.Stp.Add(line[14..].Trim());
        }

        return ports.Values.OrderBy(x => NaturalKey(x.Interface), StringComparer.OrdinalIgnoreCase).Select(x => new PortPlanEntry(
            x.Interface, x.Description, x.Mode, x.AccessVlan, x.VoiceVlan, x.AllowedVlans, x.NativeVlan,
            x.ChannelGroup, x.IpAddress, x.State, string.Join("; ", x.Stp))).ToList();
    }

    private static string NaturalKey(string value) => Regex.Replace(value ?? string.Empty, @"\d+", m => m.Value.PadLeft(10, '0'));
}

public static class ConfigDiffService
{
    public static IReadOnlyList<ConfigDiffLine> Compare(string oldConfig, string newConfig)
    {
        var oldLines = Normalize(oldConfig);
        var newLines = Normalize(newConfig);
        var lcs = BuildLcs(oldLines.Select(x => x.Line).ToArray(), newLines.Select(x => x.Line).ToArray());
        var result = new List<ConfigDiffLine>();
        var i = 0; var j = 0;
        while (i < oldLines.Count || j < newLines.Count)
        {
            if (i < oldLines.Count && j < newLines.Count && oldLines[i].Line.Equals(newLines[j].Line, StringComparison.OrdinalIgnoreCase))
            { i++; j++; continue; }
            if (j < newLines.Count && (i == oldLines.Count || lcs[i, j + 1] >= lcs[i + 1, j]))
            {
                result.Add(new ConfigDiffLine("Hinzugefügt", newLines[j].Context, newLines[j].Line, 0, newLines[j].Number));
                j++;
            }
            else if (i < oldLines.Count)
            {
                result.Add(new ConfigDiffLine("Entfernt", oldLines[i].Context, oldLines[i].Line, oldLines[i].Number, 0));
                i++;
            }
        }
        return result;
    }

    public static string CreateRollback(IEnumerable<ConfigDiffLine> diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine("configure terminal");
        string currentContext = "global";
        foreach (var item in diff.Reverse())
        {
            if (!item.Context.Equals(currentContext, StringComparison.OrdinalIgnoreCase))
            {
                if (!currentContext.Equals("global", StringComparison.OrdinalIgnoreCase)) sb.AppendLine("exit");
                currentContext = item.Context;
                if (!currentContext.Equals("global", StringComparison.OrdinalIgnoreCase)) sb.AppendLine(currentContext);
            }
            if (item.Change == "Hinzugefügt")
            {
                var command = item.Line.Trim();
                sb.AppendLine(command.StartsWith("no ", StringComparison.OrdinalIgnoreCase) ? " " + command[3..] : " no " + command);
            }
            else sb.AppendLine(" " + item.Line);
        }
        if (!currentContext.Equals("global", StringComparison.OrdinalIgnoreCase)) sb.AppendLine("exit");
        sb.AppendLine("end");
        return sb.ToString();
    }

    private sealed record LineInfo(string Context, string Line, int Number);

    private static List<LineInfo> Normalize(string config)
    {
        var result = new List<LineInfo>();
        var context = "global";
        var source = (config ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < source.Length; index++)
        {
            var line = Regex.Replace(source[index].Trim(), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("!")) continue;
            if (IsContext(line)) { context = line; continue; }
            if (line is "exit" or "end") { context = "global"; continue; }
            if (line is "configure terminal" or "enable" or "write memory") continue;
            result.Add(new LineInfo(context, line, index + 1));
        }
        return result;
    }

    private static bool IsContext(string line) => new[] { "interface ", "router ", "line ", "vlan ", "ip access-list ", "ipv6 access-list ", "ip dhcp pool ", "route-map ", "vrf definition " }.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static int[,] BuildLcs(string[] a, string[] b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        for (var j = b.Length - 1; j >= 0; j--)
            table[i, j] = a[i].Equals(b[j], StringComparison.OrdinalIgnoreCase) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
        return table;
    }
}
