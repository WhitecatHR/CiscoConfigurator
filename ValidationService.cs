using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public static class ValidationService
{
    private static readonly string[] InterfaceFields =
    {
        "interfacesList", "rangesList", "subifList", "rosParentIf", "qosTrustInterfaces", "interfaceProfileList",
        "trunkUplinkList", "etherExtList", "interfaceRoleList", "sviList", "vrfSviList", "aclApplyList",
        "aclAssignExtList", "ipv6IfList", "ipv6AclApplyList", "ospfv3IfList", "natIfList", "greList",
        "greIpsecList", "zoneMemberList", "dmzList", "voipInterfaceList", "qinqTunnelList", "qinqSubifList", "mplsInterfaceList"
    };

    public static string BuildWarnings(IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, bool> modules)
    {
        var warnings = new List<string>();
        string V(string key) => values.TryGetValue(key, out var v) ? v.Trim() : string.Empty;
        bool M(string key) => modules.TryGetValue(key, out var m) && m;

        if (M("basic") && string.IsNullOrWhiteSpace(V("hostname")))
            warnings.Add("Grunddaten: Hostname ist leer.");

        if (M("ssh") && string.IsNullOrWhiteSpace(V("domain")))
            warnings.Add("SSH: Domain ist leer. RSA-Key-Erzeugung kann auf Cisco IOS fehlschlagen.");

        if (M("ssh") && V("vtyLoginMode") == "password login" && string.IsNullOrWhiteSpace(V("vtyPassword")))
            warnings.Add("SSH/VTY: password login gewählt, aber VTY-Passwort ist leer.");

        if (M("ssh") && V("vtyLoginMode") == "login local" && (!M("userRights") || string.IsNullOrWhiteSpace(V("privUserList"))))
            warnings.Add("SSH/VTY: login local benötigt mindestens einen lokalen Benutzer mit Secret.");

        if (M("ssh") && V("vtyLoginMode") == "login authentication default" && !M("aaa") && !M("radiusTacacs"))
            warnings.Add("SSH/VTY: login authentication default benötigt eine passende AAA-Authentifizierungsmethode.");

        if (M("radiusTacacs") && IsYes(V("aaaUseRadius")) && string.IsNullOrWhiteSpace(V("radiusList")))
            warnings.Add("RADIUS/TACACS: AAA Radius nutzen ist Ja, aber RADIUS-Liste ist leer.");

        if (V("configMode") == "Mit VRF" && (!M("vrfDefs") || string.IsNullOrWhiteSpace(V("vrfDefList"))))
            warnings.Add("VRF-Modus: VRF Definitionen sollten gepflegt werden, bevor VRF-SVI/Routing erzeugt wird.");

        if (M("voip") && V("deviceType").Equals("Router", StringComparison.OrdinalIgnoreCase))
            warnings.Add("VoIP: Das Modul ist für Router deaktiviert. Voice-VLAN wird nur für L2-/L3-Switches erzeugt.");

        if (M("qinq"))
        {
            if (V("deviceType").Equals("Router", StringComparison.OrdinalIgnoreCase))
                warnings.Add("QinQ: Das Modul ist für Router deaktiviert. QinQ wird nur für L2-/L3-Switches erzeugt.");

            var qinqMtu = V("qinqSystemMtu");
            if (!string.IsNullOrWhiteSpace(qinqMtu))
            {
                if (!int.TryParse(qinqMtu, out var mtu))
                    warnings.Add("QinQ: System MTU muss eine Zahl sein oder leer bleiben.");
                else if (mtu < 1504)
                    warnings.Add("QinQ: System MTU wirkt zu niedrig. QinQ benötigt wegen zusätzlichem VLAN-Tag mehr als Standard-Ethernet, abhängig von Plattform und Framegröße.");
                else if (mtu == 9000)
                    warnings.Add("QinQ: MTU 9000 ist ein Host-Jumbo-Wert und kein QinQ-Standard. Nur setzen, wenn das komplette Transportnetz Jumbo unterstützt.");
            }

            if (V("deviceType").Equals("L2-Switch", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(V("qinqSubifList")))
                warnings.Add("QinQ: L3-Subinterfaces werden beim L2-Switch bewusst nicht erzeugt.");

            foreach (var line in V("qinqTunnelList").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var cols = line.Split('|').Select(x => x.Trim()).ToArray();
                if (cols.Length >= 4 && int.TryParse(cols[3], out _))
                    warnings.Add("QinQ: Alte Tunnel-Listen mit Interface-MTU erkannt. Interface-MTU wird nicht mehr erzeugt; nutze bei Bedarf System MTU optional.");
            }
        }



        if (M("mpls"))
        {
            if (V("deviceType").Equals("L2-Switch", StringComparison.OrdinalIgnoreCase))
                warnings.Add("MPLS: Das Modul ist nur für Router und L3-Switches vorgesehen.");

            if (string.IsNullOrWhiteSpace(V("mplsInterfaceList")))
                warnings.Add("MPLS: Keine MPLS-Interfaces eingetragen. Ohne mpls ip auf Core-Interfaces entsteht keine LDP-Nachbarschaft.");

            foreach (var line in V("mplsInterfaceList").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var cols = line.Split('|').Select(x => x.Trim()).ToArray();
                if (cols.Length >= 3 && !string.IsNullOrWhiteSpace(cols[2]))
                {
                    if (!int.TryParse(cols[2], out var mtu))
                        warnings.Add("MPLS: MPLS-MTU in der Interface-Liste muss eine Zahl sein oder leer bleiben.");
                    else if (mtu < 1504)
                        warnings.Add("MPLS: MPLS-MTU wirkt sehr niedrig. Prüfe Label-Overhead, QinQ/Trunking und Plattformgrenzen.");
                    else if (mtu == 9000)
                        warnings.Add("MPLS: MTU 9000 ist ein Host-Jumbo-Wert. Nur setzen, wenn das gesamte Transportnetz diese Framegröße unterstützt.");
                }
            }

            if (!string.IsNullOrWhiteSpace(V("mplsVpnv4Peers")) && string.IsNullOrWhiteSpace(V("mplsBgpAs")))
                warnings.Add("MPLS: VPNv4-Peers sind eingetragen, aber MP-BGP AS ist leer.");
        }

        if (M("nat"))
        {
            var natIf = V("natIfList").ToLowerInvariant();
            if (!natIf.Contains("inside") || !natIf.Contains("outside"))
                warnings.Add("NAT: natIfList sollte mindestens ein inside- und ein outside-Interface enthalten.");
        }

        if (M("ospf"))
        {
            var ospfMode = V("ospfConfigMode");
            var hasNetworks = !string.IsNullOrWhiteSpace(V("ospfNetList"));
            var hasInterfaces = !string.IsNullOrWhiteSpace(V("ospfInterfaceAreaList"));
            if (ospfMode.Equals("Prozessbasiert", StringComparison.OrdinalIgnoreCase) && !hasNetworks)
                warnings.Add("OSPF: Prozessbasierter Modus gewählt, aber keine network-Zeilen eingetragen.");
            else if (ospfMode.Equals("Interfacebasiert", StringComparison.OrdinalIgnoreCase) && !hasInterfaces)
                warnings.Add("OSPF: Interfacebasierter Modus gewählt, aber keine Interface-/Area-Zuordnungen eingetragen.");
            else if (ospfMode.Equals("Gemischt", StringComparison.OrdinalIgnoreCase) && !hasNetworks && !hasInterfaces)
                warnings.Add("OSPF: Gemischter Modus gewählt, aber weder network-Zeilen noch Interface-/Area-Zuordnungen eingetragen.");
        }

        foreach (var duplicate in FindDuplicateFirstColumns(values))
            warnings.Add($"Mögliche doppelte Interface-/Objektdefinition: {duplicate}");

        return warnings.Count == 0 ? "Keine kritischen Hinweise." : string.Join(Environment.NewLine, warnings);
    }

    private static bool IsYes(string? value) =>
        value?.Equals("Ja", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("Yes", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<string> FindDuplicateFirstColumns(IReadOnlyDictionary<string, string> values)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dupes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in InterfaceFields)
        {
            if (!values.TryGetValue(field, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                var first = trimmed.Split('|')[0].Trim();
                if (first.Length == 0) continue;
                if (!seen.TryAdd(first, field)) dupes.Add($"{first} ({seen[first]} / {field})");
            }
        }

        return dupes;
    }
}


public sealed record StpValidationWarning(string FieldName, string Message);

public static class StpValidationService
{
    private static readonly HashSet<int> BridgePriorities = Enumerable.Range(0, 16).Select(x => x * 4096).ToHashSet();
    private static readonly HashSet<int> PortPriorities = Enumerable.Range(0, 16).Select(x => x * 16).ToHashSet();
    private static readonly HashSet<string> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "client", "client-port", "server", "server-port", "access-point", "ap",
        "switch-uplink", "switch uplink", "port-channel", "portchannel", "provider-uplink", "provider uplink"
    };

    public static IReadOnlyList<StpValidationWarning> Validate(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, bool> modules)
    {
        var warnings = new List<StpValidationWarning>();
        string V(string key) => values.TryGetValue(key, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
        bool M(string key) => modules.TryGetValue(key, out var enabled) && enabled;
        if (!M("stpExtended")) return warnings;

        var mode = V("stpGlobalMode").ToLowerInvariant();
        if (mode is not ("pvst" or "rapid-pvst" or "mst"))
            warnings.Add(new("stpGlobalMode", "Spanning-Tree-Modus muss pvst, rapid-pvst oder mst sein."));

        if (V("deviceType").Equals("Router", StringComparison.OrdinalIgnoreCase))
            warnings.Add(new("stpGlobalMode", "Spanning Tree ist in diesem Modul nur für L2- und L3-Switches vorgesehen."));

        if (mode is "pvst" or "rapid-pvst")
        {
            ValidateBridgePriority(V("stpVlanPriorityValue"), "stpVlanPriorityValue", "VLAN-Assistent", warnings);

            foreach (var row in Rows(V("stpVlanPriorityList")))
            {
                var p = Parts(row);
                if (p.Length < 2) { warnings.Add(new("stpVlanPriorityList", $"Ungültige Zeile: {row}")); continue; }
                ValidateVlanExpression(p[0], "stpVlanPriorityList", warnings);
                ValidateBridgePriority(p[1], "stpVlanPriorityList", $"VLAN {p[0]}", warnings);
            }

            foreach (var row in Rows(V("stpVlanTimersList")))
            {
                var p = Parts(row);
                if (p.Length < 1) continue;
                ValidateVlanExpression(p[0], "stpVlanTimersList", warnings);
                ValidateRange(p.ElementAtOrDefault(1), 1, 10, "Hello-Time", "stpVlanTimersList", warnings);
                ValidateRange(p.ElementAtOrDefault(2), 4, 30, "Forward-Time", "stpVlanTimersList", warnings);
                ValidateRange(p.ElementAtOrDefault(3), 6, 40, "Max-Age", "stpVlanTimersList", warnings);
            }

            ValidateInterfaceParameterRows(V("stpVlanInterfaceList"), "stpVlanInterfaceList", true, warnings);
            ValidateVlanRootPriorityConflicts(V, warnings);
        }
        else if (mode == "mst")
        {
            ValidateBridgePriority(V("stpMstPriorityValue"), "stpMstPriorityValue", "MST-Assistent", warnings);
            ValidateInterfaceParameterRows(V("stpMstInterfaceList"), "stpMstInterfaceList", false, warnings);
        }

        var rootGuard = Rows(V("stpRootGuardIfs")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loopGuard = Rows(V("stpLoopGuardIfs")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in rootGuard.Intersect(loopGuard, StringComparer.OrdinalIgnoreCase))
            warnings.Add(new("stpLoopGuardIfs", $"{duplicate}: Root Guard und Loop Guard sind gleichzeitig eingetragen."));

        var advancedIfTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows(V("stpInterfaceList")))
        {
            var p = Parts(row);
            if (p.Length == 0 || string.IsNullOrWhiteSpace(p[0])) continue;
            var ifc = p[0];
            advancedIfTargets.Add(ifc);
            var portFastValue = p.ElementAtOrDefault(1) ?? string.Empty;
            var portFast = IsYes(portFastValue) || portFastValue.Equals("Trunk", StringComparison.OrdinalIgnoreCase);
            var bpduGuard = IsYes(p.ElementAtOrDefault(2));
            var bpduFilter = IsYes(p.ElementAtOrDefault(3));
            var guard = p.ElementAtOrDefault(4);

            if (bpduGuard && bpduFilter)
                warnings.Add(new("stpInterfaceList", $"{ifc}: BPDU Guard und BPDU Filter gleichzeitig sind widersprüchlich."));
            if (portFast && (string.Equals(guard, "root", StringComparison.OrdinalIgnoreCase) || string.Equals(guard, "loop", StringComparison.OrdinalIgnoreCase)))
                warnings.Add(new("stpInterfaceList", $"{ifc}: PortFast zusammen mit {guard} Guard prüfen; dies ist typischerweise keine Endgeräteport-Konfiguration."));
            ValidatePortPriority(p.ElementAtOrDefault(6), "stpInterfaceList", ifc, warnings);
            ValidatePositive(p.ElementAtOrDefault(7), "Cost", "stpInterfaceList", ifc, warnings);
            if (mode == "pvst" && !string.IsNullOrWhiteSpace(p.ElementAtOrDefault(5)))
                warnings.Add(new("stpInterfaceList", $"{ifc}: Link-Type wird im PVST-Modus nicht exportiert."));
        }

        var profileTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows(V("stpInterfaceProfileList")))
        {
            var p = Parts(row);
            if (p.Length < 2) { warnings.Add(new("stpInterfaceProfileList", $"Ungültige Profilzeile: {row}")); continue; }
            profileTargets.Add(p[0]);
            if (!Profiles.Contains(p[1]))
                warnings.Add(new("stpInterfaceProfileList", $"{p[0]}: Unbekanntes Profil '{p[1]}'."));
        }
        foreach (var duplicate in profileTargets.Intersect(advancedIfTargets, StringComparer.OrdinalIgnoreCase))
            warnings.Add(new("stpInterfaceProfileList", $"{duplicate}: Interfaceprofil und manuelle STP-Interfacezeile können doppelte oder widersprüchliche Befehle erzeugen."));

        if (mode == "mst")
            ValidateMst(V, warnings);

        if (IsYes(V("stpErrdisableBpduguard")))
            ValidateRange(V("stpErrdisableInterval"), 1, 86400, "Errdisable-Intervall", "stpErrdisableInterval", warnings, required: true);

        ValidateResetRows(V("stpResetList"), warnings);
        ValidatePortChannelConsistency(V, mode, warnings);

        var platform = V("stpPlatformProfile");
        if (IsYes(V("stpStrictCompatibility")) && IsAutomatic(platform))
            warnings.Add(new("stpPlatformProfile", "Strikte Kompatibilität ist aktiv, aber kein konkretes Plattformprofil wurde gewählt."));
        if (platform.Equals("Packet Tracer", StringComparison.OrdinalIgnoreCase) && HasAdvancedFeatures(V))
            warnings.Add(new("stpPlatformProfile", "Packet Tracer unterstützt je nach simuliertem Switch nicht alle erweiterten STP-, MST-, Guard- und Errdisable-Befehle. Ausgabe am Zielgerät prüfen."));

        return warnings.Distinct().ToList();
    }

    private static void ValidateVlanRootPriorityConflicts(Func<string, string> v, List<StpValidationWarning> warnings)
    {
        var rootVlans = new SortedSet<int>();
        foreach (var row in Rows(v("stpRootList")))
        {
            var p = Parts(row);
            if (p.Length == 0) continue;
            foreach (var vlan in ParseVlanExpression(p[0], out _)) rootVlans.Add(vlan);
        }

        var priorityVlans = new SortedSet<int>();
        foreach (var row in Rows(v("stpVlanPriorityList")))
        {
            var p = Parts(row);
            if (p.Length == 0) continue;
            foreach (var vlan in ParseVlanExpression(p[0], out _)) priorityVlans.Add(vlan);
        }

        foreach (var vlan in rootVlans.Intersect(priorityVlans))
            warnings.Add(new("stpVlanPriorityList", $"VLAN {vlan}: Root-Rolle und manuelle Bridge-Priorität sind parallel definiert. Nur eine Methode verwenden."));

        var assistantTarget = v("stpVlanTarget");
        if (!string.IsNullOrWhiteSpace(assistantTarget))
        {
            var assistantVlans = ParseVlanExpression(assistantTarget, out _);
            foreach (var vlan in assistantVlans.Intersect(rootVlans.Union(priorityVlans)))
                warnings.Add(new("stpVlanTarget", $"VLAN {vlan}: Der Assistent und eine Listenzeile definieren denselben VLAN-STP-Wert doppelt."));
        }

        var assistantRole = v("stpVlanRootRole");
        var assistantPriority = v("stpVlanPriorityValue");
        if ((assistantRole.Equals("primary", StringComparison.OrdinalIgnoreCase) || assistantRole.Equals("secondary", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(assistantPriority))
            warnings.Add(new("stpVlanPriorityValue", "Beim VLAN-Assistenten sind Root-Rolle und manuelle Priorität gleichzeitig gesetzt. Die Root-Rolle hat beim Export Vorrang."));
    }

    private static void ValidateMst(Func<string, string> v, List<StpValidationWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(v("stpMstName")))
            warnings.Add(new("stpMstName", "MST benötigt einen Regionsnamen."));
        ValidateRange(v("stpMstRevision"), 0, 65535, "MST Revision", "stpMstRevision", warnings, required: true);
        ValidateRange(v("stpMstMaxHops"), 1, 40, "MST Max Hops", "stpMstMaxHops", warnings, required: true);

        var localMappings = new Dictionary<int, SortedSet<int>>();
        var vlanOwner = new Dictionary<int, int>();
        foreach (var row in Rows(v("stpMstInstanceList")))
        {
            var p = Parts(row);
            if (p.Length < 2) { warnings.Add(new("stpMstInstanceList", $"Ungültige MST-Zeile: {row}")); continue; }
            if (!int.TryParse(p[0], out var instance) || instance < 1 || instance > 4094)
            {
                warnings.Add(new("stpMstInstanceList", $"Ungültige MST-Instanz '{p[0]}'. Erlaubt: 1 bis 4094."));
                continue;
            }

            var vlans = ParseVlanExpression(p[1], out var error);
            if (error != null) warnings.Add(new("stpMstInstanceList", $"Instanz {instance}: {error}"));
            localMappings[instance] = vlans;
            foreach (var vlan in vlans)
            {
                if (vlanOwner.TryGetValue(vlan, out var oldInstance) && oldInstance != instance)
                    warnings.Add(new("stpMstInstanceList", $"VLAN {vlan} ist den MST-Instanzen {oldInstance} und {instance} zugeordnet."));
                else vlanOwner[vlan] = instance;
            }

            ValidateBridgePriority(p.ElementAtOrDefault(2), "stpMstInstanceList", $"MST {instance}", warnings);
            var role = p.ElementAtOrDefault(3);
            if (!string.IsNullOrWhiteSpace(role) && !role.Equals("primary", StringComparison.OrdinalIgnoreCase) && !role.Equals("secondary", StringComparison.OrdinalIgnoreCase))
                warnings.Add(new("stpMstInstanceList", $"MST {instance}: Root-Rolle muss primary, secondary oder leer sein."));
            if (!string.IsNullOrWhiteSpace(p.ElementAtOrDefault(2)) && !string.IsNullOrWhiteSpace(role))
                warnings.Add(new("stpMstInstanceList", $"MST {instance}: Manuelle Priorität und Root-Rolle sind gleichzeitig gesetzt. Nur eine Methode verwenden."));
        }

        var assistantInstance = v("stpMstTargetInstance");
        if (!string.IsNullOrWhiteSpace(assistantInstance) && int.TryParse(assistantInstance, out var assistantId) && localMappings.ContainsKey(assistantId))
            warnings.Add(new("stpMstTargetInstance", $"MST {assistantId}: Der Assistent und die Instanzliste definieren dieselbe Instanz doppelt."));
        if ((v("stpMstRootRole").Equals("primary", StringComparison.OrdinalIgnoreCase) || v("stpMstRootRole").Equals("secondary", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(v("stpMstPriorityValue")))
            warnings.Add(new("stpMstPriorityValue", "Beim MST-Assistenten sind Root-Rolle und manuelle Priorität gleichzeitig gesetzt. Die Root-Rolle hat beim Export Vorrang."));

        if (localMappings.Count == 0)
            warnings.Add(new("stpMstInstanceList", "MST ist gewählt, aber es wurde keine VLAN-zu-Instanz-Zuordnung eingetragen."));

        var localName = v("stpMstName");
        var localRevision = v("stpMstRevision");
        var localNormalized = NormalizeMappings(localMappings);
        foreach (var row in Rows(v("stpMstPeerRegionList")))
        {
            var p = Parts(row);
            if (p.Length < 4) { warnings.Add(new("stpMstPeerRegionList", $"Ungültige Vergleichszeile: {row}")); continue; }
            var peer = p[0];
            if (!p[1].Equals(localName, StringComparison.Ordinal))
                warnings.Add(new("stpMstPeerRegionList", $"{peer}: Regionsname '{p[1]}' weicht von '{localName}' ab."));
            if (!p[2].Equals(localRevision, StringComparison.OrdinalIgnoreCase))
                warnings.Add(new("stpMstPeerRegionList", $"{peer}: Revision '{p[2]}' weicht von '{localRevision}' ab."));
            var peerMappings = ParsePeerMappings(p[3], out var peerError);
            if (peerError != null)
                warnings.Add(new("stpMstPeerRegionList", $"{peer}: {peerError}"));
            else if (!NormalizeMappings(peerMappings).Equals(localNormalized, StringComparison.Ordinal))
                warnings.Add(new("stpMstPeerRegionList", $"{peer}: VLAN-zu-Instanz-Mapping weicht von der lokalen MST-Region ab."));
        }
    }

    private static void ValidatePortChannelConsistency(Func<string, string> v, string mode, List<StpValidationWarning> warnings)
    {
        var memberToChannel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows(v("etherExtList")))
        {
            var p = Parts(row);
            if (p.Length < 2 || string.IsNullOrWhiteSpace(p[0])) continue;
            var channel = p[0].StartsWith("Port-channel", StringComparison.OrdinalIgnoreCase) ? p[0] : "Port-channel" + p[0];
            foreach (var member in ExpandInterfaceExpression(p[1])) memberToChannel[member] = channel;
        }

        var fields = new List<string> { "stpInterfaceProfileList", "stpInterfaceList" };
        if (mode is "pvst" or "rapid-pvst") fields.Add("stpVlanInterfaceList");
        if (mode == "mst") fields.Add("stpMstInterfaceList");
        foreach (var field in fields)
        {
            foreach (var row in Rows(v(field)))
            {
                var target = Parts(row).FirstOrDefault();
                if (target != null && memberToChannel.TryGetValue(target, out var channel))
                    warnings.Add(new(field, $"{target} ist Mitglied von {channel}. STP-Optionen sollten konsistent am Port-Channel statt am einzelnen Mitgliedsport gesetzt werden."));
            }
        }
    }

    private static bool HasAdvancedFeatures(Func<string, string> v)
    {
        var mode = v("stpGlobalMode");
        return mode.Equals("mst", StringComparison.OrdinalIgnoreCase) ||
               IsYes(v("stpBpdufilterDefault")) ||
               IsYes(v("stpLoopguardDefault")) ||
               ((mode.Equals("pvst", StringComparison.OrdinalIgnoreCase) || mode.Equals("rapid-pvst", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(v("stpVlanInterfaceList"))) ||
               (mode.Equals("mst", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v("stpMstInterfaceList"))) ||
               !string.IsNullOrWhiteSpace(v("stpResetList"));
    }

    private static void ValidateResetRows(string value, List<StpValidationWarning> warnings)
    {
        var global = new HashSet<string>(new[] { "portfast-default", "bpduguard-default", "bpdufilter-default", "loopguard-default", "etherchannel-guard", "uplinkfast", "backbonefast", "max-hops" }, StringComparer.OrdinalIgnoreCase);
        var vlan = new HashSet<string>(new[] { "priority", "root", "hello-time", "forward-time", "max-age" }, StringComparer.OrdinalIgnoreCase);
        var mst = new HashSet<string>(new[] { "priority", "root" }, StringComparer.OrdinalIgnoreCase);
        var iface = new HashSet<string>(new[] { "portfast", "bpduguard", "bpdufilter", "guard", "link-type", "port-priority", "cost" }, StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows(value))
        {
            var p = Parts(row);
            if (p.Length < 3) { warnings.Add(new("stpResetList", $"Ungültige Rücksetzzeile: {row}")); continue; }
            var allowed = p[0].ToLowerInvariant() switch
            {
                "global" => global.Contains(p[2]),
                "vlan" => !string.IsNullOrWhiteSpace(p[1]) && vlan.Contains(p[2]),
                "mst" => !string.IsNullOrWhiteSpace(p[1]) && mst.Contains(p[2]),
                "interface" => !string.IsNullOrWhiteSpace(p[1]) && iface.Contains(p[2]),
                _ => false
            };
            if (!allowed) warnings.Add(new("stpResetList", $"Nicht unterstützte Rücksetzdefinition: {row}"));
        }
    }

    private static void ValidateInterfaceParameterRows(string value, string field, bool vlanMode, List<StpValidationWarning> warnings)
    {
        foreach (var row in Rows(value))
        {
            var p = Parts(row);
            if (p.Length < 2) { warnings.Add(new(field, $"Ungültige Zeile: {row}")); continue; }
            if (vlanMode) ValidateVlanExpression(p[1], field, warnings);
            else if (!int.TryParse(p[1], out var instance) || instance < 0 || instance > 4094)
                warnings.Add(new(field, $"{p[0]}: Ungültige MST-Instanz '{p[1]}'."));
            ValidatePortPriority(p.ElementAtOrDefault(2), field, p[0], warnings);
            ValidatePositive(p.ElementAtOrDefault(3), "Cost", field, p[0], warnings);
        }
    }

    private static void ValidateBridgePriority(string? value, string field, string context, List<StpValidationWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!int.TryParse(value, out var priority) || !BridgePriorities.Contains(priority))
            warnings.Add(new(field, $"{context}: Bridge-Priorität muss 0 bis 61440 in Schritten von 4096 sein."));
    }

    private static void ValidatePortPriority(string? value, string field, string context, List<StpValidationWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!int.TryParse(value, out var priority) || !PortPriorities.Contains(priority))
            warnings.Add(new(field, $"{context}: Port-Priority muss 0 bis 240 in Schritten von 16 sein."));
    }

    private static void ValidatePositive(string? value, string label, string field, string context, List<StpValidationWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!int.TryParse(value, out var number) || number <= 0)
            warnings.Add(new(field, $"{context}: {label} muss eine positive Ganzzahl sein."));
    }

    private static void ValidateRange(string? value, int min, int max, string label, string field, List<StpValidationWarning> warnings, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) warnings.Add(new(field, $"{label} ist erforderlich."));
            return;
        }
        if (!int.TryParse(value, out var number) || number < min || number > max)
            warnings.Add(new(field, $"{label} muss zwischen {min} und {max} liegen."));
    }

    private static void ValidateVlanExpression(string value, string field, List<StpValidationWarning> warnings)
    {
        ParseVlanExpression(value, out var error);
        if (error != null) warnings.Add(new(field, error));
    }

    private static SortedSet<int> ParseVlanExpression(string value, out string? error)
    {
        error = null;
        var result = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(value)) { error = "VLAN-Angabe ist leer."; return result; }
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = token.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (range.Length == 1 && int.TryParse(range[0], out var single) && single is >= 1 and <= 4094)
            {
                result.Add(single);
                continue;
            }
            if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end) && start >= 1 && end <= 4094 && start <= end)
            {
                for (var vlan = start; vlan <= end; vlan++) result.Add(vlan);
                continue;
            }
            error = $"Ungültiger VLAN-Ausdruck '{token}'. Erlaubt sind VLAN 1 bis 4094 und Bereiche wie 10-20.";
            return result;
        }
        return result;
    }

    private static Dictionary<int, SortedSet<int>> ParsePeerMappings(string value, out string? error)
    {
        error = null;
        var result = new Dictionary<int, SortedSet<int>>();
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (split.Length != 2 || !int.TryParse(split[0], out var instance))
            {
                error = $"Ungültiges Mapping '{entry}'. Erwartet: Instanz:VLANs.";
                return result;
            }
            var vlans = ParseVlanExpression(split[1], out error);
            if (error != null) return result;
            result[instance] = vlans;
        }
        return result;
    }

    private static string NormalizeMappings(Dictionary<int, SortedSet<int>> mappings) =>
        string.Join(";", mappings.OrderBy(x => x.Key).Select(x => $"{x.Key}:{string.Join(",", x.Value)}"));

    private static IEnumerable<string> ExpandInterfaceExpression(string value)
    {
        foreach (var token in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(token, @"^(.*?/)(\d+)-(\d+)$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[2].Value, out var start) && int.TryParse(match.Groups[3].Value, out var end) && start <= end)
            {
                for (var number = start; number <= end; number++) yield return match.Groups[1].Value + number;
            }
            else yield return token;
        }
    }

    private static IEnumerable<string> Rows(string value) =>
        (value ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal));

    private static string[] Parts(string row) => row.Split('|').Select(x => x.Trim()).ToArray();
    private static bool IsYes(string? value) => value?.Equals("Ja", StringComparison.OrdinalIgnoreCase) == true || value?.Equals("Yes", StringComparison.OrdinalIgnoreCase) == true || value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsAutomatic(string? value) => value?.Equals("Automatisch", StringComparison.OrdinalIgnoreCase) == true || value?.Equals("Automatic", StringComparison.OrdinalIgnoreCase) == true;
}

public static class SubnettingCalculator
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    public static bool TryCalculate(string networkText, string newPrefixText, string maxSubnetsText, out string output)
    {
        var messages = new List<string>();
        var networkInput = (networkText ?? string.Empty).Trim();

        if (!TryParseCidr(networkInput, out var baseIp, out var basePrefix, out var addressBits, out var cidrError))
        {
            output = cidrError;
            return false;
        }

        if (!TryParsePrefix(newPrefixText, addressBits, out var newPrefix))
        {
            output = $"Neuer Präfix ungültig. Erlaubt ist 0 bis {addressBits}, z. B. {(addressBits == 32 ? "26" : "64")}.";
            return false;
        }

        if (newPrefix < basePrefix)
        {
            output = $"Der neue Präfix /{newPrefix} ist kleiner als das Ausgangsnetz /{basePrefix}. Für Subnetting muss er gleich oder größer sein.";
            return false;
        }

        var maxSubnets = 32;
        if (!string.IsNullOrWhiteSpace(maxSubnetsText) &&
            (!int.TryParse(maxSubnetsText.Trim(), out maxSubnets) || maxSubnets < 1))
        {
            output = "Max. Subnetze anzeigen ist ungültig. Erlaubt ist eine Zahl ab 1.";
            return false;
        }
        maxSubnets = Math.Min(maxSubnets, 512);

        var family = baseIp.AddressFamily;
        var familyName = family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
        var baseValue = ToBigInteger(baseIp);
        var baseMask = MaskFromPrefix(addressBits, basePrefix);
        var newMask = MaskFromPrefix(addressBits, newPrefix);
        var baseNetwork = baseValue & baseMask;
        var baseSize = BigInteger.One << (addressBits - basePrefix);
        var baseEnd = baseNetwork + baseSize - BigInteger.One;
        var subnetSize = BigInteger.One << (addressBits - newPrefix);
        var totalSubnets = BigInteger.One << (newPrefix - basePrefix);
        var displayCount = totalSubnets > maxSubnets ? maxSubnets : (int)totalSubnets;
        var addressesPerSubnet = subnetSize;

        messages.Add($"SUBNETTING – {familyName}");
        messages.Add(new string('=', 88));
        messages.Add($"Ausgangsnetz       : {ToIpString(baseNetwork, family)}/{basePrefix}");
        messages.Add($"Adressbereich      : {ToIpString(baseNetwork, family)} - {ToIpString(baseEnd, family)}");
        messages.Add($"Neuer Präfix       : /{newPrefix}");

        if (family == AddressFamily.InterNetwork)
        {
            messages.Add($"Ausgangsmaske      : {ToIpString(baseMask, family)}");
            messages.Add($"Neue Maske         : {ToIpString(newMask, family)}");
            var allBits = (BigInteger.One << 32) - BigInteger.One;
            messages.Add($"Cisco Wildcard     : {ToIpString(allBits ^ newMask, family)}");
            messages.Add($"Hosts je Subnetz   : {Ipv4HostCountText(newPrefix, addressesPerSubnet)}");
        }
        else
        {
            messages.Add($"IPv6-Präfixmaske   : {ToIpString(newMask, family)}");
            messages.Add($"Adressen je Subnetz: {FormatNumber(addressesPerSubnet)}");
            if (newPrefix == 64)
                messages.Add("Hinweis             : /64 ist die übliche Präfixlänge für IPv6-LANs und SLAAC.");
            else if (newPrefix == 127)
                messages.Add("Hinweis             : /127 eignet sich für IPv6-Punkt-zu-Punkt-Verbindungen.");
            else if (newPrefix == 128)
                messages.Add("Hinweis             : /128 beschreibt eine einzelne IPv6-Adresse beziehungsweise Hostroute.");
        }

        messages.Add($"Subnetze möglich   : {FormatNumber(totalSubnets)}");
        messages.Add($"Angezeigt          : {displayCount} von {FormatNumber(totalSubnets)}");
        messages.Add(new string('=', 88));
        messages.Add(string.Empty);

        for (var i = 0; i < displayCount; i++)
        {
            var subnet = baseNetwork + (BigInteger)i * subnetSize;
            var subnetEnd = subnet + subnetSize - BigInteger.One;
            if (subnetEnd > baseEnd)
                break;

            messages.Add($"#{i + 1:000}  {ToIpString(subnet, family)}/{newPrefix}");
            messages.Add($"     Netz/Präfix : {ToIpString(subnet, family)}/{newPrefix}");

            if (family == AddressFamily.InterNetwork)
            {
                var allBits = (BigInteger.One << 32) - BigInteger.One;
                messages.Add($"     Maske       : {ToIpString(newMask, family)}");
                messages.Add($"     Wildcard    : {ToIpString(allBits ^ newMask, family)}");

                if (newPrefix <= 30)
                {
                    messages.Add($"     Hostbereich : {ToIpString(subnet + 1, family)} - {ToIpString(subnetEnd - 1, family)}");
                    messages.Add($"     Broadcast   : {ToIpString(subnetEnd, family)}");
                }
                else if (newPrefix == 31)
                {
                    messages.Add($"     Endpunkte   : {ToIpString(subnet, family)} - {ToIpString(subnetEnd, family)} (/31 Punkt-zu-Punkt)");
                    messages.Add("     Broadcast   : keiner im klassischen Sinne");
                }
                else
                {
                    messages.Add($"     Host        : {ToIpString(subnet, family)} (/32 Hostroute)");
                    messages.Add("     Broadcast   : keiner");
                }
            }
            else
            {
                messages.Add($"     Erste Adresse: {ToIpString(subnet, family)}");
                messages.Add($"     Letzte Adresse: {ToIpString(subnetEnd, family)}");
                messages.Add($"     Adressen      : {FormatNumber(addressesPerSubnet)}");
                messages.Add("     Broadcast     : nicht vorhanden bei IPv6");
            }

            messages.Add(string.Empty);
        }

        if (totalSubnets > displayCount)
            messages.Add($"Hinweis: Es werden nur {displayCount} Subnetze angezeigt. Erhöhe 'Max. Subnetze anzeigen' für weitere Einträge (maximal 512). ");

        output = string.Join(Environment.NewLine, messages);
        return true;
    }

    private static bool TryParseCidr(string value, out IPAddress ip, out int prefix, out int addressBits, out string error)
    {
        ip = IPAddress.None;
        prefix = 0;
        addressBits = 0;
        error = string.Empty;

        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            error = "Netz / CIDR ungültig. Beispiele: 192.168.10.0/24 oder 2001:db8:10::/48";
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out var parsedIp) ||
            (parsedIp.AddressFamily != AddressFamily.InterNetwork && parsedIp.AddressFamily != AddressFamily.InterNetworkV6))
        {
            error = $"IPv4-/IPv6-Netzadresse ungültig: {parts[0]}";
            return false;
        }

        ip = parsedIp;
        addressBits = parsedIp.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        if (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > addressBits)
        {
            error = $"CIDR-Präfix ungültig: {parts[1]}. Erlaubt ist 0 bis {addressBits}.";
            return false;
        }

        return true;
    }

    private static bool TryParsePrefix(string? value, int maxPrefix, out int prefix)
    {
        var normalized = (value ?? string.Empty).Trim().TrimStart('/');
        return int.TryParse(normalized, out prefix) && prefix >= 0 && prefix <= maxPrefix;
    }

    private static string Ipv4HostCountText(int prefix, BigInteger addressCount) => prefix switch
    {
        32 => "1 (/32 Hostroute)",
        31 => "2 (/31 Punkt-zu-Punkt)",
        _ => FormatNumber(addressCount - 2)
    };

    private static BigInteger MaskFromPrefix(int totalBits, int prefix)
    {
        if (prefix == 0) return BigInteger.Zero;
        var allBits = (BigInteger.One << totalBits) - BigInteger.One;
        var hostBits = totalBits - prefix;
        var hostMask = hostBits == 0 ? BigInteger.Zero : (BigInteger.One << hostBits) - BigInteger.One;
        return allBits ^ hostMask;
    }

    internal static BigInteger ToBigInteger(IPAddress ip) =>
        new(ip.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

    internal static IPAddress FromBigInteger(BigInteger value, AddressFamily family)
    {
        var byteCount = family == AddressFamily.InterNetwork ? 4 : 16;
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > byteCount)
            raw = raw[^byteCount..];

        var bytes = new byte[byteCount];
        Buffer.BlockCopy(raw, 0, bytes, byteCount - raw.Length, raw.Length);
        return new IPAddress(bytes);
    }

    internal static string ToIpString(BigInteger value, AddressFamily family) =>
        FromBigInteger(value, family).ToString();

    internal static BigInteger MaskForPrefix(int totalBits, int prefix) => MaskFromPrefix(totalBits, prefix);

    internal static string FormatNumber(BigInteger value) => value.ToString("N0", GermanCulture);
}

public static class IpCalculator
{
    public static bool TryCalculate(string ipText, string maskText, string wildcardText, out string output)
    {
        var messages = new List<string>();
        var rawIp = (ipText ?? string.Empty).Trim();
        int? embeddedPrefix = null;

        if (rawIp.Contains('/'))
        {
            var parts = rawIp.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var parsedEmbeddedPrefix))
            {
                output = $"IP/CIDR ungültig: {ipText}";
                return false;
            }
            rawIp = parts[0];
            embeddedPrefix = parsedEmbeddedPrefix;
        }

        if (!IPAddress.TryParse(rawIp, out var ip) ||
            (ip.AddressFamily != AddressFamily.InterNetwork && ip.AddressFamily != AddressFamily.InterNetworkV6))
        {
            output = $"IPv4-/IPv6-Adresse ungültig: {ipText}";
            return false;
        }

        var isIpv4 = ip.AddressFamily == AddressFamily.InterNetwork;
        var addressBits = isIpv4 ? 32 : 128;

        int prefix;
        if (embeddedPrefix.HasValue)
        {
            prefix = embeddedPrefix.Value;
            if (prefix < 0 || prefix > addressBits)
            {
                output = $"Präfix /{prefix} ist für {(isIpv4 ? "IPv4" : "IPv6")} ungültig. Erlaubt ist 0 bis {addressBits}.";
                return false;
            }
        }
        else if (!TryParseMaskOrPrefix(maskText, isIpv4, out prefix, out var maskError))
        {
            output = maskError;
            return false;
        }

        var value = SubnettingCalculator.ToBigInteger(ip);
        var mask = SubnettingCalculator.MaskForPrefix(addressBits, prefix);
        var network = value & mask;
        var addressCount = BigInteger.One << (addressBits - prefix);
        var lastAddress = network + addressCount - BigInteger.One;
        var familyName = isIpv4 ? "IPv4" : "IPv6";

        messages.Add($"EINZEL-IP-PRÜFUNG – {familyName}");
        messages.Add(new string('=', 72));
        messages.Add($"Adresse          : {ip}/{prefix}");
        messages.Add($"Netz/Präfix      : {SubnettingCalculator.ToIpString(network, ip.AddressFamily)}/{prefix}");
        messages.Add($"Adressbereich    : {SubnettingCalculator.ToIpString(network, ip.AddressFamily)} - {SubnettingCalculator.ToIpString(lastAddress, ip.AddressFamily)}");
        messages.Add($"Adressen gesamt  : {SubnettingCalculator.FormatNumber(addressCount)}");

        if (isIpv4)
        {
            var allBits = (BigInteger.One << 32) - BigInteger.One;
            var wildcard = allBits ^ mask;
            var wildcardString = SubnettingCalculator.ToIpString(wildcard, AddressFamily.InterNetwork);
            messages.Add($"Subnetzmaske     : {SubnettingCalculator.ToIpString(mask, AddressFamily.InterNetwork)}");
            messages.Add($"Cisco Wildcard   : {wildcardString}");

            if (prefix <= 30)
            {
                messages.Add($"Erster Host      : {SubnettingCalculator.ToIpString(network + 1, AddressFamily.InterNetwork)}");
                messages.Add($"Letzter Host     : {SubnettingCalculator.ToIpString(lastAddress - 1, AddressFamily.InterNetwork)}");
                messages.Add($"Broadcast        : {SubnettingCalculator.ToIpString(lastAddress, AddressFamily.InterNetwork)}");
                messages.Add($"Hosts nutzbar    : {SubnettingCalculator.FormatNumber(addressCount - 2)}");
                messages.Add($"Adressart        : {(value == network ? "Netzadresse" : value == lastAddress ? "Broadcast-Adresse" : "nutzbare Hostadresse")}");
            }
            else if (prefix == 31)
            {
                messages.Add($"P2P-Endpunkte    : {SubnettingCalculator.ToIpString(network, AddressFamily.InterNetwork)} - {SubnettingCalculator.ToIpString(lastAddress, AddressFamily.InterNetwork)}");
                messages.Add("Broadcast        : keiner im klassischen Sinne");
                messages.Add("Adressart        : /31-Punkt-zu-Punkt-Endpunkt");
            }
            else
            {
                messages.Add("Broadcast        : keiner");
                messages.Add("Adressart        : /32-Hostroute");
            }

            if (!string.IsNullOrWhiteSpace(wildcardText))
                messages.Add(string.Equals(wildcardText.Trim(), wildcardString, StringComparison.OrdinalIgnoreCase)
                    ? "Wildcard-Prüfung : passt"
                    : $"Wildcard-Prüfung : passt nicht; erwartet wird {wildcardString}");
        }
        else
        {
            messages.Add($"IPv6-Präfixmaske : {SubnettingCalculator.ToIpString(mask, AddressFamily.InterNetworkV6)}");
            messages.Add("Broadcast        : nicht vorhanden bei IPv6");
            messages.Add($"Adressart/Scope : {DescribeIpv6Scope(ip)}");
            messages.Add($"Hostanteil       : {SubnettingCalculator.ToIpString(value - network, AddressFamily.InterNetworkV6)}");

            if (!string.IsNullOrWhiteSpace(wildcardText))
                messages.Add("Wildcard-Prüfung : nicht anwendbar bei IPv6");
        }

        output = string.Join(Environment.NewLine, messages);
        return true;
    }

    private static bool TryParseMaskOrPrefix(string? maskText, bool isIpv4, out int prefix, out string error)
    {
        prefix = 0;
        error = string.Empty;
        var input = (maskText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            error = $"Präfix{(isIpv4 ? " oder Subnetzmaske" : string.Empty)} fehlt.";
            return false;
        }

        var normalized = input.TrimStart('/');
        var max = isIpv4 ? 32 : 128;
        if (int.TryParse(normalized, out prefix))
        {
            if (prefix >= 0 && prefix <= max) return true;
            error = $"Präfix /{prefix} ist ungültig. Erlaubt ist 0 bis {max}.";
            return false;
        }

        if (!isIpv4)
        {
            error = "IPv6 benötigt einen Präfix zwischen /0 und /128, z. B. /64.";
            return false;
        }

        if (!IPAddress.TryParse(input, out var maskIp) || maskIp.AddressFamily != AddressFamily.InterNetwork || !IsContiguousIpv4Mask(maskIp))
        {
            error = $"IPv4-Subnetzmaske ungültig: {maskText}. Erlaubt sind z. B. 255.255.255.0, 24 oder /24.";
            return false;
        }

        prefix = PrefixFromIpv4Mask(maskIp);
        return true;
    }

    private static bool IsContiguousIpv4Mask(IPAddress mask)
    {
        var value = (uint)SubnettingCalculator.ToBigInteger(mask);
        var inverted = ~value;
        return (inverted & (inverted + 1)) == 0;
    }

    private static int PrefixFromIpv4Mask(IPAddress mask)
    {
        var value = (uint)SubnettingCalculator.ToBigInteger(mask);
        var count = 0;
        for (var bit = 31; bit >= 0; bit--)
        {
            if (((value >> bit) & 1) == 1) count++;
            else break;
        }
        return count;
    }

    private static string DescribeIpv6Scope(IPAddress ip)
    {
        if (ip.Equals(IPAddress.IPv6Loopback)) return "Loopback";
        if (ip.Equals(IPAddress.IPv6None)) return "unspezifizierte Adresse";
        if (ip.IsIPv6LinkLocal) return "Link-Local";
        if (ip.IsIPv6Multicast) return "Multicast";
        var bytes = ip.GetAddressBytes();
        if ((bytes[0] & 0xFE) == 0xFC) return "Unique Local Address (ULA)";
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return "Dokumentationspräfix 2001:db8::/32";
        return "Global Unicast oder sonstiger IPv6-Adressbereich";
    }
}

