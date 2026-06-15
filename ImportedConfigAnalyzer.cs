using System.Text;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public sealed class ImportedConfigAnalysis
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> Modules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ImportedUnknownCommand> UnknownCommands { get; } = new();
    public List<string> Notes { get; } = new();
    public int TotalCommands { get; set; }
    public int KnownCommands { get; set; }
    public int AppliedFields => Values.Count;
    public int ActiveModules => Modules.Count(x => x.Value);
}

public sealed record ImportedUnknownCommand(int LineNumber, string Context, string Command, string Note);

internal static class ImportedConfigAnalyzer
{
    private sealed class InterfaceInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Mask { get; set; } = string.Empty;
        public string Vrf { get; set; } = string.Empty;
        public string Ipv6 { get; set; } = string.Empty;
        public string AccessVlan { get; set; } = string.Empty;
        public string VoiceVlan { get; set; } = string.Empty;
        public string AllowedVlans { get; set; } = string.Empty;
        public string NativeVlan { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public bool PortFast { get; set; }
        public bool PortFastTrunk { get; set; }
        public bool BpduGuard { get; set; }
        public bool BpduFilter { get; set; }
        public string StpGuard { get; set; } = string.Empty;
        public string StpLinkType { get; set; } = string.Empty;
        public string StpPortPriority { get; set; } = string.Empty;
        public string StpCost { get; set; } = string.Empty;
        public Dictionary<string, string[]> StpVlanParameters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> StpMstParameters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> StpResetParameters { get; } = new();
        public bool PortSecurity { get; set; }
        public bool NatInside { get; set; }
        public bool NatOutside { get; set; }
        public bool Shutdown { get; set; }
        public bool Qinq { get; set; }
        public string L2ProtocolTunnel { get; set; } = string.Empty;
        public bool Mpls { get; set; }
        public string MplsMtu { get; set; } = string.Empty;
        public bool LdpIgpSync { get; set; }
        public bool Nonegotiate { get; set; }
        public bool NoIpAddress { get; set; }
        public string OspfProcess { get; set; } = string.Empty;
        public string OspfArea { get; set; } = string.Empty;
        public string OspfCost { get; set; } = string.Empty;
        public string OspfPriority { get; set; } = string.Empty;
        public string OspfNetworkType { get; set; } = string.Empty;
    }

    private sealed class VlanInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DhcpPoolInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public string Mask { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;
        public string Dns { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Lease { get; set; } = string.Empty;
        public string Option150 { get; set; } = string.Empty;
    }

    public static ImportedConfigAnalysis Analyze(string? config)
    {
        var result = new ImportedConfigAnalysis();
        if (string.IsNullOrWhiteSpace(config))
        {
            result.Notes.Add("Keine Konfiguration eingefügt.");
            return result;
        }

        var interfaces = new Dictionary<string, InterfaceInfo>(StringComparer.OrdinalIgnoreCase);
        var vlans = new Dictionary<string, VlanInfo>(StringComparer.OrdinalIgnoreCase);
        var dhcpPools = new Dictionary<string, DhcpPoolInfo>(StringComparer.OrdinalIgnoreCase);
        var staticRoutes = new List<string>();
        var ipv6StaticRoutes = new List<string>();
        var ospfNetworks = new List<string>();
        var ospfInterfaceAreas = new List<string>();
        var ospfNoPassive = new List<string>();
        var ospfIfSettings = new List<string>();
        var stpRootEntries = new List<string>();
        var stpVlanPriorities = new List<string>();
        var stpVlanTimers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var stpInterfaceSettings = new List<string>();
        var stpVlanInterfaceSettings = new List<string>();
        var stpMstInterfaceSettings = new List<string>();
        var stpResetEntries = new List<string>();
        var stpMstInstances = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var bgpNeighbors = new List<string>();
        var bgpNetworks = new List<string>();
        var bgpExtNeighbors = new List<string>();
        var vrfDefs = new List<string>();
        var vrfIfs = new List<string>();
        var aclLines = new List<string>();
        var stdAclLines = new List<string>();
        var extAclLines = new List<string>();
        var aclApplyLines = new List<string>();
        var ipv6AclLines = new List<string>();
        var natIfs = new List<string>();
        var natOverload = new List<string>();
        var natStatic = new List<string>();
        var ntp = new List<string>();
        var syslog = new List<string>();
        var users = new List<string>();
        var radius = new List<string>();
        var tacacs = new List<string>();
        var snmp = new List<string>();
        var subifs = new List<string>();
        var mplsIfs = new List<string>();
        var mplsPasswords = new List<string>();
        var mplsVrfList = new List<string>();
        var qinqTunnels = new List<string>();
        var qinqSubifs = new List<string>();
        var greList = new List<string>();
        var customKnown = new List<string>();

        string context = "global";
        string currentInterface = string.Empty;
        string currentVlan = string.Empty;
        string currentDhcp = string.Empty;
        string currentOspf = string.Empty;
        string currentBgp = string.Empty;
        string currentAcl = string.Empty;
        string currentIpv6Acl = string.Empty;
        string currentVrf = string.Empty;
        string currentRouteMap = string.Empty;
        string currentIpSla = string.Empty;

        var lines = NormalizeLines(config).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var raw = lines[index];
            var isSubCommand = HasLeadingWhitespace(raw);
            var line = CleanLine(raw);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("!", StringComparison.Ordinal))
            {
                ResetContext(ref context, ref currentInterface, ref currentVlan, ref currentDhcp, ref currentAcl,
                    ref currentIpv6Acl, ref currentVrf, ref currentRouteMap, ref currentIpSla);
                continue;
            }
            if (IsPromptOrOutput(line)) continue;

            result.TotalCommands++;
            var matched = false;

            void MarkKnown(string moduleName)
            {
                matched = true;
                result.KnownCommands++;
                if (!string.IsNullOrWhiteSpace(moduleName))
                    result.Modules[moduleName] = true;
            }

            if (IsExit(line))
            {
                ResetContext(ref context, ref currentInterface, ref currentVlan, ref currentDhcp, ref currentAcl,
                    ref currentIpv6Acl, ref currentVrf, ref currentRouteMap, ref currentIpSla);
                MarkKnown(string.Empty);
                continue;
            }

            if (line.Equals("enable", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("configure terminal", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("conf t", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("write memory", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("copy running-config startup-config", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("copy run start", StringComparison.OrdinalIgnoreCase))
            {
                MarkKnown(string.Empty);
                continue;
            }

            if (line.Equals("ip routing", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("no ip routing", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["ipRouting"] = line.StartsWith("no ", StringComparison.OrdinalIgnoreCase) ? "Nein" : "Ja";
                MarkKnown("routingBase");
                continue;
            }

            if (line.Equals("ip ssh version 2", StringComparison.OrdinalIgnoreCase))
            {
                MarkKnown("ssh");
                continue;
            }

            if (line.Equals("no ip http server", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("no ip http secure-server", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(line, @"^login\s+block-for\s+\d+\s+attempts\s+\d+\s+within\s+\d+$", RegexOptions.IgnoreCase))
            {
                MarkKnown("security");
                continue;
            }

            var stpModeMatch = Regex.Match(line, @"^spanning-tree\s+mode\s+(\S+)$", RegexOptions.IgnoreCase);
            if (stpModeMatch.Success)
            {
                result.Values["stpGlobalMode"] = stpModeMatch.Groups[1].Value;
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree extend system-id", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpExtendSystemId"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree portfast default", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("spanning-tree portfast edge default", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpPortfastDefault"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree portfast bpduguard default", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("spanning-tree portfast edge bpduguard default", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpBpduguardDefault"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree portfast bpdufilter default", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("spanning-tree portfast edge bpdufilter default", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpBpdufilterDefault"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree loopguard default", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpLoopguardDefault"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree etherchannel guard misconfig", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpEtherchannelGuard"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree uplinkfast", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpUplinkFast"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree backbonefast", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["stpBackboneFast"] = "Ja";
                MarkKnown("stpExtended");
                continue;
            }

            var stpMaxHopsMatch = Regex.Match(line, @"^spanning-tree\s+mst\s+max-hops\s+(\d+)$", RegexOptions.IgnoreCase);
            if (stpMaxHopsMatch.Success)
            {
                result.Values["stpGlobalMode"] = "mst";
                result.Values["stpMstMaxHops"] = stpMaxHopsMatch.Groups[1].Value;
                MarkKnown("stpExtended");
                continue;
            }

            var errdisableCauseMatch = Regex.Match(line, @"^errdisable\s+recovery\s+cause\s+(\S+)$", RegexOptions.IgnoreCase);
            if (errdisableCauseMatch.Success)
            {
                var cause = errdisableCauseMatch.Groups[1].Value;
                AppendValueLine(result, "errdisableCauseList", cause);
                result.Modules["errdisableRecovery"] = true;
                if (cause.Equals("bpduguard", StringComparison.OrdinalIgnoreCase))
                {
                    result.Values["stpErrdisableBpduguard"] = "Ja";
                    MarkKnown("stpExtended");
                }
                else
                {
                    result.Modules["errdisableRecovery"] = true;
                    result.KnownCommands++;
                }
                continue;
            }

            var stpErrdisableIntervalMatch = Regex.Match(line, @"^errdisable\s+recovery\s+interval\s+(\d+)$", RegexOptions.IgnoreCase);
            if (stpErrdisableIntervalMatch.Success)
            {
                result.Values["stpErrdisableInterval"] = stpErrdisableIntervalMatch.Groups[1].Value;
                result.Values["errdisableInterval"] = stpErrdisableIntervalMatch.Groups[1].Value;
                result.Modules["errdisableRecovery"] = true;
                MarkKnown("stpExtended");
                continue;
            }

            var stpNoGlobalMatch = Regex.Match(line, @"^no\s+spanning-tree\s+(.+)$", RegexOptions.IgnoreCase);
            if (stpNoGlobalMatch.Success)
            {
                var body = stpNoGlobalMatch.Groups[1].Value.Trim();
                var reset = ParseStpGlobalReset(body);
                if (!string.IsNullOrWhiteSpace(reset))
                {
                    stpResetEntries.Add(reset);
                    MarkKnown("stpExtended");
                    continue;
                }
            }

            var m = Regex.Match(line, @"^spanning-tree\s+pathcost\s+method\s+(short|long)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["stpPathcostMethod"] = m.Groups[1].Value.ToLowerInvariant();
                MarkKnown("stpExtended");
                continue;
            }

            m = Regex.Match(line, @"^spanning-tree\s+vlan\s+(\S+)\s+root\s+(primary|secondary)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                stpRootEntries.Add($"{m.Groups[1].Value}|{m.Groups[2].Value.ToLowerInvariant()}");
                MarkKnown("stpExtended");
                continue;
            }

            m = Regex.Match(line, @"^spanning-tree\s+vlan\s+(\S+)\s+priority\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                stpVlanPriorities.Add($"{m.Groups[1].Value}|{m.Groups[2].Value}");
                MarkKnown("stpExtended");
                continue;
            }

            m = Regex.Match(line, @"^spanning-tree\s+vlan\s+(\S+)\s+(hello-time|forward-time|max-age)\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var vlan = m.Groups[1].Value;
                if (!stpVlanTimers.TryGetValue(vlan, out var timers))
                {
                    timers = new[] { vlan, string.Empty, string.Empty, string.Empty };
                    stpVlanTimers[vlan] = timers;
                }

                switch (m.Groups[2].Value.ToLowerInvariant())
                {
                    case "hello-time": timers[1] = m.Groups[3].Value; break;
                    case "forward-time": timers[2] = m.Groups[3].Value; break;
                    case "max-age": timers[3] = m.Groups[3].Value; break;
                }

                MarkKnown("stpExtended");
                continue;
            }

            if (line.Equals("spanning-tree mst configuration", StringComparison.OrdinalIgnoreCase))
            {
                context = "spanning-tree mst configuration";
                result.Values["stpGlobalMode"] = "mst";
                MarkKnown("stpExtended");
                continue;
            }

            m = Regex.Match(line, @"^spanning-tree\s+mst\s+(\d+)\s+(priority\s+(\d+)|root\s+(primary|secondary))$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["stpGlobalMode"] = "mst";
                var instance = m.Groups[1].Value;
                if (!stpMstInstances.TryGetValue(instance, out var values))
                {
                    values = new[] { instance, string.Empty, string.Empty, string.Empty };
                    stpMstInstances[instance] = values;
                }

                if (m.Groups[3].Success) values[2] = m.Groups[3].Value;
                if (m.Groups[4].Success) values[3] = m.Groups[4].Value.ToLowerInvariant();
                MarkKnown("stpExtended");
                continue;
            }

            m = Regex.Match(line, @"^clock\s+timezone\s+(\S+)\s+(-?\d+)(?:\s+(\d+))?$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["clockTimezoneName"] = m.Groups[1].Value;
                result.Values["clockTimezoneHours"] = m.Groups[2].Value;
                result.Values["clockTimezoneMinutes"] = m.Groups[3].Success ? m.Groups[3].Value : "0";
                MarkKnown("basic");
                continue;
            }

            m = Regex.Match(line, @"^clock\s+summer-time\s+(\S+)\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["clockSummerName"] = m.Groups[1].Value;
                result.Values["clockSummerRule"] = m.Groups[2].Value.Trim();
                MarkKnown("basic");
                continue;
            }

            m = Regex.Match(line, @"^hostname\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["hostname"] = m.Groups[1].Value.Trim();
                MarkKnown("basic");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+domain-name\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["domain"] = m.Groups[1].Value.Trim();
                MarkKnown("basic");
                continue;
            }

            m = Regex.Match(line, @"^enable\s+secret\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["enableSecret"] = m.Groups[1].Value.Trim();
                MarkKnown("basic");
                continue;
            }

            m = Regex.Match(line, @"^security\s+passwords\s+min-length\s+(\S+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["minPass"] = m.Groups[1].Value.Trim();
                MarkKnown("basic");
                continue;
            }

            if (line.Equals("no ip domain-lookup", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["noDomainLookup"] = "Ja";
                MarkKnown("basic");
                continue;
            }

            if (line.Equals("service password-encryption", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["servicePass"] = "Ja";
                MarkKnown("basic");
                continue;
            }

            if (line.StartsWith("banner motd", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["motd"] = ExtractBanner(lines, ref index, line);
                MarkKnown("banner");
                continue;
            }

            if (line.StartsWith("banner login", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["loginBanner"] = ExtractBanner(lines, ref index, line);
                MarkKnown("banner");
                continue;
            }

            m = Regex.Match(line, @"^username\s+(\S+)(?:\s+privilege\s+(\d+))?.*?\s+secret\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                users.Add($"{m.Groups[1].Value}|{(m.Groups[2].Success ? m.Groups[2].Value : "15")}|{m.Groups[3].Value.Trim()}");
                MarkKnown("userRights");
                continue;
            }

            if (line.Equals("aaa new-model", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["aaaLocal"] = "Ja";
                MarkKnown("aaa");
                continue;
            }

            m = Regex.Match(line, @"^radius\s+server\s+(\S+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var name = m.Groups[1].Value;
                var serverLine = CollectBlock(lines, ref index, l => !IsTopLevelHeader(l));
                var address = ExtractFirst(serverLine, @"address\s+ipv4\s+(\S+)");
                var auth = ExtractFirst(serverLine, @"auth-port\s+(\d+)");
                var acct = ExtractFirst(serverLine, @"acct-port\s+(\d+)");
                var key = ExtractFirst(serverLine, @"key\s+(.+)");
                radius.Add($"{name}|{address}|{auth}|{acct}|{key}||");
                MarkKnown("radiusTacacs");
                continue;
            }

            m = Regex.Match(line, @"^tacacs\s+server\s+(\S+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var name = m.Groups[1].Value;
                var serverLine = CollectBlock(lines, ref index, l => !IsTopLevelHeader(l));
                var address = ExtractFirst(serverLine, @"address\s+ipv4\s+(\S+)");
                var key = ExtractFirst(serverLine, @"key\s+(.+)");
                tacacs.Add($"{name}|{address}|{key}");
                MarkKnown("radiusTacacs");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+ssh\s+time-out\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["sshTimeout"] = m.Groups[1].Value;
                MarkKnown("ssh");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+ssh\s+authentication-retries\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["sshRetries"] = m.Groups[1].Value;
                MarkKnown("ssh");
                continue;
            }

            m = Regex.Match(line, @"^crypto\s+key\s+generate\s+rsa.*modulus\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["rsaModulus"] = m.Groups[1].Value;
                MarkKnown("ssh");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+ssh\s+source-interface\s+(\S+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["sshSourceIf"] = m.Groups[1].Value;
                MarkKnown("ssh");
                continue;
            }

            m = Regex.Match(line, @"^line\s+vty\s+(\d+)\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                context = "line vty";
                result.Values["vtyStart"] = m.Groups[1].Value;
                result.Values["vtyEnd"] = m.Groups[2].Value;
                MarkKnown("ssh");
                continue;
            }

            if (context == "line vty" && isSubCommand)
            {
                var blockMatched = true;
                if (line.Equals("login local", StringComparison.OrdinalIgnoreCase))
                    result.Values["vtyLoginMode"] = "login local";
                else if (line.StartsWith("login authentication", StringComparison.OrdinalIgnoreCase))
                    result.Values["vtyLoginMode"] = "login authentication default";
                else if (line.Equals("login", StringComparison.OrdinalIgnoreCase))
                    result.Values["vtyLoginMode"] = "password login";
                else if (line.StartsWith("password ", StringComparison.OrdinalIgnoreCase))
                    result.Values["vtyPassword"] = line[9..].Trim();
                else if (line.StartsWith("access-class ", StringComparison.OrdinalIgnoreCase)) result.Values["vtyAclSsh"] = FirstTokenAfter(line, "access-class");
                else if (line.StartsWith("exec-timeout ", StringComparison.OrdinalIgnoreCase)) result.Values["vtyExecTimeout"] = line[13..].Trim();
                else if (line.Equals("logging synchronous", StringComparison.OrdinalIgnoreCase)) result.Values["vtyLoggingSync"] = "Ja";
                else if (line.StartsWith("privilege level ", StringComparison.OrdinalIgnoreCase)) result.Values["vtyPrivilege"] = line[16..].Trim();
                else if (line.Equals("transport input ssh", StringComparison.OrdinalIgnoreCase)) { }
                else if (IsCommonLineCommand(line)) { }
                else blockMatched = false;

                if (blockMatched) MarkKnown("ssh");
                else AddUnknown(result, lineNumber, context, line, "Line-VTY-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            if (line.StartsWith("line console", StringComparison.OrdinalIgnoreCase))
            {
                context = "line console";
                MarkKnown("aaa");
                continue;
            }

            if (context == "line console" && isSubCommand)
            {
                var blockMatched = true;
                if (line.StartsWith("exec-timeout ", StringComparison.OrdinalIgnoreCase)) result.Values["execTimeout"] = line[13..].Trim();
                else if (line.Equals("logging synchronous", StringComparison.OrdinalIgnoreCase)) result.Values["consoleLoggingSync"] = "Ja";
                else if (line.StartsWith("login", StringComparison.OrdinalIgnoreCase)) result.Values["consoleLogin"] = line;
                else if (IsCommonLineCommand(line)) { }
                else blockMatched = false;

                if (blockMatched) MarkKnown("aaa");
                else AddUnknown(result, lineNumber, context, line, "Console-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            m = Regex.Match(line, @"^ntp\s+server\s+(\S+)(.*)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                ntp.Add($"{m.Groups[1].Value}|{(m.Groups[2].Value.Contains("prefer", StringComparison.OrdinalIgnoreCase) ? "prefer" : string.Empty)}");
                MarkKnown("monitoringBase");
                continue;
            }

            m = Regex.Match(line, @"^logging\s+host\s+(\S+)(?:\s+(\S+))?$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                syslog.Add($"{m.Groups[1].Value}|{m.Groups[2].Value}");
                MarkKnown("monitoringBase");
                continue;
            }

            m = Regex.Match(line, @"^logging\s+buffered\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["logBuffered"] = m.Groups[1].Value;
                MarkKnown("monitoringBase");
                continue;
            }

            m = Regex.Match(line, @"^logging\s+trap\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["logTrap"] = m.Groups[1].Value;
                MarkKnown("monitoringBase");
                continue;
            }

            if (line.StartsWith("service timestamps", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["logTimestamps"] = "Ja";
                MarkKnown("monitoringBase");
                continue;
            }

            if (line.StartsWith("snmp-server group", StringComparison.OrdinalIgnoreCase) || line.StartsWith("snmp-server user", StringComparison.OrdinalIgnoreCase))
            {
                snmp.Add(line);
                MarkKnown("monitoringBase");
                continue;
            }

            if (context == "spanning-tree mst configuration" && isSubCommand)
            {
                result.Values["stpGlobalMode"] = "mst";
                var blockMatched = true;
                if (line.StartsWith("name ", StringComparison.OrdinalIgnoreCase))
                {
                    result.Values["stpMstName"] = line[5..].Trim();
                }
                else if (line.StartsWith("revision ", StringComparison.OrdinalIgnoreCase))
                {
                    result.Values["stpMstRevision"] = line[9..].Trim();
                }
                else
                {
                    m = Regex.Match(line, @"^instance\s+(\d+)\s+vlan\s+(.+)$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var instance = m.Groups[1].Value;
                        if (!stpMstInstances.TryGetValue(instance, out var values))
                        {
                            values = new[] { instance, string.Empty, string.Empty, string.Empty };
                            stpMstInstances[instance] = values;
                        }
                        values[1] = m.Groups[2].Value.Trim();
                    }
                    else blockMatched = false;
                }

                if (blockMatched) MarkKnown("stpExtended");
                else AddUnknown(result, lineNumber, context, line, "MST-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            m = Regex.Match(line, @"^vlan\s+(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentVlan = m.Groups[1].Value;
                context = "vlan";
                EnsureVlan(vlans, currentVlan);
                MarkKnown("vlans");
                continue;
            }

            if (context == "vlan" && isSubCommand)
            {
                if (line.StartsWith("name ", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureVlan(vlans, currentVlan).Name = line[5..].Trim();
                    MarkKnown("vlans");
                }
                else
                {
                    AddUnknown(result, lineNumber, context, line, "VLAN-Unterbefehl wurde nicht übernommen.");
                }
                continue;
            }

            m = Regex.Match(line, @"^interface\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentInterface = m.Groups[1].Value.Trim();
                context = "interface";
                GetInterface(interfaces, currentInterface);
                MarkKnown("interfaces");
                continue;
            }

            if (context == "interface" && isSubCommand && !string.IsNullOrWhiteSpace(currentInterface))
            {
                var inf = GetInterface(interfaces, currentInterface);
                var blockMatched = true;

                if (line.StartsWith("description ", StringComparison.OrdinalIgnoreCase))
                    inf.Description = line[12..].Trim();
                else if (line.StartsWith("ip address ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = SplitWords(line);
                    if (parts.Length >= 4)
                    {
                        inf.Ip = parts[2];
                        inf.Mask = parts[3];
                        inf.NoIpAddress = false;
                    }
                    else blockMatched = false;
                }
                else if (line.Equals("no ip address", StringComparison.OrdinalIgnoreCase))
                    inf.NoIpAddress = true;
                else if (line.StartsWith("ipv6 address ", StringComparison.OrdinalIgnoreCase))
                    inf.Ipv6 = line[13..].Trim();
                else if (line.StartsWith("ip vrf forwarding ", StringComparison.OrdinalIgnoreCase))
                    inf.Vrf = line[18..].Trim();
                else if (line.StartsWith("vrf forwarding ", StringComparison.OrdinalIgnoreCase))
                    inf.Vrf = line[15..].Trim();
                else if (line.Equals("switchport", StringComparison.OrdinalIgnoreCase)) { }
                else if (line.Equals("no switchport", StringComparison.OrdinalIgnoreCase))
                    inf.Mode = "routed";
                else if (line.StartsWith("switchport mode access", StringComparison.OrdinalIgnoreCase))
                    inf.Mode = "access";
                else if (line.StartsWith("switchport mode trunk", StringComparison.OrdinalIgnoreCase))
                    inf.Mode = "trunk";
                else if (line.StartsWith("switchport mode dot1q-tunnel", StringComparison.OrdinalIgnoreCase))
                {
                    inf.Mode = "access";
                    inf.Qinq = true;
                }
                else if (line.StartsWith("switchport access vlan ", StringComparison.OrdinalIgnoreCase))
                    inf.AccessVlan = line[24..].Trim();
                else if (line.StartsWith("switchport voice vlan ", StringComparison.OrdinalIgnoreCase))
                    inf.VoiceVlan = line[23..].Trim();
                else if (line.StartsWith("switchport trunk allowed vlan ", StringComparison.OrdinalIgnoreCase))
                    inf.AllowedVlans = line[30..].Trim();
                else if (line.StartsWith("switchport trunk native vlan ", StringComparison.OrdinalIgnoreCase))
                    inf.NativeVlan = line[29..].Trim();
                else if (line.Equals("switchport nonegotiate", StringComparison.OrdinalIgnoreCase))
                    inf.Nonegotiate = true;
                else if (line.Equals("spanning-tree portfast", StringComparison.OrdinalIgnoreCase) ||
                         line.Equals("spanning-tree portfast edge", StringComparison.OrdinalIgnoreCase))
                    inf.PortFast = true;
                else if (line.Equals("spanning-tree portfast trunk", StringComparison.OrdinalIgnoreCase) ||
                         line.Equals("spanning-tree portfast edge trunk", StringComparison.OrdinalIgnoreCase))
                {
                    inf.PortFast = true;
                    inf.PortFastTrunk = true;
                }
                else if (line.Equals("spanning-tree bpduguard enable", StringComparison.OrdinalIgnoreCase))
                    inf.BpduGuard = true;
                else if (line.Equals("spanning-tree bpdufilter enable", StringComparison.OrdinalIgnoreCase))
                    inf.BpduFilter = true;
                else if (line.Equals("spanning-tree guard root", StringComparison.OrdinalIgnoreCase))
                    inf.StpGuard = "root";
                else if (line.Equals("spanning-tree guard loop", StringComparison.OrdinalIgnoreCase))
                    inf.StpGuard = "loop";
                else if (line.StartsWith("spanning-tree link-type ", StringComparison.OrdinalIgnoreCase))
                    inf.StpLinkType = line[24..].Trim();
                else if (line.StartsWith("spanning-tree port-priority ", StringComparison.OrdinalIgnoreCase))
                    inf.StpPortPriority = line[28..].Trim();
                else if (line.StartsWith("spanning-tree cost ", StringComparison.OrdinalIgnoreCase))
                    inf.StpCost = line[19..].Trim();
                else if (TryParseStpVlanInterfaceParameter(line, inf)) { }
                else if (TryParseStpMstInterfaceParameter(line, inf)) { }
                else if (TryParseStpInterfaceReset(line, inf)) { }
                else if (line.StartsWith("switchport port-security", StringComparison.OrdinalIgnoreCase))
                    inf.PortSecurity = true;
                else if (line.Equals("ip nat inside", StringComparison.OrdinalIgnoreCase))
                    inf.NatInside = true;
                else if (line.Equals("ip nat outside", StringComparison.OrdinalIgnoreCase))
                    inf.NatOutside = true;
                else if (line.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
                    inf.Shutdown = true;
                else if (line.Equals("mpls ip", StringComparison.OrdinalIgnoreCase))
                    inf.Mpls = true;
                else if (line.StartsWith("mpls mtu ", StringComparison.OrdinalIgnoreCase))
                    inf.MplsMtu = line[9..].Trim();
                else if (line.Equals("mpls ldp igp sync", StringComparison.OrdinalIgnoreCase))
                    inf.LdpIgpSync = true;
                else if (line.StartsWith("l2protocol-tunnel", StringComparison.OrdinalIgnoreCase))
                    inf.L2ProtocolTunnel = AppendCsv(inf.L2ProtocolTunnel, line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty);
                else if (line.StartsWith("encapsulation dot1Q ", StringComparison.OrdinalIgnoreCase))
                    ParseSubinterface(inf, qinqSubifs, subifs, line);
                else
                {
                    m = Regex.Match(line, @"^ip\s+access-group\s+(\S+)\s+(in|out)$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        aclApplyLines.Add($"{currentInterface}|{m.Groups[1].Value}|{m.Groups[2].Value.ToLowerInvariant()}");
                    }
                    else
                    {
                        m = Regex.Match(line, @"^ip\s+ospf\s+(\S+)\s+area\s+(\S+)$", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            inf.OspfProcess = m.Groups[1].Value;
                            inf.OspfArea = m.Groups[2].Value;
                        }
                        else
                        {
                            m = Regex.Match(line, @"^ip\s+ospf\s+network\s+(\S+)$", RegexOptions.IgnoreCase);
                            if (m.Success)
                                inf.OspfNetworkType = m.Groups[1].Value;
                            else
                            {
                                m = Regex.Match(line, @"^ip\s+ospf\s+cost\s+(\d+)$", RegexOptions.IgnoreCase);
                                if (m.Success)
                                    inf.OspfCost = m.Groups[1].Value;
                                else
                                {
                                    m = Regex.Match(line, @"^ip\s+ospf\s+priority\s+(\d+)$", RegexOptions.IgnoreCase);
                                    if (m.Success)
                                        inf.OspfPriority = m.Groups[1].Value;
                                    else if (line.StartsWith("tunnel source ", StringComparison.OrdinalIgnoreCase) ||
                                             line.StartsWith("tunnel destination ", StringComparison.OrdinalIgnoreCase) ||
                                             line.StartsWith("tunnel mode ", StringComparison.OrdinalIgnoreCase))
                                        AddContextCommand(customKnown, $"interface {currentInterface}", line);
                                    else if (IsCommonLineCommand(line)) { }
                                    else blockMatched = false;
                                }
                            }
                        }
                    }
                }

                if (blockMatched)
                    MarkKnown(GuessInterfaceModule(inf));
                else
                    AddUnknown(result, lineNumber, $"interface {currentInterface}", line, "Interface-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+route\s+(\S+)\s+(\S+)\s+(\S+)(?:\s+(\d+))?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                staticRoutes.Add($"{m.Groups[1].Value}|{m.Groups[2].Value}|{m.Groups[3].Value}|{m.Groups[4].Value}");
                MarkKnown("staticRoutes");
                continue;
            }

            m = Regex.Match(line, @"^ipv6\s+route\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                ipv6StaticRoutes.Add($"{m.Groups[1].Value}|{m.Groups[2].Value}");
                MarkKnown("ipv6Extended");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+dhcp\s+pool\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentDhcp = m.Groups[1].Value.Trim();
                context = "dhcp";
                EnsureDhcp(dhcpPools, currentDhcp);
                MarkKnown("dhcp");
                continue;
            }

            if (context == "dhcp" && isSubCommand && !string.IsNullOrWhiteSpace(currentDhcp))
            {
                var pool = EnsureDhcp(dhcpPools, currentDhcp);
                var blockMatched = true;
                m = Regex.Match(line, @"^network\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success) { pool.Network = m.Groups[1].Value; pool.Mask = m.Groups[2].Value; }
                else if (line.StartsWith("default-router ", StringComparison.OrdinalIgnoreCase)) pool.Gateway = line[15..].Trim();
                else if (line.StartsWith("dns-server ", StringComparison.OrdinalIgnoreCase)) pool.Dns = line[11..].Trim();
                else if (line.StartsWith("domain-name ", StringComparison.OrdinalIgnoreCase)) pool.Domain = line[12..].Trim();
                else if (line.StartsWith("lease ", StringComparison.OrdinalIgnoreCase)) pool.Lease = line[6..].Trim();
                else if (line.StartsWith("option 150 ip ", StringComparison.OrdinalIgnoreCase)) pool.Option150 = line[14..].Trim();
                else blockMatched = false;

                if (blockMatched) MarkKnown("dhcp");
                else AddUnknown(result, lineNumber, $"ip dhcp pool {currentDhcp}", line, "DHCP-Pool-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+dhcp\s+excluded-address\s+(\S+)(?:\s+(\S+))?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                AppendValueLine(result, "dhcpExcludedList", $"{m.Groups[1].Value}|{m.Groups[2].Value}");
                MarkKnown("dhcp");
                continue;
            }

            m = Regex.Match(line, @"^router\s+ospf\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentOspf = m.Groups[1].Value;
                context = "router ospf";
                result.Values["ospfPid"] = currentOspf;
                MarkKnown("ospf");
                continue;
            }

            if (context == "router ospf" && isSubCommand)
            {
                var blockMatched = true;
                m = Regex.Match(line, @"^router-id\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success) result.Values["ospfRid"] = m.Groups[1].Value;
                else if (line.StartsWith("auto-cost reference-bandwidth ", StringComparison.OrdinalIgnoreCase)) result.Values["ospfRef"] = line[30..].Trim();
                else if (line.Equals("passive-interface default", StringComparison.OrdinalIgnoreCase)) result.Values["ospfPassiveDefault"] = "Ja";
                else if (line.StartsWith("no passive-interface ", StringComparison.OrdinalIgnoreCase)) ospfNoPassive.Add(line[21..].Trim());
                else if (line.Equals("default-information originate always", StringComparison.OrdinalIgnoreCase)) result.Values["ospfDefaultOriginate"] = "Immer";
                else if (line.Equals("default-information originate", StringComparison.OrdinalIgnoreCase)) result.Values["ospfDefaultOriginate"] = "Ja";
                else if (line.Equals("log-adjacency-changes detail", StringComparison.OrdinalIgnoreCase)) result.Values["ospfLogAdjacency"] = "Detail";
                else if (line.Equals("log-adjacency-changes", StringComparison.OrdinalIgnoreCase)) result.Values["ospfLogAdjacency"] = "Ja";
                else
                {
                    m = Regex.Match(line, @"^network\s+(\S+)\s+(\S+)\s+area\s+(\S+)", RegexOptions.IgnoreCase);
                    if (m.Success) ospfNetworks.Add($"{m.Groups[1].Value}|{m.Groups[2].Value}|{m.Groups[3].Value}");
                    else blockMatched = false;
                }

                if (blockMatched) MarkKnown("ospf");
                else AddUnknown(result, lineNumber, $"router ospf {currentOspf}", line, "OSPF-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            m = Regex.Match(line, @"^router\s+bgp\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentBgp = m.Groups[1].Value;
                context = "router bgp";
                result.Values["bgpAs"] = currentBgp;
                MarkKnown("bgp");
                continue;
            }

            if (context == "router bgp" && isSubCommand)
            {
                var blockMatched = true;
                m = Regex.Match(line, @"^bgp\s+router-id\s+(\S+)", RegexOptions.IgnoreCase);
                if (m.Success) result.Values["bgpRid"] = m.Groups[1].Value;
                else
                {
                    m = Regex.Match(line, @"^neighbor\s+(\S+)\s+remote-as\s+(\S+)", RegexOptions.IgnoreCase);
                    if (m.Success) bgpNeighbors.Add($"{m.Groups[1].Value}|{m.Groups[2].Value}|");
                    else
                    {
                        m = Regex.Match(line, @"^neighbor\s+(\S+)\s+(update-source|ebgp-multihop|next-hop-self|password|maximum-prefix|soft-reconfiguration inbound|default-originate)\s*(.*)$", RegexOptions.IgnoreCase);
                        if (m.Success) bgpExtNeighbors.Add($"{m.Groups[1].Value}|{(m.Groups[2].Value.Equals("update-source", StringComparison.OrdinalIgnoreCase) ? m.Groups[3].Value : string.Empty)}||||||");
                        else
                        {
                            m = Regex.Match(line, @"^network\s+(\S+)(?:\s+mask\s+(\S+))?", RegexOptions.IgnoreCase);
                            if (m.Success) bgpNetworks.Add($"{m.Groups[1].Value}|{m.Groups[2].Value}");
                            else if (line.StartsWith("address-family ", StringComparison.OrdinalIgnoreCase) || line.Equals("exit-address-family", StringComparison.OrdinalIgnoreCase)) customKnown.Add(line);
                            else blockMatched = false;
                        }
                    }
                }

                if (blockMatched) MarkKnown("bgp");
                else AddUnknown(result, lineNumber, $"router bgp {currentBgp}", line, "BGP-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            m = Regex.Match(line, @"^vrf\s+definition\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentVrf = m.Groups[1].Value;
                context = "vrf";
                if (!vrfDefs.Any(x => x.StartsWith(currentVrf + "|", StringComparison.OrdinalIgnoreCase)))
                    vrfDefs.Add($"{currentVrf}||");
                MarkKnown("vrfDefs");
                continue;
            }

            if (context == "vrf" && isSubCommand && !string.IsNullOrWhiteSpace(currentVrf))
            {
                var blockMatched = true;
                if (line.StartsWith("rd ", StringComparison.OrdinalIgnoreCase)) UpdatePipeValue(vrfDefs, currentVrf, 1, line[3..].Trim());
                else if (line.StartsWith("description ", StringComparison.OrdinalIgnoreCase)) UpdatePipeValue(vrfDefs, currentVrf, 2, line[12..].Trim());
                else if (line.StartsWith("route-target import ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("route-target export ", StringComparison.OrdinalIgnoreCase))
                    mplsVrfList.Add($"{currentVrf}||{(line.Contains("import", StringComparison.OrdinalIgnoreCase) ? line.Split(' ').Last() : string.Empty)}|{(line.Contains("export", StringComparison.OrdinalIgnoreCase) ? line.Split(' ').Last() : string.Empty)}|");
                else if (line.StartsWith("address-family ", StringComparison.OrdinalIgnoreCase) || line.Equals("exit-address-family", StringComparison.OrdinalIgnoreCase)) customKnown.Add(line);
                else blockMatched = false;

                if (blockMatched) MarkKnown("vrfDefs");
                else AddUnknown(result, lineNumber, $"vrf definition {currentVrf}", line, "VRF-Unterbefehl wurde nicht übernommen.");
                continue;
            }

            if (line.Equals("ip cef", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["mplsCef"] = "Ja";
                MarkKnown("mpls");
                continue;
            }

            if (line.Equals("mpls ip", StringComparison.OrdinalIgnoreCase))
            {
                result.Values["mplsGlobal"] = "Ja";
                MarkKnown("mpls");
                continue;
            }

            m = Regex.Match(line, @"^mpls\s+label\s+protocol\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["mplsLabelProtocol"] = m.Groups[1].Value.ToUpperInvariant();
                MarkKnown("mpls");
                continue;
            }

            m = Regex.Match(line, @"^mpls\s+ldp\s+router-id\s+(\S+)(?:\s+(force))?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                result.Values["mplsLdpRouterId"] = m.Groups[1].Value;
                result.Values["mplsLdpRouterIdForce"] = m.Groups[2].Success ? "Ja" : "Nein";
                MarkKnown("mpls");
                continue;
            }

            m = Regex.Match(line, @"^mpls\s+ldp\s+neighbor\s+(\S+)\s+password\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                mplsPasswords.Add($"{m.Groups[1].Value}|{m.Groups[2].Value.Trim()}");
                MarkKnown("mpls");
                continue;
            }

            if (line.StartsWith("ip nat ", StringComparison.OrdinalIgnoreCase))
            {
                if (line.Contains(" overload", StringComparison.OrdinalIgnoreCase))
                {
                    var acl = ExtractFirst(line, @"list\s+(\S+)");
                    var outsideIf = ExtractFirst(line, @"interface\s+(\S+)");
                    natOverload.Add($"{acl}|{outsideIf}");
                }
                else if (line.StartsWith("ip nat inside source static", StringComparison.OrdinalIgnoreCase))
                {
                    var p = SplitWords(line);
                    if (p.Length >= 7) natStatic.Add($"{p[5]}|{p[6]}");
                }
                MarkKnown("nat");
                continue;
            }

            m = Regex.Match(line, @"^ip\s+access-list\s+(standard|extended)\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentAcl = m.Groups[2].Value.Trim();
                context = m.Groups[1].Value.Equals("standard", StringComparison.OrdinalIgnoreCase) ? "std acl" : "ext acl";
                MarkKnown("acl");
                continue;
            }

            if (context == "std acl" && isSubCommand)
            {
                m = Regex.Match(line, @"^(permit|deny|remark)\s+(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    if (!m.Groups[1].Value.Equals("remark", StringComparison.OrdinalIgnoreCase))
                        stdAclLines.Add($"{currentAcl}|{m.Groups[1].Value}|{m.Groups[2].Value}");
                    MarkKnown("acl");
                }
                else AddUnknown(result, lineNumber, $"ip access-list standard {currentAcl}", line, "Standard-ACL-Zeile wurde nicht übernommen.");
                continue;
            }

            if (context == "ext acl" && isSubCommand)
            {
                m = Regex.Match(line, @"^(permit|deny)\s+(\S+)\s+(.+?)\s+(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    extAclLines.Add($"{currentAcl}|{m.Groups[1].Value}|{m.Groups[2].Value}|{m.Groups[3].Value}|{m.Groups[4].Value}");
                    MarkKnown("acl");
                }
                else if (line.StartsWith("remark ", StringComparison.OrdinalIgnoreCase)) MarkKnown("acl");
                else AddUnknown(result, lineNumber, $"ip access-list extended {currentAcl}", line, "Extended-ACL-Zeile wurde nicht übernommen.");
                continue;
            }

            m = Regex.Match(line, @"^access-list\s+(\S+)\s+(permit|deny)\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var acl = m.Groups[1].Value;
                if (int.TryParse(acl, out var aclNum) && aclNum < 100)
                    stdAclLines.Add($"{acl}|{m.Groups[2].Value}|{m.Groups[3].Value}");
                else
                    aclLines.Add($"{acl}|{m.Groups[2].Value}|||{m.Groups[3].Value}");
                MarkKnown("acl");
                continue;
            }

            m = Regex.Match(line, @"^ipv6\s+access-list\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                currentIpv6Acl = m.Groups[1].Value.Trim();
                context = "ipv6 acl";
                MarkKnown("ipv6Extended");
                continue;
            }

            if (context == "ipv6 acl" && isSubCommand)
            {
                m = Regex.Match(line, @"^(permit|deny)\s+(\S+)\s+(.+?)\s+(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    ipv6AclLines.Add($"{currentIpv6Acl}|{m.Groups[1].Value}|{m.Groups[2].Value}|{m.Groups[3].Value}|{m.Groups[4].Value}");
                    MarkKnown("ipv6Extended");
                }
                else if (line.StartsWith("remark ", StringComparison.OrdinalIgnoreCase)) MarkKnown("ipv6Extended");
                else AddUnknown(result, lineNumber, $"ipv6 access-list {currentIpv6Acl}", line, "IPv6-ACL-Zeile wurde nicht übernommen.");
                continue;
            }

            if (line.StartsWith("router ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("address-family ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("class-map", StringComparison.OrdinalIgnoreCase) || line.StartsWith("policy-map", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("crypto ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("zone", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("route-map", StringComparison.OrdinalIgnoreCase) || line.StartsWith("ip prefix-list", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ip sla", StringComparison.OrdinalIgnoreCase) || line.StartsWith("track ", StringComparison.OrdinalIgnoreCase))
            {
                customKnown.Add(line);
                MarkKnown("customCommands");
                continue;
            }

            if (!matched)
            {
                AddUnknown(result, lineNumber, context, line, "Befehl ist aktuell keinem Import-Mapping zugeordnet.");
            }
        }

        FinalizeInterfaces(result, interfaces, vlans, subifs, natIfs, aclApplyLines, ipv6AclLines, qinqTunnels, qinqSubifs, mplsIfs, vrfIfs,
            ospfIfSettings, ospfInterfaceAreas, stpInterfaceSettings, stpVlanInterfaceSettings, stpMstInterfaceSettings, stpResetEntries, customKnown);
        SetJoined(result, "vlanList", vlans.Values.Where(v => !string.IsNullOrWhiteSpace(v.Id)).Select(v => $"{v.Id}|{v.Name}"), "vlans");
        SetJoined(result, "staticRouteList", staticRoutes, "staticRoutes");
        SetJoined(result, "ipv6StaticRoutes", ipv6StaticRoutes, "ipv6Extended");
        SetJoined(result, "ospfNetList", ospfNetworks, "ospf");
        SetJoined(result, "ospfInterfaceAreaList", ospfInterfaceAreas, "ospf");
        SetJoined(result, "ospfNoPassiveList", ospfNoPassive, "ospf");
        SetJoined(result, "ospfIfSettings", ospfIfSettings, "ospf");
        if (ospfNetworks.Count > 0 && ospfInterfaceAreas.Count > 0)
            result.Values["ospfConfigMode"] = "Gemischt";
        else if (ospfInterfaceAreas.Count > 0)
            result.Values["ospfConfigMode"] = "Interfacebasiert";
        else if (ospfNetworks.Count > 0)
            result.Values["ospfConfigMode"] = "Prozessbasiert";

        if (string.IsNullOrWhiteSpace(result.Values.GetValueOrDefault("stpGlobalMode")))
        {
            var hasMstConfiguration = stpMstInstances.Count > 0 || stpMstInterfaceSettings.Count > 0 ||
                                      !string.IsNullOrWhiteSpace(result.Values.GetValueOrDefault("stpMstName")) ||
                                      !string.IsNullOrWhiteSpace(result.Values.GetValueOrDefault("stpMstRevision"));
            var hasVlanConfiguration = stpRootEntries.Count > 0 || stpVlanPriorities.Count > 0 ||
                                       stpVlanTimers.Count > 0 || stpVlanInterfaceSettings.Count > 0;
            if (hasMstConfiguration) result.Values["stpGlobalMode"] = "mst";
            else if (hasVlanConfiguration) result.Values["stpGlobalMode"] = "pvst";
        }

        SetJoined(result, "stpRootList", stpRootEntries, "stpExtended");
        SetJoined(result, "stpVlanPriorityList", stpVlanPriorities, "stpExtended");
        SetJoined(result, "stpVlanTimersList", stpVlanTimers.Values.Select(x => string.Join("|", x)), "stpExtended");
        SetJoined(result, "stpInterfaceList", stpInterfaceSettings, "stpExtended");
        SetJoined(result, "stpVlanInterfaceList", stpVlanInterfaceSettings, "stpExtended");
        SetJoined(result, "stpMstInterfaceList", stpMstInterfaceSettings, "stpExtended");
        SetJoined(result, "stpResetList", stpResetEntries, "stpExtended");
        SetJoined(result, "stpMstInstanceList", stpMstInstances.Values.Select(x => string.Join("|", x)), "stpExtended");
        SetJoined(result, "bgpNeighborList", bgpNeighbors.Distinct(StringComparer.OrdinalIgnoreCase), "bgp");
        SetJoined(result, "bgpNetworkList", bgpNetworks, "bgp");
        SetJoined(result, "bgpExtNeighborList", bgpExtNeighbors, "bgp");
        SetJoined(result, "vrfDefList", vrfDefs, "vrfDefs");
        SetJoined(result, "vrfList", vrfDefs, "vrfLite");
        SetJoined(result, "vrfInterfaceList", vrfIfs, "vrfLite");
        SetJoined(result, "dhcpPoolList", dhcpPools.Values.Where(p => !string.IsNullOrWhiteSpace(p.Network)).Select(p => $"{p.Name}|{p.Network}|{p.Mask}|{p.Gateway}|{p.Dns}"), "dhcp");
        SetJoined(result, "dhcpExtPoolList", dhcpPools.Values.Where(p => !string.IsNullOrWhiteSpace(p.Domain) || !string.IsNullOrWhiteSpace(p.Lease) || !string.IsNullOrWhiteSpace(p.Option150)).Select(p => $"{p.Name}|{p.Network}|{p.Mask}|{p.Gateway}|{p.Dns}|{p.Domain}|{p.Lease}|{p.Option150}"), "dhcp");
        SetJoined(result, "stdAclList", stdAclLines, "acl");
        SetJoined(result, "extAclList", extAclLines, "acl");
        SetJoined(result, "aclList", aclLines, "acl");
        SetJoined(result, "aclApplyList", aclApplyLines, "acl");
        SetJoined(result, "ipv6AclList", ipv6AclLines, "ipv6Extended");
        SetJoined(result, "ntpList", ntp, "monitoringBase");
        SetJoined(result, "syslogList", syslog, "monitoringBase");
        SetJoined(result, "snmpList", snmp, "monitoringBase");
        SetJoined(result, "privUserList", users, "userRights");
        SetJoined(result, "radiusList", radius, "radiusTacacs");
        SetJoined(result, "tacacsList", tacacs, "radiusTacacs");
        SetJoined(result, "natIfList", natIfs, "nat");
        SetJoined(result, "natOverloadList", natOverload, "nat");
        SetJoined(result, "natStaticList", natStatic, "nat");
        SetJoined(result, "qinqTunnelList", qinqTunnels, "qinq");
        SetJoined(result, "qinqSubifList", qinqSubifs, "qinq");
        SetJoined(result, "mplsInterfaceList", mplsIfs, "mpls");
        SetJoined(result, "mplsLdpPasswordList", mplsPasswords, "mpls");
        SetJoined(result, "mplsVpnVrfList", mplsVrfList, "mpls");
        if (customKnown.Count > 0)
        {
            result.Values["customConfigList"] = string.Join(Environment.NewLine, customKnown);
            result.Modules["customCommands"] = true;
        }

        if (result.UnknownCommands.Count > 0)
            result.Notes.Add($"{result.UnknownCommands.Count} Befehl(e) wurden nicht automatisch übernommen. Diese stehen rechts unter 'Unbekannte Befehle'.");

        if (result.Values.Count == 0 && result.Modules.Count == 0)
            result.Notes.Add("Die eingefügte Konfiguration wurde gelesen, aber es wurden keine direkt übernehmbaren Felder erkannt.");

        return result;
    }

    private static void AddContextCommand(List<string> customKnown, string contextHeader, string command)
    {
        if (string.IsNullOrWhiteSpace(contextHeader) || string.IsNullOrWhiteSpace(command)) return;

        customKnown.Add(contextHeader.Trim());
        customKnown.Add(" " + command.Trim());
        customKnown.Add("exit");
    }

    private static void AddUnknown(ImportedConfigAnalysis result, int lineNumber, string context, string line, string note) =>
        result.UnknownCommands.Add(new ImportedUnknownCommand(lineNumber, context, line, note));

    private static void ResetContext(ref string context, ref string currentInterface, ref string currentVlan, ref string currentDhcp,
        ref string currentAcl, ref string currentIpv6Acl, ref string currentVrf, ref string currentRouteMap, ref string currentIpSla)
    {
        context = "global";
        currentInterface = currentVlan = currentDhcp = currentAcl = currentIpv6Acl = currentVrf = currentRouteMap = currentIpSla = string.Empty;
    }

    private static bool HasLeadingWhitespace(string? line) =>
        !string.IsNullOrEmpty(line) && char.IsWhiteSpace(line[0]);

    private static IEnumerable<string> NormalizeLines(string config) =>
        config.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string CleanLine(string line)
    {
        var clean = (line ?? string.Empty).Trim();
        clean = Regex.Replace(clean, @"^[A-Za-z0-9_.-]+(?:\(config[^)]*\))?[#>]\s*", string.Empty);
        return clean.Trim();
    }

    private static string[] SplitWords(string line) => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsExit(string line) => line.Equals("exit", StringComparison.OrdinalIgnoreCase) || line.Equals("end", StringComparison.OrdinalIgnoreCase) || line.Equals("exit-address-family", StringComparison.OrdinalIgnoreCase);

    private static bool IsPromptOrOutput(string line) =>
        line.StartsWith("Building configuration", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Current configuration", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("version ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Last configuration", StringComparison.OrdinalIgnoreCase);

    private static bool IsTopLevelHeader(string line)
    {
        var clean = CleanLine(line);
        return clean.StartsWith("interface ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("router ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("line ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("vlan ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("ip dhcp pool ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("ip access-list ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("ipv6 access-list ", StringComparison.OrdinalIgnoreCase) ||
               clean.StartsWith("vrf definition ", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("!", StringComparison.OrdinalIgnoreCase) ||
               clean.Equals("exit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCommonLineCommand(string line) =>
        line.Equals("no shutdown", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("shutdown", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("duplex auto", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("speed auto", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("negotiation auto", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("no ip redirects", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("no ip proxy-arp", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("no ip unreachables", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("load-interval ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("logging event", StringComparison.OrdinalIgnoreCase);

    private static InterfaceInfo GetInterface(Dictionary<string, InterfaceInfo> interfaces, string name)
    {
        if (!interfaces.TryGetValue(name, out var inf))
        {
            inf = new InterfaceInfo { Name = name };
            interfaces[name] = inf;
        }
        return inf;
    }

    private static VlanInfo EnsureVlan(Dictionary<string, VlanInfo> vlans, string id)
    {
        if (!vlans.TryGetValue(id, out var vlan))
        {
            vlan = new VlanInfo { Id = id };
            vlans[id] = vlan;
        }
        return vlan;
    }

    private static DhcpPoolInfo EnsureDhcp(Dictionary<string, DhcpPoolInfo> pools, string name)
    {
        if (!pools.TryGetValue(name, out var pool))
        {
            pool = new DhcpPoolInfo { Name = name };
            pools[name] = pool;
        }
        return pool;
    }

    private static void FinalizeInterfaces(ImportedConfigAnalysis result, Dictionary<string, InterfaceInfo> interfaces, Dictionary<string, VlanInfo> vlans,
        List<string> subifs, List<string> natIfs, List<string> aclApplyLines, List<string> ipv6AclLines, List<string> qinqTunnels, List<string> qinqSubifs, List<string> mplsIfs, List<string> vrfIfs,
        List<string> ospfIfSettings, List<string> ospfInterfaceAreas, List<string> stpInterfaceSettings,
        List<string> stpVlanInterfaceSettings, List<string> stpMstInterfaceSettings, List<string> stpResetEntries, List<string> customKnown)
    {
        var interfaceLines = new List<string>();
        var sviLines = new List<string>();
        var ipv6Lines = new List<string>();
        var trunkLines = new List<string>();
        var voipLines = new List<string>();

        foreach (var inf in interfaces.Values)
        {
            var isSvi = Regex.IsMatch(inf.Name, @"^vlan\d+$", RegexOptions.IgnoreCase);
            var vlanId = isSvi ? Regex.Match(inf.Name, @"\d+").Value : string.Empty;

            if (isSvi && !string.IsNullOrWhiteSpace(inf.Ip))
                sviLines.Add($"{vlanId}|{inf.Ip}|{inf.Mask}");

            if (!string.IsNullOrWhiteSpace(inf.Ipv6))
                ipv6Lines.Add($"{inf.Name}|{inf.Ipv6}|");

            if (!string.IsNullOrWhiteSpace(inf.Vrf) && !string.IsNullOrWhiteSpace(inf.Ip))
                vrfIfs.Add($"{inf.Name}|{inf.Vrf}|{inf.Ip}|{inf.Mask}");

            if (inf.NatInside) natIfs.Add($"{inf.Name}|inside");
            if (inf.NatOutside) natIfs.Add($"{inf.Name}|outside");

            if (inf.Qinq)
                qinqTunnels.Add($"{inf.Name}|{inf.AccessVlan}|{inf.Description}|{inf.L2ProtocolTunnel}");

            if (inf.Mpls)
                mplsIfs.Add($"{inf.Name}|{inf.Description}|{inf.MplsMtu}|{(inf.LdpIgpSync ? "Ja" : "Nein")}");

            if (!string.IsNullOrWhiteSpace(inf.OspfCost) ||
                !string.IsNullOrWhiteSpace(inf.OspfPriority) ||
                !string.IsNullOrWhiteSpace(inf.OspfNetworkType))
                ospfIfSettings.Add($"{inf.Name}|{inf.OspfCost}|{inf.OspfPriority}|{inf.OspfNetworkType}");

            if (!string.IsNullOrWhiteSpace(inf.OspfProcess) && !string.IsNullOrWhiteSpace(inf.OspfArea))
            {
                ospfInterfaceAreas.Add($"{inf.Name}|{inf.OspfArea}");
                if (string.IsNullOrWhiteSpace(result.Values.GetValueOrDefault("ospfPid")))
                    result.Values["ospfPid"] = inf.OspfProcess;
            }

            if (inf.PortFast || inf.BpduGuard || inf.BpduFilter || !string.IsNullOrWhiteSpace(inf.StpGuard) ||
                !string.IsNullOrWhiteSpace(inf.StpLinkType) || !string.IsNullOrWhiteSpace(inf.StpPortPriority) || !string.IsNullOrWhiteSpace(inf.StpCost))
            {
                var portFast = inf.PortFastTrunk ? "Trunk" : inf.PortFast ? "Ja" : string.Empty;
                stpInterfaceSettings.Add(string.Join("|", new[]
                {
                    inf.Name,
                    portFast,
                    inf.BpduGuard ? "Ja" : string.Empty,
                    inf.BpduFilter ? "Ja" : string.Empty,
                    inf.StpGuard,
                    inf.StpLinkType,
                    inf.StpPortPriority,
                    inf.StpCost
                }));
            }

            foreach (var values in inf.StpVlanParameters.Values)
                stpVlanInterfaceSettings.Add($"{inf.Name}|{values[0]}|{values[1]}|{values[2]}");

            foreach (var values in inf.StpMstParameters.Values)
                stpMstInterfaceSettings.Add($"{inf.Name}|{values[0]}|{values[1]}|{values[2]}");

            foreach (var parameter in inf.StpResetParameters)
                stpResetEntries.Add($"interface|{inf.Name}|{parameter}");

            if (inf.NoIpAddress)
                AddContextCommand(customKnown, $"interface {inf.Name}", "no ip address");

            if (inf.Nonegotiate && !inf.Mode.Equals("trunk", StringComparison.OrdinalIgnoreCase))
                AddContextCommand(customKnown, $"interface {inf.Name}", "switchport nonegotiate");

            if (inf.Mode.Equals("trunk", StringComparison.OrdinalIgnoreCase))
                trunkLines.Add($"{inf.Name}|{inf.AllowedVlans}|{inf.NativeVlan}|{inf.Description}|");

            if (!string.IsNullOrWhiteSpace(inf.VoiceVlan))
                voipLines.Add($"{inf.Name}|{inf.Description}|{inf.AccessVlan}|{inf.VoiceVlan}|{(inf.PortFast ? "Ja" : "Nein")}|{(inf.BpduGuard ? "Ja" : "Nein")}|AutoQoS|{(inf.PortSecurity ? "Ja" : "Nein")}");

            if (isSvi) continue;
            if (inf.Qinq) continue;

            if (!string.IsNullOrWhiteSpace(inf.Ip))
                interfaceLines.Add($"{inf.Name}|{inf.Description}|routed|{inf.Ip}|{inf.Mask}");
            else if (inf.Mode.Equals("routed", StringComparison.OrdinalIgnoreCase))
                AddContextCommand(customKnown, $"interface {inf.Name}", "no switchport");
            else if (inf.Mode.Equals("trunk", StringComparison.OrdinalIgnoreCase))
                interfaceLines.Add($"{inf.Name}|{inf.Description}|trunk|{inf.AllowedVlans}|");
            else if (!string.IsNullOrWhiteSpace(inf.AccessVlan) || inf.Mode.Equals("access", StringComparison.OrdinalIgnoreCase))
                interfaceLines.Add($"{inf.Name}|{inf.Description}|access|{inf.AccessVlan}|");
        }

        SetJoined(result, "interfacesList", interfaceLines, "interfaces");
        SetJoined(result, "sviList", sviLines, "vlans");
        SetJoined(result, "ipv6IfList", ipv6Lines, "ipv6");
        SetJoined(result, "trunkUplinkList", trunkLines, "trunkUplink");
        SetJoined(result, "voipInterfaceList", voipLines, "voip");
        SetJoined(result, "subifList", subifs, "subinterfaces");
    }


    private static bool TryParseStpVlanInterfaceParameter(string line, InterfaceInfo inf)
    {
        var match = Regex.Match(line, @"^spanning-tree\s+vlan\s+(\S+)\s+(port-priority|cost)\s+(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var key = match.Groups[1].Value;
        if (!inf.StpVlanParameters.TryGetValue(key, out var values))
        {
            values = new[] { key, string.Empty, string.Empty };
            inf.StpVlanParameters[key] = values;
        }
        if (match.Groups[2].Value.Equals("port-priority", StringComparison.OrdinalIgnoreCase)) values[1] = match.Groups[3].Value;
        else values[2] = match.Groups[3].Value;
        return true;
    }

    private static bool TryParseStpMstInterfaceParameter(string line, InterfaceInfo inf)
    {
        var match = Regex.Match(line, @"^spanning-tree\s+mst\s+(\d+)\s+(port-priority|cost)\s+(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var key = match.Groups[1].Value;
        if (!inf.StpMstParameters.TryGetValue(key, out var values))
        {
            values = new[] { key, string.Empty, string.Empty };
            inf.StpMstParameters[key] = values;
        }
        if (match.Groups[2].Value.Equals("port-priority", StringComparison.OrdinalIgnoreCase)) values[1] = match.Groups[3].Value;
        else values[2] = match.Groups[3].Value;
        return true;
    }

    private static bool TryParseStpInterfaceReset(string line, InterfaceInfo inf)
    {
        var match = Regex.Match(line, @"^no\s+spanning-tree\s+(portfast(?:\s+trunk)?|bpduguard(?:\s+enable)?|bpdufilter(?:\s+enable)?|guard|link-type|port-priority|cost)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var parameter = match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
        inf.StpResetParameters.Add(parameter);
        return true;
    }

    private static string ParseStpGlobalReset(string body)
    {
        var match = Regex.Match(body, @"^vlan\s+(\S+)\s+(priority|root|hello-time|forward-time|max-age)$", RegexOptions.IgnoreCase);
        if (match.Success) return $"vlan|{match.Groups[1].Value}|{match.Groups[2].Value.ToLowerInvariant()}";

        match = Regex.Match(body, @"^mst\s+(\d+)\s+(priority|root)$", RegexOptions.IgnoreCase);
        if (match.Success) return $"mst|{match.Groups[1].Value}|{match.Groups[2].Value.ToLowerInvariant()}";

        var global = body.ToLowerInvariant() switch
        {
            "portfast default" => "portfast-default",
            "portfast bpduguard default" => "bpduguard-default",
            "portfast bpdufilter default" => "bpdufilter-default",
            "loopguard default" => "loopguard-default",
            "etherchannel guard misconfig" => "etherchannel-guard",
            "uplinkfast" => "uplinkfast",
            "backbonefast" => "backbonefast",
            "mst max-hops" => "max-hops",
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(global) ? string.Empty : $"global||{global}";
    }

    private static void ParseSubinterface(InterfaceInfo inf, List<string> qinqSubifs, List<string> subifs, string line)
    {
        var first = Regex.Match(line, @"dot1Q\s+(\S+)", RegexOptions.IgnoreCase);
        var second = Regex.Match(line, @"second-dot1q\s+(\S+)", RegexOptions.IgnoreCase);
        var parent = inf.Name.Contains('.') ? inf.Name[..inf.Name.IndexOf('.')] : inf.Name;
        var sub = inf.Name.Contains('.') ? inf.Name[(inf.Name.IndexOf('.') + 1)..] : string.Empty;
        if (second.Success)
            qinqSubifs.Add($"{parent}|{sub}|{first.Groups[1].Value}|{second.Groups[1].Value}|{inf.Ip}|{inf.Mask}|{inf.Description}");
        else if (first.Success)
            subifs.Add($"{parent}|{sub}|{first.Groups[1].Value}|{inf.Ip}|{inf.Mask}");
    }

    private static string GuessInterfaceModule(InterfaceInfo inf)
    {
        if (inf.Qinq) return "qinq";
        if (!string.IsNullOrWhiteSpace(inf.VoiceVlan)) return "voip";
        if (inf.Mpls) return "mpls";
        if (inf.Name.Contains('.')) return "subinterfaces";
        if (inf.Name.StartsWith("Vlan", StringComparison.OrdinalIgnoreCase)) return "vlans";
        if (inf.Mode.Equals("trunk", StringComparison.OrdinalIgnoreCase)) return "trunkUplink";
        return "interfaces";
    }

    private static void SetJoined(ImportedConfigAnalysis result, string field, IEnumerable<string> lines, string module)
    {
        var value = string.Join(Environment.NewLine, lines.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(value)) return;
        result.Values[field] = value;
        result.Modules[module] = true;
    }

    private static void AppendValueLine(ImportedConfigAnalysis result, string field, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (result.Values.TryGetValue(field, out var existing) && !string.IsNullOrWhiteSpace(existing))
            result.Values[field] = existing + Environment.NewLine + line;
        else
            result.Values[field] = line;
    }

    private static string AppendCsv(string current, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return current;
        if (string.IsNullOrWhiteSpace(current)) return value;
        if (current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(value, StringComparer.OrdinalIgnoreCase)) return current;
        return current + "," + value;
    }

    private static void UpdatePipeValue(List<string> rows, string key, int index, string value)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var parts = rows[i].Split('|').ToList();
            if (!parts[0].Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            while (parts.Count <= index) parts.Add(string.Empty);
            parts[index] = value;
            rows[i] = string.Join('|', parts);
            return;
        }
    }

    private static string FirstTokenAfter(string line, string keyword)
    {
        var parts = SplitWords(line);
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals(keyword, StringComparison.OrdinalIgnoreCase)) return parts[i + 1];
        return string.Empty;
    }

    private static string ExtractFirst(string text, string pattern)
    {
        var m = Regex.Match(text ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : string.Empty;
    }

    private static string CollectBlock(string[] lines, ref int index, Func<string, bool> include)
    {
        var sb = new StringBuilder();
        var i = index + 1;
        while (i < lines.Length)
        {
            var line = CleanLine(lines[i]);
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("!", StringComparison.Ordinal)) { i++; continue; }
            if (!include(line)) break;
            sb.AppendLine(line);
            i++;
        }
        index = i - 1;
        return sb.ToString();
    }

    private static string ExtractBanner(string[] lines, ref int index, string firstLine)
    {
        var delimiter = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(delimiter) || delimiter.Length != 1)
            return firstLine;

        var delim = delimiter[0];
        var sb = new StringBuilder();
        var restStart = firstLine.IndexOf(delim);
        if (restStart >= 0 && restStart + 1 < firstLine.Length)
        {
            var rest = firstLine[(restStart + 1)..];
            var end = rest.IndexOf(delim);
            if (end >= 0) return rest[..end];
            sb.AppendLine(rest);
        }

        for (var i = index + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var pos = line.IndexOf(delim);
            if (pos >= 0)
            {
                sb.AppendLine(line[..pos]);
                index = i;
                return sb.ToString().TrimEnd();
            }
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }
}
