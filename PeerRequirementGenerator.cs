using System.Net;
using System.Text;

namespace CiscoConfigGuiWpf;

public static class PeerRequirementGenerator
{
    private sealed record InterfaceAddress(string Name, IPAddress Address, IPAddress Mask);

    public static string Generate(GenerationRequest request)
    {
        var sb = new StringBuilder();
        var sectionCount = 0;
        string V(string key) => request.Values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
        bool M(string key) => request.Modules.TryGetValue(key, out var active) && active;
        var addresses = CollectInterfaceAddresses(V);

        void Section(string title, Action body)
        {
            sectionCount++;
            if (sectionCount > 1) sb.AppendLine();
            sb.AppendLine("! =========================================================");
            sb.AppendLine($"! {title}");
            sb.AppendLine("! =========================================================");
            body();
        }

        sb.AppendLine("! GEGENSTELLEN- / PEER-ANFORDERUNGEN");
        sb.AppendLine("! Automatisch aus den aktuell aktivierten Modulen abgeleitet.");
        sb.AppendLine("! Platzhalter in <...> müssen am Nachbargerät angepasst werden.");

        if (M("trunkUplink") && !string.IsNullOrWhiteSpace(V("trunkUplinkList")))
        {
            Section("TRUNK-GEGENSTELLE", () =>
            {
                foreach (var row in Rows(V("trunkUplinkList")))
                {
                    var p = Parts(row);
                    if (p.Length < 2) continue;
                    var localIf = p[0];
                    var allowed = p.ElementAtOrDefault(1) ?? string.Empty;
                    var native = p.ElementAtOrDefault(2) ?? string.Empty;
                    var description = p.ElementAtOrDefault(3) ?? string.Empty;
                    sb.AppendLine($"! Lokale Seite: {localIf}{(string.IsNullOrWhiteSpace(description) ? string.Empty : " - " + description)}");
                    sb.AppendLine("interface <GEGENSTELLEN-INTERFACE>");
                    sb.AppendLine(" description Verbindung zur lokalen Seite");
                    sb.AppendLine(" switchport mode trunk");
                    if (!string.IsNullOrWhiteSpace(native)) sb.AppendLine($" switchport trunk native vlan {native}");
                    if (!string.IsNullOrWhiteSpace(allowed)) sb.AppendLine($" switchport trunk allowed vlan {allowed}");
                    sb.AppendLine(" no shutdown");
                    sb.AppendLine("!");
                    sb.AppendLine("! Erforderlich: Allowed-VLANs und Native-VLAN müssen auf beiden Seiten übereinstimmen.");
                    sb.AppendLine("!");
                }
            });
        }

        if ((M("routerStick") && !string.IsNullOrWhiteSpace(V("rosVlanList"))) ||
            (M("subinterfaces") && !string.IsNullOrWhiteSpace(V("subifList"))))
        {
            Section("ROUTER-ON-A-STICK / SUBINTERFACE-GEGENSTELLE", () =>
            {
                var vlanIds = new SortedSet<int>();
                foreach (var row in Rows(V("rosVlanList")))
                {
                    var p = Parts(row);
                    if (p.Length > 0 && int.TryParse(p[0], out var vlan)) vlanIds.Add(vlan);
                }
                foreach (var row in Rows(V("subifList")))
                {
                    var p = Parts(row);
                    if (p.Length >= 3 && int.TryParse(p[2], out var vlan)) vlanIds.Add(vlan);
                }

                sb.AppendLine("interface <SWITCH-PORT-ZUM-ROUTER>");
                sb.AppendLine(" switchport mode trunk");
                if (vlanIds.Count > 0) sb.AppendLine($" switchport trunk allowed vlan {string.Join(",", vlanIds)}");
                if (!string.IsNullOrWhiteSpace(V("rosNativeVlan")))
                    sb.AppendLine($" switchport trunk native vlan {V("rosNativeVlan")}");
                sb.AppendLine(" no shutdown");
                sb.AppendLine("!");
                sb.AppendLine("! Jede Router-Subinterface-VLAN-ID muss auf dem Switch existieren und über den Trunk zugelassen sein.");
                sb.AppendLine("! Ein als native konfiguriertes VLAN muss auf beiden Seiten identisch und nur auf der Routerseite ohne dot1q-Tag behandelt werden.");
            });
        }

        if (M("etherChannelExt") && !string.IsNullOrWhiteSpace(V("etherExtList")))
        {
            Section("ETHERCHANNEL-GEGENSTELLE", () =>
            {
                foreach (var row in Rows(V("etherExtList")))
                {
                    var p = Parts(row);
                    if (p.Length < 4) continue;
                    var channel = p[0];
                    var localMembers = p[1];
                    var localMode = p[2].ToLowerInvariant();
                    var switchMode = p[3].ToLowerInvariant();
                    var vlans = p.ElementAtOrDefault(4) ?? string.Empty;
                    var native = p.ElementAtOrDefault(6) ?? string.Empty;
                    var peerMode = localMode switch
                    {
                        "passive" => "active",
                        "auto" => "desirable",
                        "on" => "on",
                        "desirable" => "desirable oder auto",
                        _ => "active oder passive"
                    };

                    sb.AppendLine($"! Lokaler Port-Channel {channel}, Mitglieder {localMembers}, Modus {localMode}");
                    sb.AppendLine("interface range <GEGENSTELLEN-MITGLIEDSPORTS>");
                    if (localMode == "passive") sb.AppendLine($" channel-group {channel} mode active");
                    else if (localMode == "auto") sb.AppendLine($" channel-group {channel} mode desirable");
                    else if (localMode == "on") sb.AppendLine($" channel-group {channel} mode on");
                    else sb.AppendLine($" channel-group {channel} mode {localMode}");
                    sb.AppendLine(" no shutdown");
                    sb.AppendLine("!");
                    sb.AppendLine($"interface Port-channel{channel}");
                    if (switchMode == "trunk")
                    {
                        sb.AppendLine(" switchport mode trunk");
                        if (!string.IsNullOrWhiteSpace(native)) sb.AppendLine($" switchport trunk native vlan {native}");
                        if (!string.IsNullOrWhiteSpace(vlans)) sb.AppendLine($" switchport trunk allowed vlan {vlans}");
                    }
                    else if (switchMode == "access")
                    {
                        sb.AppendLine(" switchport mode access");
                        if (!string.IsNullOrWhiteSpace(vlans)) sb.AppendLine($" switchport access vlan {vlans}");
                    }
                    sb.AppendLine(" no shutdown");
                    sb.AppendLine("!");
                    sb.AppendLine($"! Zulässiger Gegenstellenmodus: {peerMode}. Kanalnummer, Layer-2-Modus, VLANs und Native-VLAN müssen konsistent sein.");
                    sb.AppendLine("!");
                }
            });
        }

        if (M("stpExtended"))
        {
            var mode = V("stpGlobalMode");
            if (mode.Equals("mst", StringComparison.OrdinalIgnoreCase))
            {
                Section("MST-REGION DER GEGENSTELLE", () =>
                {
                    sb.AppendLine("spanning-tree mode mst");
                    sb.AppendLine("spanning-tree mst configuration");
                    if (!string.IsNullOrWhiteSpace(V("stpMstName"))) sb.AppendLine($" name {V("stpMstName")}");
                    if (!string.IsNullOrWhiteSpace(V("stpMstRevision"))) sb.AppendLine($" revision {V("stpMstRevision")}");
                    foreach (var row in Rows(V("stpMstInstanceList")))
                    {
                        var p = Parts(row);
                        if (p.Length >= 2 && !string.IsNullOrWhiteSpace(p[0]) && !string.IsNullOrWhiteSpace(p[1]))
                            sb.AppendLine($" instance {p[0]} vlan {p[1]}");
                    }
                    sb.AppendLine(" exit");
                    sb.AppendLine("!");
                    sb.AppendLine("! Regionsname, Revision und VLAN-zu-Instanz-Zuordnung müssen exakt identisch sein.");
                    sb.AppendLine("! Die Bridge-Priority sollte bewusst unterschiedlich gewählt werden, damit nur ein Root-Bridge-Kandidat entsteht.");
                });
            }
            else if (!string.IsNullOrWhiteSpace(mode))
            {
                Section("SPANNING-TREE-GEGENSTELLE", () =>
                {
                    sb.AppendLine($"spanning-tree mode {mode}");
                    sb.AppendLine("!");
                    sb.AppendLine("! Der STP-Modus sollte im gemeinsamen Layer-2-Bereich konsistent sein.");
                    sb.AppendLine("! Root Guard, Loop Guard und PortFast werden nicht blind gespiegelt, sondern entsprechend der Portrolle gesetzt.");
                });
            }
        }

        if (M("ospf"))
        {
            Section("OSPF-GEGENSTELLE", () =>
            {
                var pid = string.IsNullOrWhiteSpace(V("ospfPid")) ? "1" : V("ospfPid");
                var localRid = V("ospfRid");
                sb.AppendLine($"router ospf {pid}");
                sb.AppendLine(" router-id <EINDEUTIGE-GEGENSTELLEN-ROUTER-ID>");
                if (V("ospfLogAdjacency").Equals("Detail", StringComparison.OrdinalIgnoreCase)) sb.AppendLine(" log-adjacency-changes detail");
                else if (V("ospfLogAdjacency").Equals("Ja", StringComparison.OrdinalIgnoreCase)) sb.AppendLine(" log-adjacency-changes");
                sb.AppendLine("!");

                foreach (var row in Rows(V("ospfInterfaceAreaList")))
                {
                    var p = Parts(row);
                    if (p.Length < 2) continue;
                    var localIf = p[0];
                    var area = p[1];
                    var localAddress = addresses.FirstOrDefault(x => x.Name.Equals(localIf, StringComparison.OrdinalIgnoreCase));
                    sb.AppendLine($"! Gegenstelle zur lokalen Schnittstelle {localIf}");
                    sb.AppendLine("interface <GEGENSTELLEN-INTERFACE>");
                    if (localAddress != null)
                    {
                        var peer = SuggestPeerAddress(localAddress.Address, localAddress.Mask);
                        if (peer != null) sb.AppendLine($" ip address {peer} {localAddress.Mask}");
                    }
                    sb.AppendLine($" ip ospf {pid} area {area}");
                    var settings = FindByFirstColumn(V("ospfIfSettings"), localIf);
                    if (settings.Length >= 4 && !string.IsNullOrWhiteSpace(settings[3]))
                        sb.AppendLine($" ip ospf network {settings[3]}");
                    sb.AppendLine(" no shutdown");
                    sb.AppendLine("!");
                }

                foreach (var row in Rows(V("ospfNetList")))
                {
                    var p = Parts(row);
                    if (p.Length >= 3)
                        sb.AppendLine($"! Prozessbasiert: Die Gegenstelle muss ihr eigenes Transitnetz mit passender Wildcard in Area {p[2]} aktivieren.");
                }

                foreach (var row in Rows(V("ospfTimerList")))
                {
                    var p = Parts(row);
                    if (p.Length >= 3)
                        sb.AppendLine($"! Timer müssen auf der Gegenstelle zu {p[0]} passen: Hello {p[1]}, Dead {p[2]}.");
                }
                foreach (var row in Rows(V("ospfAuthList")))
                {
                    var p = Parts(row);
                    if (p.Length >= 3)
                        sb.AppendLine($"! OSPF-Authentifizierung muss auf der Gegenstelle zu {p[0]} mit gleichem Typ und Schlüssel konfiguriert werden.");
                }
            });
        }

        if (M("ospfv3") && !string.IsNullOrWhiteSpace(V("ospfv3IfList")))
        {
            Section("OSPFV3-GEGENSTELLE", () =>
            {
                var process = string.IsNullOrWhiteSpace(V("ospfv3Process")) ? "1" : V("ospfv3Process");
                sb.AppendLine("ipv6 unicast-routing");
                sb.AppendLine($"ipv6 router ospf {process}");
                sb.AppendLine(" router-id <EINDEUTIGE-GEGENSTELLEN-ROUTER-ID>");
                sb.AppendLine("!");
                foreach (var row in Rows(V("ospfv3IfList")))
                {
                    var p = Parts(row);
                    if (p.Length < 2) continue;
                    sb.AppendLine($"! Gegenstelle zur lokalen Schnittstelle {p[0]}");
                    sb.AppendLine("interface <GEGENSTELLEN-INTERFACE>");
                    sb.AppendLine(" ipv6 enable");
                    sb.AppendLine($" ipv6 ospf {process} area {p[1]}");
                    sb.AppendLine(" no shutdown");
                    sb.AppendLine("!");
                }
                sb.AppendLine("! Area, Hello-/Dead-Timer, Network Type und Authentifizierung müssen kompatibel sein.");
            });
        }

        if (M("ipv6RoutingProtocols") && !string.IsNullOrWhiteSpace(V("ipv6BgpList")))
        {
            Section("BGP-IPV6-GEGENSTELLE", () =>
            {
                foreach (var row in Rows(V("ipv6BgpList")))
                {
                    var p = Parts(row);
                    if (p.Length < 4) continue;
                    sb.AppendLine($"router bgp {p[2]}");
                    sb.AppendLine(" address-family ipv6 unicast");
                    sb.AppendLine($"  neighbor <LOKALE-IPV6-BGP-QUELLADRESSE> remote-as {p[0]}");
                    sb.AppendLine("  neighbor <LOKALE-IPV6-BGP-QUELLADRESSE> activate");
                    sb.AppendLine(" exit-address-family");
                    sb.AppendLine("!");
                    sb.AppendLine($"! Lokaler Neighbor-Eintrag zeigt auf {p[1]}; die Gegenstelle benötigt eine Rückroute zur lokalen BGP-Quelladresse.");
                    sb.AppendLine("!");
                }
            });
        }

        if (M("bgp") && !string.IsNullOrWhiteSpace(V("bgpNeighborList")))
        {
            Section("BGP-GEGENSTELLE", () =>
            {
                var localAs = V("bgpAs");
                foreach (var row in Rows(V("bgpNeighborList")))
                {
                    var p = Parts(row);
                    if (p.Length < 2) continue;
                    var peerIp = p[0];
                    var peerAs = p[1];
                    var localSource = FindLocalSourceForPeer(peerIp, addresses) ?? "<LOKALE-QUELL-IP-DIE-DER-PEER-ERREICHT>";
                    sb.AppendLine($"router bgp {peerAs}");
                    sb.AppendLine($" neighbor {localSource} remote-as {localAs}");
                    var ext = FindByFirstColumn(V("bgpExtNeighborList"), peerIp);
                    if (ext.Length >= 5 && !string.IsNullOrWhiteSpace(ext[4]))
                        sb.AppendLine($" neighbor {localSource} password {ext[4]}");
                    if (ext.Length >= 3 && int.TryParse(ext[2], out var multihop) && multihop > 1)
                        sb.AppendLine($" neighbor {localSource} ebgp-multihop {multihop}");
                    sb.AppendLine("!");
                    sb.AppendLine("! Die Gegenstelle benötigt eine Rückroute zur verwendeten lokalen Update-Source.");
                    sb.AppendLine("!");
                }
            });
        }

        if (M("fhrp") && !string.IsNullOrWhiteSpace(V("hsrpList")))
        {
            Section("HSRP-GEGENSTELLE", () =>
            {
                foreach (var row in Rows(V("hsrpList")))
                {
                    var p = Parts(row);
                    if (p.Length < 3) continue;
                    var localPriority = int.TryParse(p.ElementAtOrDefault(3), out var priority) ? priority : 110;
                    var peerPriority = Math.Clamp(localPriority - 10, 1, 255);
                    sb.AppendLine($"interface <GEGENSTELLEN-INTERFACE-FÜR-{p[0]}> ");
                    sb.AppendLine(" ip address <ANDERE-REALE-IP-IM-GLEICHEN-SUBNETZ> <MASKE>");
                    sb.AppendLine($" standby {p[1]} ip {p[2]}");
                    sb.AppendLine($" standby {p[1]} priority {peerPriority}");
                    if (p.ElementAtOrDefault(4)?.Contains("preempt", StringComparison.OrdinalIgnoreCase) == true)
                        sb.AppendLine($" standby {p[1]} preempt");
                    sb.AppendLine("!");
                    sb.AppendLine("! Gruppe und virtuelle IP müssen identisch sein; die reale Interface-IP muss unterschiedlich sein.");
                    sb.AppendLine("!");
                }
            });
        }

        if (M("vpn") && !string.IsNullOrWhiteSpace(V("greList")))
        {
            Section("GRE-GEGENSTELLE", () =>
            {
                foreach (var row in Rows(V("greList")))
                {
                    var p = Parts(row);
                    if (p.Length < 5) continue;
                    sb.AppendLine($"interface Tunnel{p[0]}");
                    if (IPAddress.TryParse(p[1], out var tunnelIp) && IPAddress.TryParse(p[2], out var tunnelMask))
                    {
                        var peerTunnelIp = SuggestPeerAddress(tunnelIp, tunnelMask);
                        if (peerTunnelIp != null) sb.AppendLine($" ip address {peerTunnelIp} {p[2]}");
                    }
                    else sb.AppendLine(" ip address <GEGENSTELLEN-TUNNEL-IP> <MASKE>");
                    sb.AppendLine(" tunnel source <GEGENSTELLEN-WAN-INTERFACE-ODER-IP>");
                    var localSourceAddress = ResolveInterfaceOrAddress(p[3], addresses);
                    sb.AppendLine($" tunnel destination {localSourceAddress ?? "<LOKALE-WAN-IP>"}");
                    sb.AppendLine(" no shutdown");
                    sb.AppendLine("!");
                }
            });
        }

        if (M("greIpsec") && !string.IsNullOrWhiteSpace(V("greIpsecList")))
        {
            Section("GRE-OVER-IPSEC-GEGENSTELLE", () =>
            {
                foreach (var row in Rows(V("greIpsecList")))
                {
                    var p = Parts(row);
                    if (p.Length < 9) continue;
                    var localOutside = ResolveInterfaceOrAddress(p[3], addresses) ?? "<LOKALE-WAN-IP>";
                    sb.AppendLine($"! Remote Peer-Adresse auf der Gegenstelle: {localOutside}");
                    sb.AppendLine($"! Gleicher PSK: {p[5]}");
                    sb.AppendLine($"! Gleicher Transform-Set-Name/Inhalt: {p[8]}");
                    sb.AppendLine($"! Crypto-ACL {p[6]} muss gespiegelt werden: Quelle und Ziel werden vertauscht.");
                    sb.AppendLine($"! Crypto-Map {p[7]} wird auf das Remote-WAN-Interface angewendet.");
                    sb.AppendLine("!");
                }
            });
        }

        if (M("vpnAdvanced") && !string.IsNullOrWhiteSpace(V("ipsecS2SList")))
        {
            Section("SITE-TO-SITE-IPSEC-GEGENSTELLE", () =>
            {
                foreach (var row in Rows(V("ipsecS2SList")))
                {
                    var p = Parts(row);
                    if (p.Length < 6) continue;
                    var localOutside = ResolveInterfaceOrAddress(p[2], addresses) ?? "<LOKALE-WAN-IP>";
                    sb.AppendLine($"! Gegenstellen-Peer muss auf {localOutside} zeigen.");
                    sb.AppendLine($"! PSK muss identisch sein: {p[1]}");
                    sb.AppendLine($"! Crypto-ACL {p[3]} muss Quelle und Ziel gegenüber der lokalen ACL vertauschen.");
                    sb.AppendLine($"! Transform-Set {p[4]} und relevante IKE/IPsec-Parameter müssen kompatibel sein.");
                    sb.AppendLine($"! Crypto-Map {p[5]} wird auf das externe Gegenstellen-Interface angewendet.");
                    sb.AppendLine("!");
                }
            });
        }

        if (M("mpls") && !string.IsNullOrWhiteSpace(V("mplsInterfaceList")))
        {
            Section("MPLS-/LDP-GEGENSTELLE", () =>
            {
                if (V("mplsCef").Equals("Ja", StringComparison.OrdinalIgnoreCase)) sb.AppendLine("ip cef");
                if (V("mplsLabelProtocol").Equals("ldp", StringComparison.OrdinalIgnoreCase)) sb.AppendLine("mpls label protocol ldp");
                sb.AppendLine("mpls ldp router-id <EINDEUTIGE-LOOPBACK> force");
                foreach (var row in Rows(V("mplsInterfaceList")))
                {
                    var p = Parts(row);
                    if (p.Length < 1) continue;
                    sb.AppendLine("interface <GEGENSTELLEN-CORE-INTERFACE>");
                    sb.AppendLine(" mpls ip");
                    if (p.ElementAtOrDefault(3)?.Equals("Ja", StringComparison.OrdinalIgnoreCase) == true)
                        sb.AppendLine(" mpls ldp igp sync");
                    sb.AppendLine(" no shutdown");
                    sb.AppendLine("!");
                }
                sb.AppendLine("! IGP-Erreichbarkeit der LDP-Router-IDs muss in beide Richtungen bestehen.");
            });
        }

        if (M("mpls") && !string.IsNullOrWhiteSpace(V("mplsVpnv4Peers")))
        {
            Section("MP-BGP-VPNV4-GEGENSTELLE", () =>
            {
                var localAs = V("mplsBgpAs");
                var updateSource = V("mplsVpnv4UpdateSource");
                var localSource = ResolveInterfaceOrAddress(updateSource, addresses) ?? "<LOKALE-VPNV4-UPDATE-SOURCE-IP>";
                foreach (var row in Rows(V("mplsVpnv4Peers")))
                {
                    var p = Parts(row);
                    if (p.Length < 2) continue;
                    sb.AppendLine($"router bgp {p[1]}");
                    sb.AppendLine($" neighbor {localSource} remote-as {localAs}");
                    sb.AppendLine($" neighbor {localSource} update-source <GEGENSTELLEN-LOOPBACK>");
                    sb.AppendLine(" address-family vpnv4");
                    sb.AppendLine($"  neighbor {localSource} activate");
                    sb.AppendLine($"  neighbor {localSource} send-community extended");
                    sb.AppendLine(" exit-address-family");
                    sb.AppendLine("!");
                }
            });
        }

        if (sectionCount == 0)
        {
            sb.AppendLine();
            sb.AppendLine("! Für die aktuell aktiven Module wurde keine protokollspezifische Gegenstellenkonfiguration erkannt.");
            sb.AppendLine("! Aktiviere beispielsweise Trunk, EtherChannel, OSPF, BGP, HSRP, GRE/IPsec, MST oder MPLS.");
        }

        sb.AppendLine();
        sb.AppendLine("! PRÜFPUNKTE VOR INBETRIEBNAHME");
        sb.AppendLine("! - Interface-Adressen und Subnetzmasken müssen zur gleichen Verbindung gehören.");
        sb.AppendLine("! - VLANs, Native VLAN, EtherChannel-Parameter und MST-Region müssen konsistent sein.");
        sb.AppendLine("! - Routing-Protokoll, Area/AS, Timer und Authentifizierung müssen kompatibel sein.");
        sb.AppendLine("! - Firewall-/ACL-Regeln müssen den Kontroll- und Nutzdatenverkehr in beide Richtungen zulassen.");
        return sb.ToString().TrimEnd();
    }

    private static List<InterfaceAddress> CollectInterfaceAddresses(Func<string, string> value)
    {
        var result = new List<InterfaceAddress>();

        foreach (var row in Rows(value("interfacesList")))
        {
            var p = Parts(row);
            if (p.Length >= 5 && p[2].Equals("routed", StringComparison.OrdinalIgnoreCase))
                AddAddress(result, p[0], p[3], p[4]);
        }

        foreach (var row in Rows(value("subifList")))
        {
            var p = Parts(row);
            if (p.Length >= 5) AddAddress(result, $"{p[0]}.{p[1]}", p[3], p[4]);
        }

        foreach (var row in Rows(value("sviList")))
        {
            var p = Parts(row);
            if (p.Length >= 3) AddAddress(result, $"Vlan{p[0]}", p[1], p[2]);
        }

        foreach (var row in Rows(value("vrfSviList")))
        {
            var p = Parts(row);
            if (p.Length >= 4) AddAddress(result, $"Vlan{p[0]}", p[2], p[3]);
        }

        foreach (var row in Rows(value("interfaceRoleList")))
        {
            var p = Parts(row);
            if (p.Length >= 10) AddAddress(result, p[0], p[8], p[9]);
        }

        return result;
    }

    private static void AddAddress(List<InterfaceAddress> list, string name, string ipText, string maskText)
    {
        if (!IPAddress.TryParse(ipText, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return;
        if (!IPAddress.TryParse(maskText, out var mask) || mask.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return;
        list.Add(new InterfaceAddress(name, ip, mask));
    }

    private static IPAddress? SuggestPeerAddress(IPAddress local, IPAddress mask)
    {
        var localValue = ToUInt32(local);
        var maskValue = ToUInt32(mask);
        var network = localValue & maskValue;
        var broadcast = network | ~maskValue;
        if (broadcast <= network + 1) return null;

        uint candidate;
        if (localValue == network + 1) candidate = network + 2;
        else candidate = localValue - 1;

        if (candidate <= network || candidate >= broadcast) return null;
        return FromUInt32(candidate);
    }

    private static string? FindLocalSourceForPeer(string peerText, IReadOnlyList<InterfaceAddress> addresses)
    {
        if (!IPAddress.TryParse(peerText, out var peer) || peer.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
        var peerValue = ToUInt32(peer);
        foreach (var item in addresses)
        {
            var mask = ToUInt32(item.Mask);
            if ((ToUInt32(item.Address) & mask) == (peerValue & mask)) return item.Address.ToString();
        }
        return null;
    }

    private static string? ResolveInterfaceOrAddress(string value, IReadOnlyList<InterfaceAddress> addresses)
    {
        if (IPAddress.TryParse(value, out var parsed)) return parsed.ToString();
        return addresses.FirstOrDefault(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase))?.Address.ToString();
    }

    private static string[] FindByFirstColumn(string value, string key)
    {
        foreach (var row in Rows(value))
        {
            var p = Parts(row);
            if (p.Length > 0 && p[0].Equals(key, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return Array.Empty<string>();
    }

    private static IEnumerable<string> Rows(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0);

    private static string[] Parts(string row) => row.Split('|').Select(x => x.Trim()).ToArray();

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static IPAddress FromUInt32(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new IPAddress(bytes);
    }
}
