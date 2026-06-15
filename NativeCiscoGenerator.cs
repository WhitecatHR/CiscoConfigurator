using System.Text;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

/// <summary>
/// Native C# generator. Replaces the formerly embedded PowerShell generator engine.
/// It does not extract a PS1 file and does not start powershell.exe.
/// </summary>
public static class NativeCiscoGenerator
{
    public static Task<string> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var generator = new Builder(request);
        return Task.FromResult(generator.Generate());
    }

    private sealed class Builder
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        private readonly IReadOnlyDictionary<string, bool> _modules;
        private readonly List<string> _lines = new();

        public Builder(GenerationRequest request)
        {
            _values = request.Values ?? new Dictionary<string, string>();
            _modules = request.Modules ?? new Dictionary<string, bool>();
        }

        private string V(string name) => _values.TryGetValue(name, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
        private bool M(string name) => _modules.TryGetValue(name, out var value) && value;
        private string ConfigMode => string.IsNullOrWhiteSpace(V("configMode")) ? "Ohne VRF" : V("configMode");

        private static IEnumerable<string> Lines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;

            foreach (var raw in Regex.Split(text, "\\r?\\n"))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                yield return line;
            }
        }

        private static List<string> Parts(string line) => line.Split('|').Select(x => x.Trim()).ToList();
        private static bool Like(string value, string pattern) => Regex.IsMatch(value ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static bool YesLike(string value) => Like(value, "ja|yes|true");

        private void AddLine(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line)) _lines.Add(line);
        }

        private void Sec(string title)
        {
            if (_lines.Count > 0) _lines.Add(string.Empty);
            _lines.Add("!");
            _lines.Add("! ==================================================");
            _lines.Add($"! {title}");
            _lines.Add("! ==================================================");
        }

        private void AddBanner(string cmd, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var now = DateTime.Now;
            var body = text
                .Replace("{HOSTNAME}", V("hostname"), StringComparison.Ordinal)
                .Replace("{DEVICE_TYPE}", V("deviceType"), StringComparison.Ordinal)
                .Replace("{LOCATION}", V("location"), StringComparison.Ordinal)
                .Replace("{ADMIN}", V("admin"), StringComparison.Ordinal)
                .Replace("{COMPANY}", V("company"), StringComparison.Ordinal)
                .Replace("{DATE}", now.ToString("dd.MM.yyyy"), StringComparison.Ordinal)
                .Replace("{TIME}", now.ToString("HH:mm"), StringComparison.Ordinal);

            _lines.Add($"{cmd} ^");
            foreach (var x in Regex.Split(body, "\\r?\\n")) _lines.Add(x);
            _lines.Add("^");
        }

        public string Generate()
        {
            Sec("KONFIGURATIONSMODUS");
            AddLine($"! Modus: {ConfigMode}");

            GenerateBasic();
            GenerateRoutingBase();
            GenerateBanner();
            GenerateSsh();
            GenerateAaa();
            GenerateRadiusTacacs();
            GenerateMonitoring();
            GenerateInterfaces();
            GenerateRanges();
            GenerateRouterOnAStick();
            GenerateSubinterfaces();
            GenerateVlans();
            GenerateSwitchSecurity();
            GenerateStaticRoutes();
            GenerateOspf();
            GenerateIsis();
            GenerateBgp();
            GenerateFhrp();
            GenerateIpv6Global();
            GenerateIpv6();
            GenerateDhcp();
            GenerateAcl();
            GenerateHardening();
            GenerateNat();
            GenerateGre();
            GenerateCustomCommands();
            GenerateAccessBaseline();
            GenerateGreIpsec();
            GenerateVrfLite();
            GenerateQosBasic();
            GenerateErrdisableRecovery();
            GenerateInterfaceProfiles();
            GenerateTrunkUplink();
            GenerateEtherChannelExtended();
            GenerateStpExtended();
            GenerateSwitchSecurityExtended();
            GeneratePortSecurityExtended();
            GenerateRouteMapFilter();
            GenerateIpSlaTracking();
            GenerateIpv6Extended();
            GenerateOspfv3();
            GenerateWanFailover();
            GenerateZoneFirewall();
            GenerateDmzAssistant();
            GenerateVpnAdvanced();
            GenerateUserRights();
            GeneratePacketTracerNote();
            GenerateVrfDefinitions();
            GenerateVrfSvi();
            GenerateVrfStaticRoutes();
            GenerateVrfOspf();
            GenerateVrfOspfv3();
            GenerateVrfBgp();
            GenerateIpv6RoutingProtocols();
            GenerateInterfaceRoles();
            GenerateVlanIpPlan();
            GenerateAclAssistant();
            GenerateOspfAdvanced();
            GenerateBgpAdvanced();
            GenerateMpls();
            GenerateVoip();
            GenerateQinQ();

            Sec("ABSCHLUSS");
            AddLine("end");
            if (V("writeMem") == "Ja") AddLine("write memory");

            return NormalizeCiscoPasteScript(string.Join("\r\n", _lines));
        }

        private void GenerateBasic()
        {
            if (!M("basic")) return;
            Sec("BASIS");
            AddLine($"hostname {V("hostname")}");
            if (V("domain") != string.Empty) AddLine($"ip domain-name {V("domain")}");
            if (V("enableSecret") != string.Empty) AddLine($"enable secret {V("enableSecret")}");
            if (V("noDomainLookup") == "Ja") AddLine("no ip domain-lookup");
            if (V("servicePass") == "Ja") AddLine("service password-encryption");
            if (V("minPass") != string.Empty) AddLine($"security passwords min-length {V("minPass")}");
            if (V("clockTimezoneName") != string.Empty && V("clockTimezoneHours") != string.Empty)
            {
                var minutes = string.IsNullOrWhiteSpace(V("clockTimezoneMinutes")) ? "0" : V("clockTimezoneMinutes");
                AddLine($"clock timezone {V("clockTimezoneName")} {V("clockTimezoneHours")} {minutes}");
            }
            if (V("clockSummerName") != string.Empty && V("clockSummerRule") != string.Empty)
                AddLine($"clock summer-time {V("clockSummerName")} {V("clockSummerRule")}");
        }

        private void GenerateRoutingBase()
        {
            if (!M("routingBase")) return;
            Sec("ROUTING ALLGEMEIN");
            if (V("ipRouting") == "Ja") AddLine("ip routing");
            else if (V("ipRouting") == "Nein") AddLine("no ip routing");
        }

        private void GenerateBanner()
        {
            if (!M("banner")) return;
            Sec("BANNER");
            AddBanner("banner motd", V("motd"));
            AddBanner("banner login", V("loginBanner"));
        }

        private void GenerateSsh()
        {
            if (!M("ssh")) return;
            Sec("SSH / LINE VTY");
            if (V("domain") != string.Empty) AddLine($"ip domain-name {V("domain")}");
            var rsa = string.IsNullOrWhiteSpace(V("rsaModulus")) ? "2048" : V("rsaModulus");
            AddLine("crypto key generate rsa");
            AddLine(rsa);
            AddLine("ip ssh version 2");
            if (V("sshTimeout") != string.Empty) AddLine($"ip ssh time-out {V("sshTimeout")}");
            if (V("sshRetries") != string.Empty) AddLine($"ip ssh authentication-retries {V("sshRetries")}");
            if (V("sshSourceIf") != string.Empty) AddLine($"ip ssh source-interface {V("sshSourceIf")}");
            if (V("sshLogEvents") == "Ja") AddLine("ip ssh logging events");
            if (V("sshAlgorithm") != string.Empty && V("sshAlgorithm") != "Standard") AddLine($"ip ssh server algorithm encryption {V("sshAlgorithm")}");

            var vtyStart = string.IsNullOrWhiteSpace(V("vtyStart")) ? "0" : V("vtyStart");
            var vtyEnd = string.IsNullOrWhiteSpace(V("vtyEnd")) ? "4" : V("vtyEnd");
            AddLine($"line vty {vtyStart} {vtyEnd}");
            if (V("vtyAclSsh") != string.Empty) AddLine($" access-class {V("vtyAclSsh")} in");

            var loginMode = V("vtyLoginMode");
            if (loginMode == "login authentication default") AddLine(" login authentication default");
            else if (loginMode == "password login")
            {
                if (V("vtyPassword") != string.Empty) AddLine($" password {V("vtyPassword")}");
                AddLine(" login");
            }
            else AddLine(" login local");

            AddLine(" transport input ssh");
            if (V("vtyExecTimeout") != string.Empty) AddLine($" exec-timeout {V("vtyExecTimeout")}");
            if (V("vtyLoggingSync") == "Ja") AddLine(" logging synchronous");
            if (V("vtyPrivilege") != string.Empty) AddLine($" privilege level {V("vtyPrivilege")}");
        }

        private void GenerateAaa()
        {
            if (!M("aaa")) return;
            Sec("AAA / CONSOLE");
            if (V("aaaLocal") == "Ja" && !M("radiusTacacs"))
            {
                AddLine("aaa new-model");
                AddLine("aaa authentication login default local");
                AddLine("aaa authorization exec default local");
            }
            var timeout = string.IsNullOrWhiteSpace(V("execTimeout")) ? "10 0" : V("execTimeout");
            AddLine("line console 0");
            if (V("consoleLoggingSync") == "Ja") AddLine(" logging synchronous");
            AddLine($" exec-timeout {timeout}");
            if (V("consoleLogin") == "login local") AddLine(" login local");
        }

        private void GenerateRadiusTacacs()
        {
            if (!M("radiusTacacs")) return;
            Sec("RADIUS TACACS AAA SERVER");
            foreach (var line in Lines(V("radiusList")))
            {
                var p = Parts(line);
                if (p.Count < 2) continue;
                var name = p[0]; var ip = p[1]; var auth = "1812"; var acct = "1813"; var key = ""; var timeout = ""; var retransmit = "";
                if (p.Count >= 3 && p[2] != string.Empty) auth = p[2];
                if (p.Count >= 4 && p[3] != string.Empty) acct = p[3];
                if (p.Count >= 5) key = p[4];
                if (p.Count >= 6) timeout = p[5];
                if (p.Count >= 7) retransmit = p[6];
                AddLine($"radius server {name}");
                AddLine($" address ipv4 {ip} auth-port {auth} acct-port {acct}");
                if (timeout != string.Empty) AddLine($" timeout {timeout}");
                if (retransmit != string.Empty) AddLine($" retransmit {retransmit}");
                if (key != string.Empty) AddLine($" key {key}");
            }
            foreach (var line in Lines(V("tacacsList")))
            {
                var p = Parts(line);
                if (p.Count < 2) continue;
                AddLine($"tacacs server {p[0]}");
                AddLine($" address ipv4 {p[1]}");
                if (p.Count >= 3 && p[2] != string.Empty) AddLine($" key {p[2]}");
            }
            if (V("aaaRadiusGroup") != string.Empty)
            {
                AddLine($"aaa group server radius {V("aaaRadiusGroup")}");
                foreach (var line in Lines(V("radiusList")))
                {
                    var p = Parts(line);
                    if (p.Count >= 1) AddLine($" server name {p[0]}");
                }
            }
            if (V("aaaUseRadius") == "Ja")
            {
                AddLine("aaa new-model");
                if (V("aaaRadiusGroup") != string.Empty)
                {
                    AddLine($"aaa authentication login default group {V("aaaRadiusGroup")} local");
                    AddLine($"aaa authorization exec default group {V("aaaRadiusGroup")} local");
                    AddLine($"aaa accounting exec default start-stop group {V("aaaRadiusGroup")}");
                }
                else
                {
                    AddLine("aaa authentication login default group radius local");
                    AddLine("aaa authorization exec default group radius local");
                    AddLine("aaa accounting exec default start-stop group radius");
                }
            }
            if (V("radiusSourceIf") != string.Empty) AddLine($"ip radius source-interface {V("radiusSourceIf")}");
        }

        private void GenerateMonitoring()
        {
            if (!M("monitoringBase")) return;
            Sec("NTP SYSLOG SNMP");
            foreach (var line in Lines(V("ntpList")))
            {
                var p = Parts(line);
                if (p.Count < 1) continue;
                var cmd = $"ntp server {p[0]}";
                if (p.Count >= 2 && Like(p[1], "prefer|ja|yes|true")) cmd += " prefer";
                AddLine(cmd);
            }
            foreach (var line in Lines(V("syslogList")))
            {
                var p = Parts(line);
                if (p.Count < 1) continue;
                if (p.Count >= 2) AddLine($"logging trap {p[1]}");
                AddLine($"logging host {p[0]}");
            }
            foreach (var line in Lines(V("snmpList")))
            {
                var p = Parts(line);
                if (p.Count < 6) continue;
                AddLine($"snmp-server group {p[0]} v3 priv");
                AddLine($"snmp-server user {p[1]} {p[0]} v3 auth {p[2]} {p[3]} priv {p[4]} {p[5]}");
            }
            if (V("logTimestamps") == "Ja")
            {
                AddLine("service timestamps debug datetime msec localtime show-timezone");
                AddLine("service timestamps log datetime msec localtime show-timezone");
            }
            if (V("logBuffered") != string.Empty) AddLine($"logging buffered {V("logBuffered")}");
            if (V("logTrap") != string.Empty) AddLine($"logging trap {V("logTrap")}");
            if (V("archiveLog") == "Ja")
            {
                AddLine("archive"); AddLine(" log config"); AddLine("  logging enable"); AddLine("  notify syslog"); AddLine("  hidekeys");
            }
            if (V("loginAudit") == "Ja") { AddLine("login on-failure log"); AddLine("login on-success log"); }
            if (V("archivePath") != string.Empty || V("archiveWriteMemory") == "Ja" || V("archiveTimePeriod") != string.Empty)
            {
                AddLine("archive");
                if (V("archivePath") != string.Empty) AddLine($" path {V("archivePath")}");
                if (V("archiveWriteMemory") == "Ja") AddLine(" write-memory");
                if (V("archiveTimePeriod") != string.Empty) AddLine($" time-period {V("archiveTimePeriod")}");
            }
        }

        private void GenerateInterfaces()
        {
            if (!M("interfaces")) return;
            Sec("EINZELINTERFACES");
            foreach (var line in Lines(V("interfacesList")))
            {
                var p = Parts(line);
                if (p.Count < 3) continue;
                AddLine($"interface {p[0]}");
                if (p[1] != string.Empty) AddLine($" description {p[1]}");
                if (p[2] == "routed")
                {
                    AddLine(" no switchport");
                    if (p.Count >= 5) AddLine($" ip address {p[3]} {p[4]}");
                }
                else if (p[2] == "access")
                {
                    AddLine(" switchport mode access");
                    if (p.Count >= 4) AddLine($" switchport access vlan {p[3]}");
                }
                else if (p[2] == "trunk")
                {
                    AddLine(" switchport mode trunk");
                    if (p.Count >= 4) AddLine($" switchport trunk allowed vlan {p[3]}");
                }
                AddLine(" no shutdown");
            }
        }

        private void GenerateRanges()
        {
            if (!M("ranges")) return;
            Sec("INTERFACE RANGES");
            foreach (var line in Lines(V("rangesList")))
            {
                var p = Parts(line);
                if (p.Count < 4) continue;
                AddLine($"interface range {p[0]}");
                if (p[1] != string.Empty) AddLine($" description {p[1]}");
                if (p[2] == "access") { AddLine(" switchport mode access"); AddLine($" switchport access vlan {p[3]}"); }
                if (p[2] == "trunk") { AddLine(" switchport mode trunk"); AddLine($" switchport trunk allowed vlan {p[3]}"); }
                if (p.Count >= 5 && Like(p[4], "ja|yes|true|portfast")) AddLine(" spanning-tree portfast");
                if (p.Count >= 6 && Like(p[5], "ja|yes|true|bpdu")) AddLine(" spanning-tree bpduguard enable");
                if (p.Count >= 7 && Like(p[6], "ja|yes|true|portsec")) { AddLine(" switchport port-security"); AddLine(" switchport port-security mac-address sticky"); }
            }
        }

        private void GenerateRouterOnAStick()
        {
            if (!M("routerStick")) return;
            Sec("ROUTER-ON-A-STICK");
            var parent = V("rosParentIf");
            var native = V("rosNativeVlan");
            if (parent == string.Empty) return;
            AddLine($"interface {parent}");
            if (V("rosParentNoShutdown") == "Ja") AddLine(" no shutdown");
            foreach (var line in Lines(V("rosVlanList")))
            {
                var p = Parts(line);
                if (p.Count < 4) continue;
                var vlan = p[0]; var name = p[1]; var ip = p[2]; var mask = p[3];
                AddLine($"interface {parent}.{vlan}");
                AddLine($" description Router-on-a-Stick VLAN {vlan} {name}");
                AddLine(native != string.Empty && native == vlan ? $" encapsulation dot1Q {vlan} native" : $" encapsulation dot1Q {vlan}");
                AddLine($" ip address {ip} {mask}");
                AddLine(" no shutdown");
                AddLine(string.Empty);
            }
        }

        private void GenerateSubinterfaces()
        {
            if (!M("subinterfaces")) return;
            Sec("SUBINTERFACES");
            foreach (var line in Lines(V("subifList")))
            {
                var p = Parts(line);
                if (p.Count < 5) continue;
                AddLine($"interface {p[0]}.{p[1]}");
                AddLine($" encapsulation dot1Q {p[2]}");
                AddLine($" ip address {p[3]} {p[4]}");
                AddLine(" no shutdown");
            }
        }

        private void GenerateVlans()
        {
            if (!M("vlans")) return;
            Sec("VLANS UND SVIS");
            foreach (var line in Lines(V("vlanList")))
            {
                var p = Parts(line);
                if (p.Count >= 1) { AddLine($"vlan {p[0]}"); if (p.Count >= 2) AddLine($" name {p[1]}"); }
            }
            foreach (var line in Lines(V("sviList")))
            {
                var p = Parts(line);
                if (p.Count >= 3) { AddLine($"interface vlan {p[0]}"); AddLine($" ip address {p[1]} {p[2]}"); AddLine(" no shutdown"); }
            }
            if (V("defaultGateway") != string.Empty) AddLine($"ip default-gateway {V("defaultGateway")}");
        }

        private void GenerateSwitchSecurity()
        {
            if (!M("switchSec")) return;
            Sec("SWITCH SECURITY");
            if (V("dhcpSnoopVlans") != string.Empty) { AddLine("ip dhcp snooping"); AddLine($"ip dhcp snooping vlan {V("dhcpSnoopVlans")}"); AddLine("no ip dhcp snooping information option"); }
            if (V("daiVlans") != string.Empty) AddLine($"ip arp inspection vlan {V("daiVlans")}");
        }

        private void GenerateStaticRoutes()
        {
            if (!M("staticRoutes")) return;
            Sec("STATISCHE ROUTEN");
            foreach (var line in Lines(V("staticRouteList")))
            {
                var p = Parts(line);
                if (p.Count < 3) continue;
                var cmd = $"ip route {p[0]} {p[1]} {p[2]}";
                if (p.Count >= 4) cmd += $" {p[3]}";
                AddLine(cmd);
            }
        }

        private void GenerateOspf()
        {
            if (!M("ospf")) return;
            Sec("OSPF");
            var pid = string.IsNullOrWhiteSpace(V("ospfPid")) ? "1" : V("ospfPid");
            var mode = string.IsNullOrWhiteSpace(V("ospfConfigMode")) ? "Prozessbasiert" : V("ospfConfigMode");
            var useProcessNetworks = !mode.Equals("Interfacebasiert", StringComparison.OrdinalIgnoreCase);
            var useInterfaceAssignments = !mode.Equals("Prozessbasiert", StringComparison.OrdinalIgnoreCase);
            AddLine($"router ospf {pid}");
            if (V("ospfRid") != string.Empty) AddLine($" router-id {V("ospfRid")}");
            if (V("ospfRef") != string.Empty) AddLine($" auto-cost reference-bandwidth {V("ospfRef")}");
            if (V("ospfLogAdjacency") == "Ja") AddLine(" log-adjacency-changes");
            else if (V("ospfLogAdjacency") == "Detail") AddLine(" log-adjacency-changes detail");
            if (useProcessNetworks)
            {
                foreach (var line in Lines(V("ospfNetList")))
                {
                    var p = Parts(line);
                    if (p.Count >= 3) AddLine($" network {p[0]} {p[1]} area {p[2]}");
                }
            }
            if (V("ospfPassiveDefault") == "Ja") AddLine(" passive-interface default");
            foreach (var ifc in Lines(V("ospfNoPassiveList"))) AddLine($" no passive-interface {ifc}");
            if (V("ospfDefaultOriginate") == "Ja") AddLine(" default-information originate");
            else if (V("ospfDefaultOriginate") == "Immer") AddLine(" default-information originate always");
            foreach (var area in Lines(V("ospfStubAreas"))) AddLine($" area {area} stub no-summary");
            if (useInterfaceAssignments)
            {
                foreach (var line in Lines(V("ospfInterfaceAreaList")))
                {
                    var p = Parts(line);
                    if (p.Count < 2 || p[0] == string.Empty || p[1] == string.Empty) continue;
                    AddLine($"interface {p[0]}");
                    AddLine($" ip ospf {pid} area {p[1]}");
                }
            }
            foreach (var line in Lines(V("ospfIfSettings")))
            {
                var p = Parts(line); if (p.Count < 1) continue;
                AddLine($"interface {p[0]}");
                if (p.Count >= 2 && p[1] != string.Empty) AddLine($" ip ospf cost {p[1]}");
                if (p.Count >= 3 && p[2] != string.Empty) AddLine($" ip ospf priority {p[2]}");
                if (p.Count >= 4 && p[3] != string.Empty) AddLine($" ip ospf network {p[3]}");
            }
        }

        private void GenerateIsis()
        {
            if (!M("isis")) return;
            Sec("IS-IS");
            var tag = string.IsNullOrWhiteSpace(V("isisTag")) ? "CORE" : V("isisTag");
            AddLine($"router isis {tag}");
            if (V("isisNet") != string.Empty) AddLine($" net {V("isisNet")}");
            AddLine(" metric-style wide");
            foreach (var line in Lines(V("isisIfList"))) { var p = Parts(line); if (p.Count >= 1) { AddLine($"interface {p[0]}"); AddLine($" ip router isis {tag}"); } }
        }

        private void GenerateBgp()
        {
            if (!M("bgp")) return;
            Sec("BGP");
            if (V("bgpAs") == string.Empty) return;
            AddLine($"router bgp {V("bgpAs")}");
            if (V("bgpRid") != string.Empty) AddLine($" bgp router-id {V("bgpRid")}");
            foreach (var line in Lines(V("bgpNeighborList")))
            {
                var p = Parts(line); if (p.Count < 2) continue;
                AddLine($" neighbor {p[0]} remote-as {p[1]}");
                if (p.Count >= 3 && p[2] != string.Empty) AddLine($" neighbor {p[0]} description {p[2]}");
            }
            foreach (var line in Lines(V("bgpNetworkList"))) { var p = Parts(line); if (p.Count >= 2) AddLine($" network {p[0]} mask {p[1]}"); }
            foreach (var line in Lines(V("bgpExtNeighborList")))
            {
                var p = Parts(line); if (p.Count < 1) continue; var n = p[0];
                if (p.Count >= 2 && p[1] != string.Empty) AddLine($" neighbor {n} update-source {p[1]}");
                if (p.Count >= 3 && p[2] != string.Empty) AddLine($" neighbor {n} ebgp-multihop {p[2]}");
                if (p.Count >= 4 && YesLike(p[3])) AddLine($" neighbor {n} next-hop-self");
                if (p.Count >= 5 && p[4] != string.Empty) AddLine($" neighbor {n} password {p[4]}");
                if (p.Count >= 6 && p[5] != string.Empty) AddLine($" neighbor {n} maximum-prefix {p[5]}");
                if (p.Count >= 7 && YesLike(p[6])) AddLine($" neighbor {n} soft-reconfiguration inbound");
                if (p.Count >= 8 && YesLike(p[7])) AddLine($" neighbor {n} default-originate");
            }
        }

        private void GenerateFhrp()
        {
            if (!M("fhrp")) return;
            Sec("FIRST HOP REDUNDANCY");
            foreach (var line in Lines(V("hsrpList")))
            {
                var p = Parts(line); if (p.Count < 3) continue;
                AddLine($"interface {p[0]}");
                AddLine($" standby {p[1]} ip {p[2]}");
                if (p.Count >= 4 && p[3] != string.Empty) AddLine($" standby {p[1]} priority {p[3]}");
                if (p.Count >= 5 && Like(p[4], "preempt|ja|yes|true")) AddLine($" standby {p[1]} preempt");
            }
        }

        private void GenerateIpv6Global()
        {
            if (M("ipv6") || M("ipv6Extended") || M("ospfv3"))
            {
                Sec("IPV6 GLOBAL");
                AddLine("ipv6 unicast-routing");
            }
        }

        private void GenerateIpv6()
        {
            if (!M("ipv6")) return;
            Sec("IPV6");
            foreach (var line in Lines(V("ipv6IfList")))
            {
                var p = Parts(line); if (p.Count < 2) continue;
                AddLine($"interface {p[0]}");
                AddLine($" ipv6 address {p[1]}");
                if (p.Count >= 3 && p[2] != string.Empty) AddLine($" ipv6 address {p[2]} link-local");
            }
        }

        private void GenerateDhcp()
        {
            if (!M("dhcp")) return;
            Sec("DHCP");
            var dhcpExtNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Lines(V("dhcpExtPoolList"))) { var ep = Parts(e); if (ep.Count >= 1) dhcpExtNames.Add(ep[0]); }
            foreach (var line in Lines(V("dhcpPoolList")))
            {
                var p = Parts(line);
                if (p.Count >= 5 && !dhcpExtNames.Contains(p[0])) { AddLine($"ip dhcp pool {p[0]}"); AddLine($" network {p[1]} {p[2]}"); AddLine($" default-router {p[3]}"); AddLine($" dns-server {p[4]}"); }
            }
            foreach (var line in Lines(V("dhcpExcludedList")))
            {
                var p = Parts(line); if (p.Count < 1) continue;
                AddLine(p.Count >= 2 && p[1] != string.Empty ? $"ip dhcp excluded-address {p[0]} {p[1]}" : $"ip dhcp excluded-address {p[0]}");
            }
            foreach (var line in Lines(V("dhcpExtPoolList")))
            {
                var p = Parts(line); if (p.Count < 5) continue;
                AddLine($"ip dhcp pool {p[0]}"); AddLine($" network {p[1]} {p[2]}"); AddLine($" default-router {p[3]}"); AddLine($" dns-server {p[4]}");
                if (p.Count >= 6 && p[5] != string.Empty) AddLine($" domain-name {p[5]}");
                if (p.Count >= 7 && p[6] != string.Empty) AddLine($" lease {p[6]}");
                if (p.Count >= 8 && p[7] != string.Empty) AddLine($" option 150 ip {p[7]}");
            }
        }

        private void GenerateAcl()
        {
            if (!M("acl")) return;
            Sec("ACL");
            var aclHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in Lines(V("aclList")))
            {
                var p = Parts(line); if (p.Count < 5) continue;
                var header = $"ip access-list extended {p[0]}";
                if (aclHeaders.Add(header)) AddLine(header);
                AddLine($" {p[1]} {p[2]} {p[3]} {p[4]}");
            }
            foreach (var line in Lines(V("aclApplyList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"interface {p[0]}"); AddLine($" ip access-group {p[1]} {p[2]}"); } }
            foreach (var line in Lines(V("stdAclList"))) { var p = Parts(line); if (p.Count >= 3) AddLine($"access-list {p[0]} {p[1]} {p[2]}"); }
            foreach (var line in Lines(V("extAclList")))
            {
                var p = Parts(line); if (p.Count < 5) continue;
                var header = $"ip access-list extended {p[0]}";
                if (aclHeaders.Add(header)) AddLine(header);
                AddLine($" {p[1]} {p[2]} {p[3]} {p[4]}");
            }
            foreach (var line in Lines(V("aclAssignExtList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"interface {p[0]}"); AddLine($" ip access-group {p[1]} {p[2]}"); } }
        }

        private void GenerateHardening()
        {
            if (!M("security")) return;
            Sec("HARDENING");
            AddLine("login block-for 120 attempts 3 within 60");
            if (V("minPass") == string.Empty) AddLine("security passwords min-length 10");
            AddLine("no ip http server");
            AddLine("no ip http secure-server");
            if (V("hardNoPad") == "Ja") AddLine("no service pad");
            if (V("hardNoSourceRoute") == "Ja") AddLine("no ip source-route");
            if (V("hardNoBootp") == "Ja") AddLine("no ip bootp server");
            if (V("hardCdp") == "Nein") AddLine("no cdp run");
            if (V("hardLldp") == "Ja") AddLine("lldp run");
            if (V("hardUnusedRange") != string.Empty) { AddLine($"interface range {V("hardUnusedRange")}"); AddLine(" description UNUSED - SHUTDOWN"); AddLine(" shutdown"); }
        }

        private void GenerateNat()
        {
            if (!M("nat")) return;
            Sec("NAT");
            foreach (var line in Lines(V("natIfList"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"interface {p[0]}"); AddLine($" ip nat {p[1]}"); } }
            var natMode = string.IsNullOrWhiteSpace(V("natMode")) ? "PAT Overload" : V("natMode");
            if (natMode is "PAT Overload" or "Alle") foreach (var line in Lines(V("natOverloadList"))) { var p = Parts(line); if (p.Count >= 2) AddLine($"ip nat inside source list {p[0]} interface {p[1]} overload"); }
            if (natMode is "Static NAT" or "Alle") foreach (var line in Lines(V("natStaticList"))) { var p = Parts(line); if (p.Count >= 2) AddLine($"ip nat inside source static {p[0]} {p[1]}"); }
            if (natMode is "Portforward" or "Alle") foreach (var line in Lines(V("natPortList"))) { var p = Parts(line); if (p.Count >= 5) AddLine($"ip nat inside source static {p[0]} {p[1]} {p[2]} {p[3]} {p[4]}"); }
            if (natMode is "Dynamic Pool" or "Alle") foreach (var line in Lines(V("natPoolList"))) { var p = Parts(line); if (p.Count >= 5) { AddLine($"ip nat pool {p[0]} {p[1]} {p[2]} netmask {p[3]}"); AddLine($"ip nat inside source list {p[4]} pool {p[0]} overload"); } }
            if (natMode is "Exemption" or "Alle") foreach (var line in Lines(V("natExemptList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"route-map {p[0]} permit 10"); AddLine($" match ip address {p[1]}"); AddLine($" set interface {p[2]}"); } }
        }

        private void GenerateGre()
        {
            if (!M("vpn")) return;
            Sec("GRE");
            foreach (var line in Lines(V("greList"))) { var p = Parts(line); if (p.Count >= 5) { AddLine($"interface tunnel {p[0]}"); AddLine($" ip address {p[1]} {p[2]}"); AddLine($" tunnel source {p[3]}"); AddLine($" tunnel destination {p[4]}"); } }
        }

        private void GenerateCustomCommands()
        {
            if (!M("customCommands")) return;
            Sec("EIGENE ZUSATZBEFEHLE");
            foreach (var line in Lines(V("customConfigList"))) AddLine(line);
        }

        private void GenerateAccessBaseline()
        {
            if (!M("accessBaseline")) return;
            Sec("ACCESS SWITCH BASELINE");
            foreach (var line in Lines(V("accessBaseList")))
            {
                var p = Parts(line); if (p.Count < 3) continue;
                var range = p[0]; var desc = p[1]; var accessVlan = p[2];
                var voice = ""; var pf = "ja"; var bpdu = "ja"; var portsec = "nein"; var storm = ""; var shutdown = "nein"; var nonego = "ja";
                if (p.Count >= 4) voice = p[3]; if (p.Count >= 5) pf = p[4]; if (p.Count >= 6) bpdu = p[5]; if (p.Count >= 7) portsec = p[6]; if (p.Count >= 8) storm = p[7]; if (p.Count >= 9) shutdown = p[8]; if (p.Count >= 10) nonego = p[9];
                AddLine($"interface range {range}");
                if (desc != string.Empty) AddLine($" description {desc}");
                AddLine(" switchport mode access"); AddLine($" switchport access vlan {accessVlan}");
                if (voice != string.Empty) AddLine($" switchport voice vlan {voice}");
                if (YesLike(nonego)) AddLine(" switchport nonegotiate");
                if (YesLike(pf)) AddLine(" spanning-tree portfast");
                if (YesLike(bpdu)) AddLine(" spanning-tree bpduguard enable");
                if (YesLike(portsec)) { AddLine(" switchport port-security"); AddLine(" switchport port-security mac-address sticky"); AddLine(" switchport port-security violation restrict"); }
                if (storm != string.Empty) { AddLine($" storm-control broadcast level {storm}"); AddLine($" storm-control multicast level {storm}"); }
                AddLine(YesLike(shutdown) ? " shutdown" : " no shutdown");
                AddLine(string.Empty);
            }
        }

        private void GenerateGreIpsec()
        {
            if (!M("greIpsec")) return;
            Sec("GRE OVER IPSEC");
            foreach (var line in Lines(V("greIpsecList")))
            {
                var p = Parts(line); if (p.Count < 9) continue;
                var tun = p[0]; var tunIp = p[1]; var mask = p[2]; var src = p[3]; var dst = p[4]; var psk = p[5]; var acl = p[6]; var crypto = p[7]; var transform = p[8];
                AddLine("crypto isakmp policy 10"); AddLine(" encr aes 256"); AddLine(" hash sha256"); AddLine(" authentication pre-share"); AddLine(" group 14");
                AddLine($"crypto isakmp key {psk} address {dst}");
                AddLine($"crypto ipsec transform-set {transform} esp-aes 256 esp-sha-hmac"); AddLine(" mode transport");
                AddLine($"crypto map {crypto} 10 ipsec-isakmp"); AddLine($" set peer {dst}"); AddLine($" set transform-set {transform}"); AddLine($" match address {acl}");
                AddLine($"interface tunnel {tun}"); AddLine($" ip address {tunIp} {mask}"); AddLine($" tunnel source {src}"); AddLine($" tunnel destination {dst}"); AddLine(" tunnel mode gre ip"); AddLine(" ip mtu 1400"); AddLine(" ip tcp adjust-mss 1360");
            }
        }

        private void GenerateVrfLite()
        {
            if (!M("vrfLite")) return;
            Sec("VRF-LITE");
            foreach (var line in Lines(V("vrfList"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"ip vrf {p[0]}"); AddLine($" rd {p[1]}"); if (p.Count >= 3 && p[2] != string.Empty) AddLine($" description {p[2]}"); } }
            foreach (var line in Lines(V("vrfInterfaceList"))) { var p = Parts(line); if (p.Count >= 4) { AddLine($"interface {p[0]}"); AddLine($" ip vrf forwarding {p[1]}"); AddLine($" ip address {p[2]} {p[3]}"); AddLine(" no shutdown"); } }
            foreach (var line in Lines(V("vrfRouteList"))) { var p = Parts(line); if (p.Count >= 4) AddLine($"ip route vrf {p[0]} {p[1]} {p[2]} {p[3]}"); }
        }

        private void GenerateQosBasic()
        {
            if (!M("qosBasic")) return;
            Sec("QOS BASIC");
            foreach (var ifc in Lines(V("qosTrustInterfaces"))) { AddLine($"interface {ifc}"); AddLine(" mls qos trust dscp"); AddLine(" auto qos trust"); }
            foreach (var line in Lines(V("qosPolicyList"))) { var p = Parts(line); if (p.Count >= 4) { AddLine($"class-map match-any {p[0]}"); AddLine($" match dscp {p[1]}"); AddLine($"policy-map {p[2]}"); AddLine($" class {p[0]}"); AddLine($"  priority percent {p[3]}"); } }
        }

        private void GenerateErrdisableRecovery()
        {
            if (!M("errdisableRecovery")) return;
            Sec("ERRDISABLE RECOVERY");
            if (V("errdisableInterval") != string.Empty) AddLine($"errdisable recovery interval {V("errdisableInterval")}");
            foreach (var cause in Lines(V("errdisableCauseList"))) AddLine($"errdisable recovery cause {cause}");
        }

        private void GenerateInterfaceProfiles()
        {
            if (!M("interfaceProfiles")) return;
            Sec("INTERFACE PROFILE ASSISTENT");
            foreach (var line in Lines(V("interfaceProfileList")))
            {
                var p = Parts(line); if (p.Count < 3) continue;
                var ifc = p[0]; var profile = p[1].ToLowerInvariant(); var desc = p[2];
                AddLine($"interface {ifc}");
                if (desc != string.Empty) AddLine($" description {desc}");
                if (profile == "access") { AddLine(" switchport mode access"); if (p.Count >= 4) AddLine($" switchport access vlan {p[3]}"); AddLine(" spanning-tree portfast"); AddLine(" spanning-tree bpduguard enable"); }
                else if (profile == "voice") { AddLine(" switchport mode access"); if (p.Count >= 4) AddLine($" switchport access vlan {p[3]}"); if (p.Count >= 5) AddLine($" switchport voice vlan {p[4]}"); AddLine(" spanning-tree portfast"); AddLine(" spanning-tree bpduguard enable"); }
                else if (profile is "trunk" or "uplink") { AddLine(" switchport mode trunk"); if (p.Count >= 4) AddLine($" switchport trunk allowed vlan {p[3]}"); if (p.Count >= 5 && p[4] != string.Empty) AddLine($" switchport trunk native vlan {p[4]}"); AddLine(" switchport nonegotiate"); if (profile == "uplink") AddLine(" spanning-tree guard root"); }
                else if (profile == "server") { AddLine(" switchport mode access"); if (p.Count >= 4) AddLine($" switchport access vlan {p[3]}"); AddLine(" spanning-tree portfast"); }
                else if (profile == "ap") { AddLine(" switchport mode trunk"); if (p.Count >= 4) AddLine($" switchport trunk native vlan {p[3]}"); if (p.Count >= 5) AddLine($" switchport trunk allowed vlan {p[4]}"); AddLine(" switchport nonegotiate"); }
                else if (profile == "unused") { AddLine(" switchport mode access"); if (p.Count >= 4) AddLine($" switchport access vlan {p[3]}"); AddLine(" shutdown"); }
                else if (profile == "routed") { AddLine(" no switchport"); if (p.Count >= 5) AddLine($" ip address {p[3]} {p[4]}"); AddLine(" no shutdown"); }
                AddLine(string.Empty);
            }
        }

        private void GenerateTrunkUplink()
        {
            if (!M("trunkUplink")) return;
            Sec("TRUNK / UPLINK ASSISTENT");
            foreach (var line in Lines(V("trunkUplinkList")))
            {
                var p = Parts(line); if (p.Count < 3) continue;
                AddLine($"interface {p[0]}");
                if (p.Count >= 4 && p[3] != string.Empty) AddLine($" description {p[3]}");
                AddLine(" switchport mode trunk"); AddLine($" switchport trunk allowed vlan {p[1]}");
                if (p[2] != string.Empty) AddLine($" switchport trunk native vlan {p[2]}");
                AddLine(" switchport nonegotiate");
                if (p.Count >= 5 && Like(p[4], "root")) AddLine(" spanning-tree guard root");
                if (p.Count >= 5 && Like(p[4], "loop")) AddLine(" spanning-tree guard loop");
                AddLine(" no shutdown");
            }
        }

        private void GenerateEtherChannelExtended()
        {
            if (!M("etherChannelExt")) return;
            Sec("ETHERCHANNEL ERWEITERT");
            if (V("etherLoadBalance") != string.Empty) AddLine($"port-channel load-balance {V("etherLoadBalance")}");
            foreach (var line in Lines(V("etherExtList")))
            {
                var p = Parts(line); if (p.Count < 5) continue;
                AddLine($"interface range {p[1]}"); AddLine($" channel-group {p[0]} mode {p[2]}"); AddLine(" no shutdown");
                AddLine($"interface port-channel {p[0]}");
                if (p.Count >= 6 && p[5] != string.Empty) AddLine($" description {p[5]}");
                if (p[3] == "trunk") { AddLine(" switchport mode trunk"); AddLine($" switchport trunk allowed vlan {p[4]}"); if (p.Count >= 7 && p[6] != string.Empty) AddLine($" switchport trunk native vlan {p[6]}"); }
                else { AddLine(" switchport mode access"); AddLine($" switchport access vlan {p[4]}"); }
            }
        }

        private void GenerateStpExtended()
        {
            if (!M("stpExtended")) return;

            Sec("STP ERWEITERT");

            var mode = V("stpGlobalMode").ToLowerInvariant();
            var strict = V("stpStrictCompatibility").Equals("Ja", StringComparison.OrdinalIgnoreCase);
            var platform = V("stpPlatformProfile");
            var rapidFeatures = mode is "rapid-pvst" or "mst";
            var vlanBasedMode = mode is "pvst" or "rapid-pvst";

            if (!string.IsNullOrWhiteSpace(platform) && !platform.Equals("Automatisch", StringComparison.OrdinalIgnoreCase))
                AddLine($"! Plattformprofil: {platform}{(V("stpSwitchModel") != string.Empty ? " / " + V("stpSwitchModel") : string.Empty)}{(V("stpIosVersion") != string.Empty ? " / " + V("stpIosVersion") : string.Empty)}");

            if (mode != string.Empty) AddLine($"spanning-tree mode {mode}");
            if (V("stpExtendSystemId") == "Ja") AddLine("spanning-tree extend system-id");
            if (V("stpPathcostMethod") != string.Empty) AddLine($"spanning-tree pathcost method {V("stpPathcostMethod")}");
            if (V("stpPortfastDefault") == "Ja") AddLine("spanning-tree portfast default");
            if (V("stpBpduguardDefault") == "Ja") AddLine("spanning-tree portfast bpduguard default");
            if (V("stpBpdufilterDefault") == "Ja") AddLine("spanning-tree portfast bpdufilter default");
            if (V("stpLoopguardDefault") == "Ja") AddLine("spanning-tree loopguard default");
            if (V("stpEtherchannelGuard") == "Ja") AddLine("spanning-tree etherchannel guard misconfig");

            if (mode == "pvst")
            {
                if (V("stpUplinkFast") == "Ja") AddLine("spanning-tree uplinkfast");
                if (V("stpBackboneFast") == "Ja") AddLine("spanning-tree backbonefast");
            }

            if (vlanBasedMode)
            {
                var target = V("stpVlanTarget");
                var role = V("stpVlanRootRole");
                var priority = V("stpVlanPriorityValue");
                if (target != string.Empty)
                {
                    if (role.Equals("primary", StringComparison.OrdinalIgnoreCase) || role.Equals("secondary", StringComparison.OrdinalIgnoreCase))
                        AddLine($"spanning-tree vlan {target} root {role.ToLowerInvariant()}");
                    else if ((role.StartsWith("Manuell", StringComparison.OrdinalIgnoreCase) || role == string.Empty) && priority != string.Empty)
                        AddLine($"spanning-tree vlan {target} priority {priority}");
                }

                foreach (var line in Lines(V("stpRootList")))
                {
                    var p = Parts(line);
                    if (p.Count >= 2 && p[0] != string.Empty &&
                        (p[1].Equals("primary", StringComparison.OrdinalIgnoreCase) || p[1].Equals("secondary", StringComparison.OrdinalIgnoreCase)))
                        AddLine($"spanning-tree vlan {p[0]} root {p[1].ToLowerInvariant()}");
                }

                foreach (var line in Lines(V("stpVlanPriorityList")))
                {
                    var p = Parts(line);
                    if (p.Count >= 2 && p[0] != string.Empty && p[1] != string.Empty)
                        AddLine($"spanning-tree vlan {p[0]} priority {p[1]}");
                }

                foreach (var line in Lines(V("stpVlanTimersList")))
                {
                    var p = Parts(line);
                    if (p.Count < 1 || p[0] == string.Empty) continue;
                    if (p.Count >= 2 && p[1] != string.Empty) AddLine($"spanning-tree vlan {p[0]} hello-time {p[1]}");
                    if (p.Count >= 3 && p[2] != string.Empty) AddLine($"spanning-tree vlan {p[0]} forward-time {p[2]}");
                    if (p.Count >= 4 && p[3] != string.Empty) AddLine($"spanning-tree vlan {p[0]} max-age {p[3]}");
                }

                foreach (var line in Lines(V("stpVlanInterfaceList")))
                {
                    var p = Parts(line);
                    if (p.Count < 2 || p[0] == string.Empty || p[1] == string.Empty) continue;
                    AddLine($"interface {p[0]}");
                    if (p.Count >= 3 && p[2] != string.Empty) AddLine($" spanning-tree vlan {p[1]} port-priority {p[2]}");
                    if (p.Count >= 4 && p[3] != string.Empty) AddLine($" spanning-tree vlan {p[1]} cost {p[3]}");
                }
            }
            else if (!strict && (!string.IsNullOrWhiteSpace(V("stpRootList")) || !string.IsNullOrWhiteSpace(V("stpVlanPriorityList"))))
            {
                AddLine("! VLAN-bezogene STP-Werte wurden im MST-Modus bewusst nicht exportiert.");
            }

            foreach (var line in Lines(V("stpInterfaceProfileList")))
            {
                var p = Parts(line);
                if (p.Count < 2 || p[0] == string.Empty || p[1] == string.Empty) continue;
                var profile = p[1].Trim().ToLowerInvariant();
                AddLine($"interface {p[0]}");

                if (profile is "client" or "client-port" or "server" or "server-port")
                {
                    AddLine(" spanning-tree portfast");
                    AddLine(" spanning-tree bpduguard enable");
                }
                else if (profile is "access-point" or "ap")
                {
                    AddLine(" spanning-tree portfast trunk");
                    AddLine(" spanning-tree bpduguard enable");
                }
                else if (profile is "switch-uplink" or "switch uplink")
                {
                    if (rapidFeatures) AddLine(" spanning-tree link-type point-to-point");
                    AddLine(" spanning-tree guard loop");
                }
                else if (profile is "port-channel" or "portchannel")
                {
                    if (rapidFeatures) AddLine(" spanning-tree link-type point-to-point");
                    AddLine(" spanning-tree guard loop");
                }
                else if (profile is "provider-uplink" or "provider uplink")
                {
                    AddLine(" spanning-tree guard root");
                }
            }

            foreach (var ifc in Lines(V("stpRootGuardIfs")))
            {
                AddLine($"interface {ifc}");
                AddLine(" spanning-tree guard root");
            }

            foreach (var ifc in Lines(V("stpLoopGuardIfs")))
            {
                AddLine($"interface {ifc}");
                AddLine(" spanning-tree guard loop");
            }

            foreach (var line in Lines(V("stpInterfaceList")))
            {
                var p = Parts(line);
                if (p.Count < 1 || p[0] == string.Empty) continue;
                AddLine($"interface {p[0]}");
                if (p.Count >= 2 && p[1].Equals("Trunk", StringComparison.OrdinalIgnoreCase)) AddLine(" spanning-tree portfast trunk");
                else if (p.Count >= 2 && YesLike(p[1])) AddLine(" spanning-tree portfast");
                if (p.Count >= 3 && YesLike(p[2])) AddLine(" spanning-tree bpduguard enable");
                if (p.Count >= 4 && YesLike(p[3])) AddLine(" spanning-tree bpdufilter enable");
                if (p.Count >= 5 && (p[4].Equals("root", StringComparison.OrdinalIgnoreCase) || p[4].Equals("loop", StringComparison.OrdinalIgnoreCase)))
                    AddLine($" spanning-tree guard {p[4].ToLowerInvariant()}");
                if (rapidFeatures && p.Count >= 6 && p[5] != string.Empty) AddLine($" spanning-tree link-type {p[5]}");
                if (p.Count >= 7 && p[6] != string.Empty) AddLine($" spanning-tree port-priority {p[6]}");
                if (p.Count >= 8 && p[7] != string.Empty) AddLine($" spanning-tree cost {p[7]}");
            }

            if (mode == "mst")
            {
                var mstLines = Lines(V("stpMstInstanceList")).Select(Parts).Where(p => p.Count >= 1 && p[0] != string.Empty).ToList();
                var hasMstRegion = V("stpMstName") != string.Empty || V("stpMstRevision") != string.Empty || mstLines.Any(p => p.Count >= 2 && p[1] != string.Empty);
                if (hasMstRegion)
                {
                    AddLine("spanning-tree mst configuration");
                    if (V("stpMstName") != string.Empty) AddLine($" name {V("stpMstName")}");
                    if (V("stpMstRevision") != string.Empty) AddLine($" revision {V("stpMstRevision")}");
                    foreach (var p in mstLines)
                        if (p.Count >= 2 && p[1] != string.Empty) AddLine($" instance {p[0]} vlan {p[1]}");
                }

                if (V("stpMstMaxHops") != string.Empty) AddLine($"spanning-tree mst max-hops {V("stpMstMaxHops")}");

                var targetInstance = V("stpMstTargetInstance");
                var mstRole = V("stpMstRootRole");
                var mstPriority = V("stpMstPriorityValue");
                if (targetInstance != string.Empty)
                {
                    if (mstRole.Equals("primary", StringComparison.OrdinalIgnoreCase) || mstRole.Equals("secondary", StringComparison.OrdinalIgnoreCase))
                        AddLine($"spanning-tree mst {targetInstance} root {mstRole.ToLowerInvariant()}");
                    else if ((mstRole.StartsWith("Manuell", StringComparison.OrdinalIgnoreCase) || mstRole == string.Empty) && mstPriority != string.Empty)
                        AddLine($"spanning-tree mst {targetInstance} priority {mstPriority}");
                }

                foreach (var p in mstLines)
                {
                    if (p.Count >= 3 && p[2] != string.Empty) AddLine($"spanning-tree mst {p[0]} priority {p[2]}");
                    if (p.Count >= 4 && (p[3].Equals("primary", StringComparison.OrdinalIgnoreCase) || p[3].Equals("secondary", StringComparison.OrdinalIgnoreCase)))
                        AddLine($"spanning-tree mst {p[0]} root {p[3].ToLowerInvariant()}");
                }

                foreach (var line in Lines(V("stpMstInterfaceList")))
                {
                    var p = Parts(line);
                    if (p.Count < 2 || p[0] == string.Empty || p[1] == string.Empty) continue;
                    AddLine($"interface {p[0]}");
                    if (p.Count >= 3 && p[2] != string.Empty) AddLine($" spanning-tree mst {p[1]} port-priority {p[2]}");
                    if (p.Count >= 4 && p[3] != string.Empty) AddLine($" spanning-tree mst {p[1]} cost {p[3]}");
                }
            }

            if (V("stpErrdisableBpduguard") == "Ja")
            {
                AddLine("errdisable recovery cause bpduguard");
                if (V("stpErrdisableInterval") != string.Empty)
                    AddLine($"errdisable recovery interval {V("stpErrdisableInterval")}");
            }

            foreach (var line in Lines(V("stpResetList")))
            {
                var p = Parts(line);
                if (p.Count < 3) continue;
                var scope = p[0].ToLowerInvariant();
                var targetReset = p[1];
                var parameter = p[2].ToLowerInvariant();

                if (scope == "vlan" && targetReset != string.Empty && (parameter is "priority" or "root" or "hello-time" or "forward-time" or "max-age"))
                {
                    var effective = parameter == "root" ? "priority" : parameter;
                    AddLine($"no spanning-tree vlan {targetReset} {effective}");
                }
                else if (scope == "mst" && targetReset != string.Empty && (parameter is "priority" or "root"))
                {
                    AddLine($"no spanning-tree mst {targetReset} priority");
                }
                else if (scope == "global")
                {
                    var command = parameter switch
                    {
                        "portfast-default" => "no spanning-tree portfast default",
                        "bpduguard-default" => "no spanning-tree portfast bpduguard default",
                        "bpdufilter-default" => "no spanning-tree portfast bpdufilter default",
                        "loopguard-default" => "no spanning-tree loopguard default",
                        "etherchannel-guard" => "no spanning-tree etherchannel guard misconfig",
                        "uplinkfast" => "no spanning-tree uplinkfast",
                        "backbonefast" => "no spanning-tree backbonefast",
                        "max-hops" => "no spanning-tree mst max-hops",
                        _ => string.Empty
                    };
                    if (command != string.Empty) AddLine(command);
                }
                else if (scope == "interface" && targetReset != string.Empty)
                {
                    var command = parameter switch
                    {
                        "portfast" => "no spanning-tree portfast",
                        "bpduguard" => "no spanning-tree bpduguard enable",
                        "bpdufilter" => "no spanning-tree bpdufilter enable",
                        "guard" => "no spanning-tree guard",
                        "link-type" => "no spanning-tree link-type",
                        "port-priority" => "no spanning-tree port-priority",
                        "cost" => "no spanning-tree cost",
                        _ => string.Empty
                    };
                    if (command != string.Empty)
                    {
                        AddLine($"interface {targetReset}");
                        AddLine(" " + command);
                    }
                }
            }
        }

        private void GenerateSwitchSecurityExtended()
        {
            if (!M("switchSecExt")) return;
            Sec("DHCP SNOOPING DAI IP SOURCE GUARD ERWEITERT");
            if (V("sseDhcpVlans") != string.Empty) { AddLine("ip dhcp snooping"); AddLine($"ip dhcp snooping vlan {V("sseDhcpVlans")}"); }
            if (V("sseDaiVlans") != string.Empty) AddLine($"ip arp inspection vlan {V("sseDaiVlans")}");
            foreach (var ifc in Lines(V("sseTrustedIfs"))) { AddLine($"interface {ifc}"); AddLine(" ip dhcp snooping trust"); AddLine(" ip arp inspection trust"); }
            foreach (var line in Lines(V("sseRateLimitIfs"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"interface {p[0]}"); AddLine($" ip dhcp snooping limit rate {p[1]}"); } }
            foreach (var ifc in Lines(V("sseSourceGuardIfs"))) { AddLine($"interface {ifc}"); AddLine(" ip verify source"); }
        }

        private void GeneratePortSecurityExtended()
        {
            if (!M("portSecurityExt")) return;
            Sec("PORT-SECURITY ERWEITERT");
            foreach (var line in Lines(V("portSecExtList")))
            {
                var p = Parts(line); if (p.Count < 1) continue;
                AddLine($"interface {p[0]}"); AddLine(" switchport port-security");
                if (p.Count >= 2 && p[1] != string.Empty) AddLine($" switchport port-security maximum {p[1]}");
                if (p.Count >= 3 && YesLike(p[2])) AddLine(" switchport port-security mac-address sticky");
                if (p.Count >= 4 && p[3] != string.Empty) AddLine($" switchport port-security violation {p[3]}");
                if (p.Count >= 5 && p[4] != string.Empty) AddLine($" switchport port-security aging time {p[4]}");
                if (p.Count >= 6 && p[5] != string.Empty) AddLine($" switchport port-security aging type {p[5]}");
            }
        }

        private void GenerateRouteMapFilter()
        {
            if (!M("routeMapFilter")) return;
            Sec("ROUTE-MAPS PREFIX-LISTS DISTRIBUTE-LISTS");
            foreach (var line in Lines(V("prefixListAdv"))) { var p = Parts(line); if (p.Count >= 4) AddLine($"ip prefix-list {p[0]} seq {p[1]} {p[2]} {p[3]}"); }
            foreach (var line in Lines(V("routeMapList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"route-map {p[0]} {p[1]} {p[2]}"); if (p.Count >= 4 && p[3] != string.Empty) AddLine($" match ip address prefix-list {p[3]}"); if (p.Count >= 5 && p[4] != string.Empty) AddLine($" set {p[4]}"); } }
            foreach (var line in Lines(V("distributeList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"router {p[0]}"); AddLine($" distribute-list {p[1]} {p[2]}"); } }
        }

        private void GenerateIpSlaTracking()
        {
            if (!M("ipSlaTracking")) return;
            Sec("IP SLA TRACKING");
            foreach (var line in Lines(V("ipSlaList"))) { var p = Parts(line); if (p.Count >= 5) { AddLine($"ip sla {p[0]}"); AddLine($" icmp-echo {p[1]} source-interface {p[2]}"); AddLine($" frequency {p[3]}"); AddLine($"ip sla schedule {p[0]} life forever start-time now"); AddLine($"track {p[4]} ip sla {p[0]} reachability"); } }
            foreach (var line in Lines(V("trackedRouteList"))) { var p = Parts(line); if (p.Count >= 5) { AddLine($"ip route {p[0]} {p[1]} {p[2]} track {p[3]}"); AddLine($"ip route {p[0]} {p[1]} {p[4]} 250"); } }
        }

        private void GenerateIpv6Extended()
        {
            if (!M("ipv6Extended")) return;
            Sec("IPV6 ERWEITERT");
            foreach (var line in Lines(V("ipv6StaticRoutes"))) { var p = Parts(line); if (p.Count >= 2) AddLine($"ipv6 route {p[0]} {p[1]}"); }
            foreach (var line in Lines(V("ipv6AclList"))) { var p = Parts(line); if (p.Count >= 5) { AddLine($"ipv6 access-list {p[0]}"); AddLine($" {p[1]} {p[2]} {p[3]} {p[4]}"); } }
            foreach (var line in Lines(V("ipv6AclApplyList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"interface {p[0]}"); AddLine($" ipv6 traffic-filter {p[1]} {p[2]}"); } }
            foreach (var ifc in Lines(V("ipv6RaSuppressIfs"))) { AddLine($"interface {ifc}"); AddLine(" ipv6 nd ra suppress all"); }
            foreach (var line in Lines(V("dhcpv6RelayList"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"interface {p[0]}"); AddLine($" ipv6 dhcp relay destination {p[1]}"); } }
        }

        private void GenerateOspfv3()
        {
            if (!M("ospfv3")) return;
            Sec("OSPFV3");
            var proc = string.IsNullOrWhiteSpace(V("ospfv3Process")) ? "1" : V("ospfv3Process");
            AddLine($"router ospfv3 {proc}");
            if (V("ospfv3Rid") != string.Empty) AddLine($" router-id {V("ospfv3Rid")}");
            AddLine(" address-family ipv6 unicast");
            foreach (var line in Lines(V("ospfv3IfList"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"interface {p[0]}"); AddLine($" ospfv3 {proc} ipv6 area {p[1]}"); } }
        }

        private void GenerateWanFailover()
        {
            if (!M("wanFailover")) return;
            Sec("WAN FAILOVER");
            if (V("wanPrimaryIf") != string.Empty) { AddLine($"interface {V("wanPrimaryIf")}"); AddLine(" ip nat outside"); AddLine(" no shutdown"); }
            if (V("wanBackupIf") != string.Empty) { AddLine($"interface {V("wanBackupIf")}"); AddLine(" ip nat outside"); AddLine(" no shutdown"); }
            if (V("wanTrackTarget") != string.Empty) { AddLine("ip sla 100"); AddLine($" icmp-echo {V("wanTrackTarget")} source-interface {V("wanPrimaryIf")}"); AddLine(" frequency 10"); AddLine("ip sla schedule 100 life forever start-time now"); AddLine("track 100 ip sla 100 reachability"); }
            if (V("wanPrimaryNh") != string.Empty) AddLine($"ip route 0.0.0.0 0.0.0.0 {V("wanPrimaryNh")} track 100");
            if (V("wanBackupNh") != string.Empty) AddLine($"ip route 0.0.0.0 0.0.0.0 {V("wanBackupNh")} 250");
        }

        private void GenerateZoneFirewall()
        {
            if (!M("zoneFirewall")) return;
            Sec("ZONE BASED FIREWALL");
            foreach (var z in Lines(V("zoneList"))) AddLine($"zone security {z}");
            foreach (var line in Lines(V("zoneMemberList"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"interface {p[0]}"); AddLine($" zone-member security {p[1]}"); } }
            foreach (var line in Lines(V("zonePolicyList"))) { var p = Parts(line); if (p.Count >= 4) { AddLine($"class-map type inspect match-any {p[0]}"); AddLine($" match protocol {p[1]}"); AddLine($"policy-map type inspect {p[2]}"); AddLine($" class type inspect {p[0]}"); AddLine($"  {p[3]}"); } }
            foreach (var line in Lines(V("zonePairList"))) { var p = Parts(line); if (p.Count >= 4) { AddLine($"zone-pair security {p[0]} source {p[1]} destination {p[2]}"); AddLine($" service-policy type inspect {p[3]}"); } }
        }

        private void GenerateDmzAssistant()
        {
            if (!M("dmzAssistant")) return;
            Sec("DMZ ASSISTENT");
            foreach (var line in Lines(V("dmzList")))
            {
                var p = Parts(line); if (p.Count < 4) continue;
                AddLine($"interface {p[0]}"); AddLine(" description DMZ"); AddLine($" ip address {p[1]} {p[2]}"); AddLine(" ip nat inside"); AddLine(" no shutdown");
                AddLine("ip access-list extended DMZ-IN"); AddLine($" permit tcp any host {p[3]} eq 443"); AddLine(" deny ip any any log");
            }
        }

        private void GenerateVpnAdvanced()
        {
            if (!M("vpnAdvanced")) return;
            Sec("VPN ERWEITERT SITE-TO-SITE");
            foreach (var line in Lines(V("ipsecS2SList")))
            {
                var p = Parts(line); if (p.Count < 6) continue;
                AddLine("crypto isakmp policy 20"); AddLine(" encr aes 256"); AddLine(" hash sha256"); AddLine(" authentication pre-share"); AddLine(" group 14");
                AddLine($"crypto isakmp key {p[1]} address {p[0]}"); AddLine($"crypto ipsec transform-set {p[4]} esp-aes 256 esp-sha-hmac");
                AddLine($"crypto map {p[5]} 10 ipsec-isakmp"); AddLine($" set peer {p[0]}"); AddLine($" set transform-set {p[4]}"); AddLine($" match address {p[3]}");
                AddLine($"interface {p[2]}"); AddLine($" crypto map {p[5]}");
            }
        }

        private void GenerateUserRights()
        {
            if (!M("userRights")) return;
            Sec("BENUTZER RECHTE ERWEITERT");
            foreach (var line in Lines(V("privUserList"))) { var p = Parts(line); if (p.Count >= 3) AddLine($"username {p[0]} privilege {p[1]} secret {p[2]}"); }
            foreach (var line in Lines(V("parserViewList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"parser view {p[0]}"); AddLine($" secret {p[1]}"); AddLine($" commands exec include {p[2]}"); } }
        }

        private void GeneratePacketTracerNote()
        {
            if (V("profile") != "Packet Tracer") return;
            Sec("PACKET TRACER HINWEIS");
            AddLine("! Packet Tracer unterstützt nicht alle IOS/IOS-XE-Befehle. Prüfe SNMPv3, IPsec, QoS, Zone Firewall, AAA/TACACS und einige Switch-Security-Befehle.");
        }

        private void GenerateVrfDefinitions()
        {
            if (!M("vrfDefs")) return;
            Sec("VRF DEFINITIONEN");
            foreach (var line in Lines(V("vrfDefList"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"ip vrf {p[0]}"); AddLine($" rd {p[1]}"); if (p.Count >= 3 && p[2] != string.Empty) AddLine($" description {p[2]}"); } }
        }

        private void GenerateVrfSvi()
        {
            if (!M("vrfSvi")) return;
            Sec("VLAN / SVI VRF");
            foreach (var line in Lines(V("vrfSviList")))
            {
                var p = Parts(line); if (p.Count < 4) continue;
                AddLine($"vlan {p[0]}"); if (p.Count >= 6 && p[5] != string.Empty) AddLine($" name {p[5]}");
                AddLine($"interface vlan {p[0]}"); AddLine($" ip vrf forwarding {p[1]}"); AddLine($" ip address {p[2]} {p[3]}");
                if (p.Count >= 7 && p[6] != string.Empty) AddLine($" ipv6 address {p[6]}");
                if (p.Count >= 8 && YesLike(p[7])) AddLine(" ipv6 enable");
                AddLine(" no shutdown");
            }
        }

        private void GenerateVrfStaticRoutes()
        {
            if (!M("vrfStaticRoutes")) return;
            Sec("STATIC ROUTES VRF");
            foreach (var line in Lines(V("vrfStaticList"))) { var p = Parts(line); if (p.Count >= 4) AddLine($"ip route vrf {p[0]} {p[1]} {p[2]} {p[3]}"); }
            foreach (var line in Lines(V("vrfIpv6StaticList"))) { var p = Parts(line); if (p.Count >= 3) AddLine($"ipv6 route vrf {p[0]} {p[1]} {p[2]}"); }
        }

        private void GenerateVrfOspf()
        {
            if (!M("vrfOspf")) return;
            Sec("OSPFV2 VRF");
            foreach (var line in Lines(V("vrfOspfList")))
            {
                var p = Parts(line); if (p.Count < 5) continue;
                AddLine($"router ospf {p[1]} vrf {p[0]}");
                if (p.Count >= 6 && p[5] != string.Empty) AddLine($" router-id {p[5]}");
                AddLine($" network {p[2]} {p[3]} area {p[4]}");
            }
        }

        private void GenerateVrfOspfv3()
        {
            if (!M("vrfOspfv3")) return;
            Sec("OSPFV3 VRF");
            AddLine("ipv6 unicast-routing");
            foreach (var line in Lines(V("vrfOspfv3List"))) { var p = Parts(line); if (p.Count >= 2) { AddLine($"router ospfv3 {p[1]}"); AddLine($" address-family ipv6 vrf {p[0]}"); if (p.Count >= 4 && p[3] != string.Empty) AddLine($"  router-id {p[3]}"); AddLine(" exit-address-family"); } }
            foreach (var line in Lines(V("vrfOspfv3IfList"))) { var p = Parts(line); if (p.Count >= 4) { AddLine($"interface {p[0]}"); AddLine($" ospfv3 {p[2]} ipv6 area {p[3]}"); } }
        }

        private void GenerateVrfBgp()
        {
            if (!M("vrfBgp")) return;
            Sec("BGP VRF");
            foreach (var line in Lines(V("vrfBgpIpv4List"))) { var p = Parts(line); if (p.Count >= 6) { AddLine($"router bgp {p[1]}"); AddLine($" address-family ipv4 vrf {p[0]}"); AddLine($"  neighbor {p[2]} remote-as {p[3]}"); AddLine($"  neighbor {p[2]} activate"); AddLine($"  network {p[4]} mask {p[5]}"); AddLine(" exit-address-family"); } }
            foreach (var line in Lines(V("vrfBgpIpv6List"))) { var p = Parts(line); if (p.Count >= 5) { AddLine($"router bgp {p[1]}"); AddLine($" address-family ipv6 vrf {p[0]}"); AddLine($"  neighbor {p[2]} remote-as {p[3]}"); AddLine($"  neighbor {p[2]} activate"); AddLine($"  network {p[4]}"); AddLine(" exit-address-family"); } }
        }

        private void GenerateIpv6RoutingProtocols()
        {
            if (!M("ipv6RoutingProtocols")) return;
            Sec("IPV6 ROUTINGPROTOKOLLE");
            AddLine("ipv6 unicast-routing");
            foreach (var line in Lines(V("ipv6BgpList")))
            {
                var p = Parts(line); if (p.Count < 4) continue;
                AddLine($"router bgp {p[0]}"); AddLine(" address-family ipv6 unicast"); AddLine($"  neighbor {p[1]} remote-as {p[2]}"); AddLine($"  neighbor {p[1]} activate"); AddLine($"  network {p[3]}");
                if (p.Count >= 6 && p[5] != string.Empty) AddLine($"  neighbor {p[1]} update-source {p[5]}");
                if (p.Count >= 7 && p[6] != string.Empty) AddLine($"  neighbor {p[1]} route-map {p[6]} out");
                AddLine(" exit-address-family");
            }
            foreach (var line in Lines(V("ipv6EigrpList"))) { var p = Parts(line); if (p.Count >= 3) { AddLine($"ipv6 router eigrp {p[0]}"); AddLine($" eigrp router-id {p[1]}"); AddLine(" no shutdown"); AddLine($"interface {p[2]}"); AddLine($" ipv6 eigrp {p[0]}"); } }
            foreach (var line in Lines(V("ipv6IsisList"))) { var p = Parts(line); if (p.Count >= 2) { var tag = p[0]; AddLine($"router isis {tag}"); AddLine(" address-family ipv6"); AddLine(" multi-topology"); AddLine($"interface {p[1]}"); AddLine($" ipv6 router isis {tag}"); } }
        }

        private void GenerateInterfaceRoles()
        {
            if (!M("interfaceRoles")) return;
            Sec("INTERFACE ROLLENMODELL");
            foreach (var line in Lines(V("interfaceRoleList")))
            {
                var f = Parts(line); if (f.Count < 2) continue;
                var ifc = f[0]; var role = f[1].ToLowerInvariant();
                var desc = f.Count >= 3 ? f[2] : ""; var access = f.Count >= 4 ? f[3] : ""; var voice = f.Count >= 5 ? f[4] : ""; var allowed = f.Count >= 6 ? f[5] : ""; var native = f.Count >= 7 ? f[6] : ""; var pc = f.Count >= 8 ? f[7] : ""; var ip = f.Count >= 9 ? f[8] : ""; var mask = f.Count >= 10 ? f[9] : ""; var vrf = f.Count >= 11 ? f[10] : "";
                AddLine($"interface {ifc}");
                if (desc != string.Empty) AddLine($" description {desc}");
                if (role == "access") { AddLine(" switchport mode access"); if (access != string.Empty) AddLine($" switchport access vlan {access}"); AddLine(" spanning-tree portfast"); AddLine(" spanning-tree bpduguard enable"); AddLine(" no shutdown"); }
                else if (role == "voice") { AddLine(" switchport mode access"); if (access != string.Empty) AddLine($" switchport access vlan {access}"); if (voice != string.Empty) AddLine($" switchport voice vlan {voice}"); AddLine(" spanning-tree portfast"); AddLine(" spanning-tree bpduguard enable"); AddLine(" no shutdown"); }
                else if (role == "trunk") { AddLine(" switchport mode trunk"); if (allowed != string.Empty) AddLine($" switchport trunk allowed vlan {allowed}"); if (native != string.Empty) AddLine($" switchport trunk native vlan {native}"); AddLine(" switchport nonegotiate"); AddLine(" no shutdown"); }
                else if (role == "uplink") { AddLine(" switchport mode trunk"); if (allowed != string.Empty) AddLine($" switchport trunk allowed vlan {allowed}"); if (native != string.Empty) AddLine($" switchport trunk native vlan {native}"); AddLine(" switchport nonegotiate"); AddLine(" spanning-tree guard root"); AddLine(" no shutdown"); }
                else if (role == "ap") { AddLine(" switchport mode trunk"); if (allowed != string.Empty) AddLine($" switchport trunk allowed vlan {allowed}"); if (native != string.Empty) AddLine($" switchport trunk native vlan {native}"); AddLine(" switchport nonegotiate"); AddLine(" spanning-tree portfast trunk"); AddLine(" no shutdown"); }
                else if (role == "server") { AddLine(" switchport mode access"); if (access != string.Empty) AddLine($" switchport access vlan {access}"); AddLine(" spanning-tree portfast"); AddLine(" no shutdown"); }
                else if (role == "pc-member") { if (pc != string.Empty) AddLine($" channel-group {pc} mode active"); AddLine(" no shutdown"); }
                else if (role == "routed") { AddLine(" no switchport"); if (vrf != string.Empty) AddLine($" ip vrf forwarding {vrf}"); if (ip != string.Empty && mask != string.Empty) AddLine($" ip address {ip} {mask}"); AddLine(" no shutdown"); }
                else if (role == "unused") { AddLine(" switchport mode access"); if (access != string.Empty) AddLine($" switchport access vlan {access}"); AddLine(" shutdown"); }
                else if (role == "mgmt") { AddLine(" switchport mode access"); if (access != string.Empty) AddLine($" switchport access vlan {access}"); AddLine(" spanning-tree portfast"); AddLine(" no shutdown"); }
            }
        }

        private void GenerateVlanIpPlan()
        {
            if (!M("vlanIpPlan")) return;
            Sec("VLAN IP PLAN");
            foreach (var line in Lines(V("vlanIpPlanList")))
            {
                var f = Parts(line); if (f.Count < 5) continue;
                var vlan = f[0]; var name = f[1]; var net = f[2]; var mask = f[3]; var gw = f[4];
                var ipv6 = f.Count >= 6 ? f[5] : ""; var vrf = f.Count >= 7 ? f[6] : ""; var dhcp = f.Count >= 8 ? f[7] : ""; var dns = f.Count >= 9 ? f[8] : ""; var helper = f.Count >= 10 ? f[9] : ""; var hsrp = f.Count >= 11 ? f[10] : ""; var ospfArea = f.Count >= 12 ? f[11] : ""; var bgp = f.Count >= 13 ? f[12] : "";
                AddLine($"vlan {vlan}"); AddLine($" name {name}"); AddLine($"interface vlan {vlan}");
                if (vrf != string.Empty) AddLine($" ip vrf forwarding {vrf}");
                AddLine($" ip address {gw} {mask}");
                if (ipv6 != string.Empty) AddLine($" ipv6 address {ipv6}");
                if (helper != string.Empty) AddLine($" ip helper-address {helper}");
                if (hsrp != string.Empty) { AddLine($" standby {vlan} ip {hsrp}"); AddLine($" standby {vlan} priority 110"); AddLine($" standby {vlan} preempt"); }
                AddLine(" no shutdown");
                if (YesLike(dhcp)) { AddLine($"ip dhcp pool VLAN{vlan}-{name}"); AddLine($" network {net} {mask}"); AddLine($" default-router {gw}"); if (dns != string.Empty) AddLine($" dns-server {dns}"); }
                if (ospfArea != string.Empty) AddLine($"! Hinweis VLAN {vlan} OSPF: Netz {net} {mask} in passendem OSPF-Modul mit Area {ospfArea} pflegen.");
                if (YesLike(bgp)) AddLine($"! Hinweis VLAN {vlan} BGP: Prefix {net} {mask} im passenden BGP-Modul als network pflegen.");
            }
        }

        private void GenerateAclAssistant()
        {
            if (!M("aclAssistant")) return;
            Sec("ACL ASSISTENT");
            var aclName = V("aclAssistName"); var action = V("aclAssistAction"); var proto = V("aclAssistProtocol"); var srcType = V("aclAssistSrcType"); var srcVal = V("aclAssistSrc"); var dstType = V("aclAssistDstType"); var dstVal = V("aclAssistDst"); var port = V("aclAssistPort"); var ifc = V("aclAssistInterface"); var dir = V("aclAssistDirection");
            if (aclName == string.Empty || action == string.Empty || proto == string.Empty) return;
            var srcExpr = "any";
            if (srcType == "host" && srcVal != string.Empty) srcExpr = $"host {srcVal}";
            else if (srcType == "netz" && srcVal != string.Empty) srcExpr = srcVal;
            var dstExpr = "any";
            if (dstType == "host" && dstVal != string.Empty) dstExpr = $"host {dstVal}";
            else if (dstType == "netz" && dstVal != string.Empty) dstExpr = dstVal;
            var portExpr = "";
            if ((proto == "tcp" || proto == "udp") && port != string.Empty && port != "kein Port")
            {
                if (Like(port, "^22")) portExpr = " eq 22";
                else if (Like(port, "^23")) portExpr = " eq 23";
                else if (Like(port, "^53")) portExpr = " eq 53";
                else if (Like(port, "^80")) portExpr = " eq 80";
                else if (Like(port, "^443")) portExpr = " eq 443";
                else if (Like(port, "^161")) portExpr = " eq 161";
                else if (Like(port, "^3389")) portExpr = " eq 3389";
            }
            AddLine($"ip access-list extended {aclName}");
            AddLine($" {action} {proto} {srcExpr} {dstExpr}{portExpr}");
            if (ifc != string.Empty) { AddLine($"interface {ifc}"); AddLine($" ip access-group {aclName} {dir}"); }
        }

        private void GenerateOspfAdvanced()
        {
            if (!M("ospfAdvanced")) return;
            Sec("OSPF OPTIONEN");
            var ospfProcess = string.IsNullOrWhiteSpace(V("ospfPid")) ? "1" : V("ospfPid");
            foreach (var line in Lines(V("ospfAreaTypeList")))
            {
                var f = Parts(line); if (f.Count < 2) continue;
                AddLine($"router ospf {ospfProcess}");
                if (f[1] == "stub") AddLine($" area {f[0]} stub");
                else if (f[1] == "totally-stubby") AddLine($" area {f[0]} stub no-summary");
                else if (f[1] == "nssa") AddLine($" area {f[0]} nssa");
                else if (f[1] == "totally-nssa") AddLine($" area {f[0]} nssa no-summary");
            }
            foreach (var line in Lines(V("ospfAuthList"))) { var f = Parts(line); if (f.Count >= 3) { AddLine($"interface {f[0]}"); if (f[1] == "md5") { AddLine(" ip ospf authentication message-digest"); AddLine($" ip ospf message-digest-key 1 md5 {f[2]}"); } else if (f[1] == "clear") { AddLine(" ip ospf authentication"); AddLine($" ip ospf authentication-key {f[2]}"); } } }
            foreach (var line in Lines(V("ospfTimerList"))) { var f = Parts(line); if (f.Count >= 3) { AddLine($"interface {f[0]}"); AddLine($" ip ospf hello-interval {f[1]}"); AddLine($" ip ospf dead-interval {f[2]}"); } }
            foreach (var line in Lines(V("ospfSummaryList"))) { var f = Parts(line); if (f.Count >= 3) { AddLine($"router ospf {ospfProcess}"); AddLine($" area {f[0]} range {f[1]} {f[2]}"); } }
        }

        private void GenerateBgpAdvanced()
        {
            if (!M("bgpAdvanced")) return;
            Sec("BGP OPTIONEN");
            var localAs = V("bgpAs");
            if (localAs == string.Empty) return;
            AddLine($"router bgp {localAs}");
            foreach (var line in Lines(V("bgpPolicyNeighborList")))
            {
                var f = Parts(line); if (f.Count < 1) continue; var n = f[0];
                if (f.Count >= 2 && f[1] != string.Empty) AddLine($" neighbor {n} route-map {f[1]} in");
                if (f.Count >= 3 && f[2] != string.Empty) AddLine($" neighbor {n} route-map {f[2]} out");
                if (f.Count >= 4 && f[3] != string.Empty) AddLine($" neighbor {n} prefix-list {f[3]} in");
                if (f.Count >= 5 && f[4] != string.Empty) AddLine($" neighbor {n} prefix-list {f[4]} out");
                if (f.Count >= 6 && f[5] != string.Empty) AddLine($" neighbor {n} timers {f[5]}");
            }
            foreach (var line in Lines(V("bgpAfIpv6List")))
            {
                var f = Parts(line); if (f.Count < 2) continue;
                AddLine("address-family ipv6 unicast"); AddLine($" neighbor {f[0]} remote-as {f[1]}"); AddLine($" neighbor {f[0]} activate");
                if (f.Count >= 3 && f[2] != string.Empty) AddLine($" network {f[2]}");
                if (f.Count >= 4 && f[3] != string.Empty) AddLine($" neighbor {f[0]} route-map {f[3]} in");
                if (f.Count >= 5 && f[4] != string.Empty) AddLine($" neighbor {f[0]} route-map {f[4]} out");
                AddLine("exit-address-family");
            }
        }



        private void GenerateMpls()
        {
            if (!M("mpls")) return;

            var deviceType = V("deviceType");
            if (deviceType.Equals("L2-Switch", StringComparison.OrdinalIgnoreCase))
            {
                Sec("MPLS / LDP / L3VPN");
                AddLine("! MPLS wird in diesem Tool nur für Router und L3-Switches erzeugt.");
                return;
            }

            Sec("MPLS / LDP / L3VPN");

            if (V("mplsCef") == "Ja") AddLine("ip cef");
            if (V("mplsGlobal") == "Ja") AddLine("mpls ip");
            if (V("mplsLabelProtocol").Equals("ldp", StringComparison.OrdinalIgnoreCase))
                AddLine("mpls label protocol ldp");

            var ldpRid = V("mplsLdpRouterId");
            if (!string.IsNullOrWhiteSpace(ldpRid))
            {
                var force = V("mplsLdpRouterIdForce") == "Ja" ? " force" : string.Empty;
                AddLine($"mpls ldp router-id {ldpRid}{force}");
            }

            foreach (var line in Lines(V("mplsLdpPasswordList")))
            {
                var f = Parts(line);
                if (f.Count < 2 || f[0] == string.Empty || f[1] == string.Empty) continue;
                AddLine($"mpls ldp neighbor {f[0]} password {f[1]}");
            }

            foreach (var line in Lines(V("mplsInterfaceList")))
            {
                var f = Parts(line);
                if (f.Count < 1 || f[0] == string.Empty) continue;

                var ifc = f[0];
                var desc = f.Count >= 2 ? f[1] : string.Empty;
                var mtu = f.Count >= 3 ? f[2] : string.Empty;
                var ldpSync = f.Count >= 4 ? f[3] : string.Empty;

                AddLine($"interface {ifc}");
                if (!string.IsNullOrWhiteSpace(desc)) AddLine($" description {desc}");
                AddLine(" mpls ip");

                if (!string.IsNullOrWhiteSpace(mtu))
                {
                    if (int.TryParse(mtu, out var mtuValue))
                    {
                        if (mtuValue == 9000)
                            AddLine(" ! Hinweis: 9000 ist ein typischer Host-Jumbo-Wert. MPLS-MTU nur setzen, wenn Plattform und Transportweg es tragen.");
                        else if (mtuValue < 1504)
                            AddLine(" ! WARNUNG: MPLS-MTU wirkt sehr niedrig. Prüfe Label-Overhead und Plattform.");
                        AddLine($" mpls mtu {mtuValue}");
                    }
                    else
                    {
                        AddLine($" ! Ungültige MPLS-MTU übersprungen: {mtu}");
                    }
                }

                if (YesLike(ldpSync)) AddLine(" mpls ldp igp sync");
                AddLine(" no shutdown");
            }

            foreach (var line in Lines(V("mplsVpnVrfList")))
            {
                var f = Parts(line);
                if (f.Count < 4 || f[0] == string.Empty || f[1] == string.Empty) continue;

                var vrf = f[0];
                var rd = f[1];
                var rtImport = f.Count >= 3 ? f[2] : string.Empty;
                var rtExport = f.Count >= 4 ? f[3] : string.Empty;
                var desc = f.Count >= 5 ? f[4] : string.Empty;

                AddLine($"vrf definition {vrf}");
                if (!string.IsNullOrWhiteSpace(desc)) AddLine($" description {desc}");
                AddLine($" rd {rd}");
                if (!string.IsNullOrWhiteSpace(rtImport)) AddLine($" route-target import {rtImport}");
                if (!string.IsNullOrWhiteSpace(rtExport)) AddLine($" route-target export {rtExport}");
                AddLine(" address-family ipv4");
                AddLine(" exit-address-family");
            }

            var bgpAs = V("mplsBgpAs");
            var vpnv4Peers = Lines(V("mplsVpnv4Peers")).ToList();
            if (!string.IsNullOrWhiteSpace(bgpAs) && vpnv4Peers.Count > 0)
            {
                AddLine($"router bgp {bgpAs}");
                AddLine(" bgp log-neighbor-changes");

                var updateSource = V("mplsVpnv4UpdateSource");
                foreach (var line in vpnv4Peers)
                {
                    var f = Parts(line);
                    if (f.Count < 2 || f[0] == string.Empty || f[1] == string.Empty) continue;
                    AddLine($" neighbor {f[0]} remote-as {f[1]}");
                    if (!string.IsNullOrWhiteSpace(updateSource)) AddLine($" neighbor {f[0]} update-source {updateSource}");
                    if (f.Count >= 3 && !string.IsNullOrWhiteSpace(f[2])) AddLine($" ! VPNv4 Peer {f[0]}: {f[2]}");
                }

                AddLine(" address-family vpnv4");
                foreach (var line in vpnv4Peers)
                {
                    var f = Parts(line);
                    if (f.Count < 2 || f[0] == string.Empty) continue;
                    AddLine($"  neighbor {f[0]} activate");
                    var sendCommunity = f.Count >= 4 ? f[3] : "Ja";
                    if (string.IsNullOrWhiteSpace(sendCommunity) || YesLike(sendCommunity))
                        AddLine($"  neighbor {f[0]} send-community extended");
                }
                AddLine(" exit-address-family");
            }
        }


        private void GenerateVoip()
        {
            if (!M("voip")) return;
            Sec("VOIP / VOICE VLAN");
            var dataVlan = V("voipDataVlan"); var dataName = V("voipDataVlanName"); var voiceVlan = V("voipVoiceVlan"); var voiceName = V("voipVoiceVlanName");
            if (dataVlan != string.Empty) { AddLine($"vlan {dataVlan}"); if (dataName != string.Empty) AddLine($" name {dataName}"); }
            if (voiceVlan != string.Empty) { AddLine($"vlan {voiceVlan}"); if (voiceName != string.Empty) AddLine($" name {voiceName}"); }
            if (V("voipCdp") == "Ja") AddLine("cdp run");
            if (V("voipLldp") == "Ja") { AddLine("lldp run"); AddLine("lldp med-tlv-select network-policy"); }
            var qosMode = V("voipQosMode");
            if (qosMode != string.Empty && qosMode != "Nein" && qosMode.StartsWith("mls qos", StringComparison.OrdinalIgnoreCase)) AddLine("mls qos");
            var pool = V("voipDhcpPool"); var net = V("voipNetwork"); var mask = V("voipMask"); var gw = V("voipGateway"); var dns = V("voipDns"); var tftp = V("voipTftp"); var excluded = V("voipExcluded");
            if (excluded != string.Empty) AddLine($"ip dhcp excluded-address {excluded}");
            if (pool != string.Empty && net != string.Empty && mask != string.Empty && gw != string.Empty) { AddLine($"ip dhcp pool {pool}"); AddLine($" network {net} {mask}"); AddLine($" default-router {gw}"); if (dns != string.Empty) AddLine($" dns-server {dns}"); if (tftp != string.Empty) AddLine($" option 150 ip {tftp}"); }
            foreach (var line in Lines(V("voipInterfaceList")))
            {
                var f = Parts(line); if (f.Count < 1 || f[0] == string.Empty) continue;
                var ifc = f[0]; var desc = f.Count >= 2 ? f[1] : ""; var access = f.Count >= 3 && f[2] != string.Empty ? f[2] : dataVlan; var voice = f.Count >= 4 && f[3] != string.Empty ? f[3] : voiceVlan; var pf = f.Count >= 5 && f[4] != string.Empty ? f[4] : "Ja"; var bpdu = f.Count >= 6 && f[5] != string.Empty ? f[5] : "Ja"; var qos = f.Count >= 7 && f[6] != string.Empty ? f[6] : qosMode; var ps = f.Count >= 8 && f[7] != string.Empty ? f[7] : "Nein";
                AddLine($"interface {ifc}"); if (desc != string.Empty) AddLine($" description {desc}"); AddLine(" switchport mode access");
                if (access != string.Empty) AddLine($" switchport access vlan {access}");
                if (voice != string.Empty) AddLine($" switchport voice vlan {voice}");
                if (qos != string.Empty && qos != "Nein")
                {
                    if (qos == "auto qos voip cisco-phone") AddLine(" auto qos voip cisco-phone");
                    else if (qos == "mls qos trust dscp") AddLine(" mls qos trust dscp");
                    else if (qos == "mls qos trust cos") AddLine(" mls qos trust cos");
                    else AddLine($" {qos}");
                }
                if (YesLike(pf)) AddLine(" spanning-tree portfast");
                if (YesLike(bpdu)) AddLine(" spanning-tree bpduguard enable");
                if (YesLike(ps)) { AddLine(" switchport port-security"); AddLine(" switchport port-security mac-address sticky"); AddLine(" switchport port-security violation restrict"); }
                AddLine(" no shutdown");
            }
        }

        private void GenerateQinQ()
        {
            if (!M("qinq")) return;

            var deviceType = V("deviceType");
            if (deviceType.Equals("Router", StringComparison.OrdinalIgnoreCase))
            {
                Sec("QINQ / 802.1AD");
                AddLine("! QinQ wird in diesem Tool nur für L2- und L3-Switches erzeugt.");
                AddLine("! Router-spezifische QinQ-Subinterfaces bitte über Subinterfaces/Routing-Module abbilden.");
                return;
            }

            Sec("QINQ / 802.1AD");

            var prefix = string.IsNullOrWhiteSpace(V("qinqVlanPrefix")) ? "QINQ_SVLAN_" : V("qinqVlanPrefix");
            var systemMtu = V("qinqSystemMtu");

            if (!string.IsNullOrWhiteSpace(systemMtu))
            {
                if (int.TryParse(systemMtu, out var mtuValue))
                {
                    AddLine("! MTU ist plattformabhängig. Für QinQ ist nicht pauschal 9000 nötig.");
                    if (mtuValue < 1504)
                        AddLine("! WARNUNG: MTU wirkt für QinQ sehr niedrig. Prüfe Plattform und Frame-Overhead.");
                    if (mtuValue == 9000)
                        AddLine("! Hinweis: 9000 ist ein typischer Host-Jumbo-Wert, nicht der QinQ-Standard. Nur setzen, wenn das gesamte Transportnetz Jumbo unterstützt.");
                    AddLine($"system mtu jumbo {mtuValue}");
                    AddLine("! Hinweis: Auf vielen Catalyst-Plattformen ist für system mtu jumbo ein Reload nötig.");
                }
                else
                {
                    AddLine($"! Ungültiger MTU-Wert übersprungen: {systemMtu}");
                }
            }

            if (V("qinqGlobalDot1q") == "Ja")
                AddLine("! Hinweis: Globales dot1q tunneling/Ethertype ist plattformabhängig. Prüfe IOS/IOS-XE Syntax.");

            var providerVlans = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in Lines(V("qinqTunnelList")))
            {
                var f = Parts(line);
                if (f.Count >= 2 && f[0] != string.Empty && f[1] != string.Empty)
                    providerVlans.Add(f[1]);
            }

            if (deviceType.Equals("L3-Switch", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in Lines(V("qinqSubifList")))
                {
                    var f = Parts(line);
                    if (f.Count >= 3 && f[2] != string.Empty)
                        providerVlans.Add(f[2]);
                }
            }

            foreach (var vlan in providerVlans)
            {
                AddLine($"vlan {vlan}");
                AddLine($" name {prefix}{vlan}");
            }

            foreach (var line in Lines(V("qinqTunnelList")))
            {
                var f = Parts(line);
                if (f.Count < 2) continue;

                var ifc = f[0];
                var svlan = f[1];
                var desc = f.Count >= 3 ? f[2] : string.Empty;
                var l2 = string.Empty;

                if (f.Count >= 4)
                {
                    if (int.TryParse(f[3], out var legacyInterfaceMtu))
                    {
                        AddLine($"! Hinweis: Interface-MTU {legacyInterfaceMtu} aus alter QinQ-Liste wird nicht als mtu-Befehl gesetzt. Nutze bei Bedarf System MTU optional.");
                        l2 = f.Count >= 5 ? f[4].ToLowerInvariant() : string.Empty;
                    }
                    else
                    {
                        l2 = f[3].ToLowerInvariant();
                    }
                }

                AddLine($"interface {ifc}");
                if (desc != string.Empty) AddLine($" description {desc}");
                AddLine(" switchport");
                AddLine(" switchport mode dot1q-tunnel");
                AddLine($" switchport access vlan {svlan}");

                if (l2 != string.Empty)
                {
                    if (Like(l2, "all"))
                    {
                        AddLine(" l2protocol-tunnel cdp");
                        AddLine(" l2protocol-tunnel stp");
                        AddLine(" l2protocol-tunnel vtp");
                    }
                    else
                    {
                        if (Like(l2, "cdp")) AddLine(" l2protocol-tunnel cdp");
                        if (Like(l2, "stp")) AddLine(" l2protocol-tunnel stp");
                        if (Like(l2, "vtp")) AddLine(" l2protocol-tunnel vtp");
                    }
                }

                AddLine(" no shutdown");
            }

            if (!deviceType.Equals("L3-Switch", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(V("qinqSubifList")))
                    AddLine("! L2-Switch: QinQ-L3-Subinterfaces werden bewusst übersprungen.");
                return;
            }

            foreach (var line in Lines(V("qinqSubifList")))
            {
                var f = Parts(line);
                if (f.Count < 4) continue;

                var parent = f[0];
                var sub = f[1];
                var svlan = f[2];
                var cvlan = f[3];
                var ip = f.Count >= 5 ? f[4] : string.Empty;
                var mask = f.Count >= 6 ? f[5] : string.Empty;
                var desc = f.Count >= 7 ? f[6] : string.Empty;

                AddLine($"interface {parent}.{sub}");
                if (desc != string.Empty) AddLine($" description {desc}");
                AddLine($" encapsulation dot1Q {svlan} second-dot1q {cvlan}");
                if (ip != string.Empty && mask != string.Empty) AddLine($" ip address {ip} {mask}");
                AddLine(" no shutdown");
            }
        }

        private static bool IsSubmodeStart(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var patterns = new[]
            {
                @"^interface\s+", @"^router\s+", @"^line\s+", @"^ip dhcp pool\s+", @"^ip vrf\s+",
                @"^ip access-list\s+", @"^ipv6 access-list\s+", @"^route-map\s+", @"^class-map\s+",
                @"^policy-map\s+", @"^zone-pair\s+", @"^zone security\s+", @"^parser view\s+",
                @"^crypto isakmp policy\s+", @"^crypto ipsec transform-set\s+", @"^crypto map\s+",
                @"^aaa group server\s+", @"^radius server\s+", @"^tacacs server\s+", @"^archive$",
                @"^spanning-tree mst configuration$"
            };
            return patterns.Any(p => Regex.IsMatch(line.Trim(), p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        private static bool IsGlobalCommandAllowedInsideSubmode(string line) => Regex.IsMatch(line ?? string.Empty, @"^\s+");

        private static string NormalizeCiscoPasteScript(string raw)
        {
            var outLines = new List<string> { "enable", "configure terminal" };
            var inSubmode = false;

            foreach (var rawLine in Regex.Split(raw ?? string.Empty, "\\r?\\n"))
            {
                var line = rawLine.TrimEnd();
                var trim = line.Trim();

                if (trim == string.Empty)
                {
                    outLines.Add(string.Empty);
                    continue;
                }

                if (trim.StartsWith("!", StringComparison.Ordinal))
                {
                    if (inSubmode) { outLines.Add(" exit"); inSubmode = false; }
                    outLines.Add(line);
                    continue;
                }

                if (trim is "enable" or "configure terminal" or "conf t") continue;

                if (trim is "end" or "write memory" or "wr mem")
                {
                    if (inSubmode) { outLines.Add(" exit"); inSubmode = false; }
                    outLines.Add(trim);
                    continue;
                }

                if (IsSubmodeStart(trim))
                {
                    if (inSubmode) { outLines.Add(" exit"); inSubmode = false; }
                    outLines.Add(trim);
                    inSubmode = true;
                    continue;
                }

                if (inSubmode && !IsGlobalCommandAllowedInsideSubmode(line))
                {
                    outLines.Add(" exit");
                    inSubmode = false;
                    outLines.Add(trim);
                    continue;
                }

                outLines.Add(line);
            }

            if (inSubmode) outLines.Add(" exit");
            return string.Join("\r\n", outLines);
        }
    }
}
