using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CiscoConfigGuiWpf;

public static partial class TooltipBuilder
{
    public static ToolTip CreateFieldTip(FieldDefinition field)
    {
        var text = Expand(field.Name, field.Label, field.Help);
        text += BuildFieldSyntaxHint(field);
        return Create(field.Label, text);
    }

    public static ToolTip CreateModuleTip(ModuleDefinition module)
    {
        var text = ModuleHelp(module);
        return Create(module.Title, text);
    }

    public static ToolTip Create(string title, string body)
    {
        var panel = new StackPanel { MaxWidth = 760, Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            Margin = new Thickness(0, 0, 0, 10),
            Opacity = 0.65
        });
        panel.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            FontSize = 13,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap
        });
        return new ToolTip
        {
            Content = panel,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            HasDropShadow = true
        };
    }

    private static string ModuleHelp(ModuleDefinition module)
    {
        var specific = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["basic"] = "Zweck:\nGrundidentität, Basisschutz und Zeitzone des Geräts.\n\nErzeugte Befehle:\nhostname, ip domain-name, enable secret, no ip domain-lookup, service password-encryption, clock timezone und clock summer-time.\n\nHinweis:\nDomain ist besonders für SSH/RSA-Key wichtig. Clock-Einstellungen liegen bewusst im Basisbereich.",
            ["routingBase"] = "Zweck:\nAllgemeine globale Routingfunktionen konfigurieren.\n\nErzeugte Befehle:\nip routing oder no ip routing.\n\nHinweis:\nDer Befehl ist besonders auf Layer-3-Switches relevant und wird beim Import nicht mehr als Zusatzbefehl behandelt.",
            ["ssh"] = "Zweck:\nSSH-Zugriff und line vty zentral konfigurieren.\n\nErzeugte Befehle:\ncrypto key generate rsa, ip ssh version 2, line vty, login local/AAA, transport input ssh, exec-timeout.\n\nWichtig:\nVTY wird nur hier erzeugt, damit keine doppelte line-vty-Konfiguration entsteht.",
            ["aaa"] = "Zweck:\nAuthentifizierung und Autorisierung vorbereiten.\n\nErzeugte Befehle:\naaa new-model, aaa authentication login default, aaa authorization exec default.\n\nHinweis:\nBei RADIUS/TACACS lokale Benutzer als Fallback einplanen.",
            ["vlans"] = "Zweck:\nVLANs und SVIs ohne VRF konfigurieren.\n\nErzeugte Befehle:\nvlan, name, interface vlan, ip address, ipv6 address.\n\nHinweis:\nIm VRF-Modus stattdessen VLAN / SVI VRF nutzen.",
            ["vrfDefs"] = "Zweck:\nVRFs als getrennte Routingtabellen definieren.\n\nErzeugte Befehle:\nip vrf <Name>, rd <ASN:Nummer>, description.\n\nHinweis:\nAndere VRF-Module referenzieren diese Namen exakt.",
            ["vrfSvi"] = "Zweck:\nSVIs einer VRF zuordnen.\n\nErzeugte Befehle:\ninterface vlan, ip vrf forwarding, ip address, ipv6 address.\n\nWichtig:\nip vrf forwarding muss vor ip address stehen.",
            ["vrfStaticRoutes"] = "Zweck:\nStatische IPv4- und IPv6-Routen pro VRF erzeugen.\n\nErzeugte Befehle:\nip route vrf ..., ipv6 route vrf ...",
            ["ospf"] = "Zweck:\nOSPFv2 ohne VRF für IPv4 konfigurieren.\n\nErzeugte Befehle:\nrouter ospf, router-id, log-adjacency-changes, network ... area ... oder interfacebasiert ip ospf <Prozess> area <Area>.\n\nHinweis:\nDie Konfigurationsart kann prozessbasiert, interfacebasiert oder gemischt gewählt werden.",
            ["stpExtended"] = "Zweck:\nDynamische Spanning-Tree-Konfiguration für PVST+, Rapid-PVST+ und MST. Nach Auswahl des Modus werden nur gültige Eingabebereiche angezeigt und exportiert.\n\nErzeugte Befehle:\nGlobale STP-Optionen, VLAN-Root/Priority/Timer, Interfaceprofile, PortFast, BPDU Guard/Filter, Root/Loop Guard, Link-Type, Cost, Port-Priority, MST-Region/Instanzen, Errdisable-Recovery und Rücksetzbefehle.\n\nPrüfungen:\nPrioritätsraster, VLAN-/MST-Zuordnungen, Guard-Konflikte, Port-Channel-Konsistenz, MST-Regionsvergleich und Plattformhinweise.\n\nLive-Vorschau:\nDer erzeugte STP-Abschnitt wird direkt im Modul angezeigt.",
            ["bgp"] = "Zweck:\nBGP ohne VRF für IPv4/IPv6 konfigurieren.\n\nErzeugte Befehle:\nrouter bgp, neighbor remote-as, network, address-family ipv6.",
            ["nat"] = "Zweck:\nNAT/PAT auf Routern konfigurieren.\n\nErzeugte Befehle:\nip nat inside/outside, ip nat inside source ...\n\nWichtig:\nInside/Outside Interfaces müssen korrekt gesetzt sein."
        };

        var baseText = specific.TryGetValue(module.Name, out var text)
            ? text
            : $"Abschnitt:\n{module.Title}\n\nZweck:\nDieses Modul erzeugt Cisco IOS/IOS-XE-Konfigurationsbefehle für diesen Bereich.\n\nBedienung:\nLinks aktivieren, Felder ausfüllen und anschließend über Kopieren oder TXT Export die Konfiguration erzeugen.\n\nHinweis:\nDropdowns begrenzen feste Auswahlwerte. IPs, Interfaces, VLAN-Listen, ACL-Namen und Beschreibungen bleiben Freitext.";

        return (baseText.Trim() + BuildModuleCommandDetails(module)).Trim();
    }

    private static string Expand(string name, string label, string help)
    {
        var text = string.IsNullOrWhiteSpace(help)
            ? "Keine Detailbeschreibung hinterlegt. Feld nur ausfüllen, wenn dieser Befehl für die Zielkonfiguration benötigt wird."
            : help.Trim();

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        if (!Regex.IsMatch(text, @"(^|\n)Zweck:", RegexOptions.IgnoreCase))
            text = "Zweck:\n" + text;

        if (label.Contains('|'))
        {
            text += "\n\nFormat:\n" + label.Replace("|", " | ");
            text += "\n\nTrennzeichen:\nJede Zeile ist ein eigener Eintrag. Spalten werden mit | getrennt.";
        }

        text = text.Replace(". Befehl:", ".\n\nBefehl:")
                   .Replace(". Befehle:", ".\n\nBefehle:")
                   .Replace(". Befehlsteile:", ".\n\nBefehlsteile:")
                   .Replace(". Format:", ".\n\nFormat:")
                   .Replace(". Beispiel:", ".\n\nBeispiel:")
                   .Replace(". Hinweis:", ".\n\nHinweis:")
                   .Replace(". Empfehlung:", ".\n\nEmpfehlung:");

        return text.Trim();
    }

    private static string BuildModuleCommandDetails(ModuleDefinition module)
    {
        var rows = GetRelatedCommandRows(module).ToList();
        if (rows.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Mögliche Befehle / Syntax:");

        foreach (var row in rows.Take(14))
            sb.AppendLine($"• {row.Command} — {row.Meaning}");

        if (rows.Count > 14)
            sb.AppendLine($"• ... weitere {rows.Count - 14} Befehle im Tab Befehle.");

        var parameterLines = rows
            .SelectMany(r => ExtractParameters(r.Command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .Select(p => $"• <{p}> = {ExplainParameter(p)}")
            .ToList();

        if (parameterLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Befehlsteile:");
            foreach (var line in parameterLines)
                sb.AppendLine(line);
        }

        var optionalParts = rows
            .SelectMany(r => ExtractOptionalParts(r.Command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        if (optionalParts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Optionale Teile:");
            foreach (var part in optionalParts)
                sb.AppendLine($"• [{part}] = optionaler Befehlsanteil; nur setzen, wenn benötigt oder vom IOS unterstützt.");
        }

        return sb.ToString();
    }

    private static string BuildFieldSyntaxHint(FieldDefinition field)
    {
        var placeholder = GuessPlaceholder(field);
        if (placeholder.Length == 0) return string.Empty;

        var rows = ConfigurationCommandCatalog.All
            .SelectMany(g => g.Rows)
            .Where(r => r.Command.Contains("<" + placeholder + ">", StringComparison.OrdinalIgnoreCase)
                     || ExtractParameters(r.Command).Any(p => SameParameter(p, placeholder)))
            .Take(5)
            .ToList();

        if (rows.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Befehlsteile:");
        sb.AppendLine($"• <{placeholder}> = {ExplainParameter(placeholder)}");
        sb.AppendLine();
        sb.AppendLine("Beispiel-Syntax:");
        foreach (var row in rows)
            sb.AppendLine($"• {row.Command}");
        return sb.ToString();
    }

    private static IEnumerable<CommandRow> GetRelatedCommandRows(ModuleDefinition module)
    {
        var aliases = GetCommandAliases(module);
        return ConfigurationCommandCatalog.All
            .SelectMany(g => g.Rows)
            .Where(r => aliases.Any(a => r.Module.Equals(a, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(r => r.Command, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First());
    }

    private static string[] GetCommandAliases(ModuleDefinition module)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["basic"] = new[] { "Grunddaten", "Abschluss" },
            ["banner"] = new[] { "Banner" },
            ["ssh"] = new[] { "SSH", "VTY" },
            ["aaa"] = new[] { "AAA", "Console" },
            ["radiusTacacs"] = new[] { "RADIUS", "TACACS+", "AAA" },
            ["monitoringBase"] = new[] { "NTP", "Syslog", "SNMP", "Archive", "Login-Audit" },
            ["userRights"] = new[] { "Benutzer", "Parser View" },
            ["interfaces"] = new[] { "Interface" },
            ["ranges"] = new[] { "Interface" },
            ["subinterfaces"] = new[] { "Subinterface" },
            ["routerStick"] = new[] { "Subinterface", "VLAN" },
            ["qosBasic"] = new[] { "QoS" },
            ["interfaceProfiles"] = new[] { "Interface", "Switchport", "QoS", "Port-Security", "STP" },
            ["trunkUplink"] = new[] { "Switchport", "VLAN" },
            ["etherChannelExt"] = new[] { "EtherChannel", "Switchport" },
            ["interfaceRoles"] = new[] { "Switchport", "STP", "QoS" },
            ["voip"] = new[] { "VoIP", "DHCP", "Switchport", "QoS", "STP" },
            ["qinq"] = new[] { "QinQ", "Switchport", "Subinterface", "VLAN" },
            ["vlans"] = new[] { "VLAN", "SVI", "Switch" },
            ["switchSec"] = new[] { "DHCP Snooping", "DAI" },
            ["accessBaseline"] = new[] { "Switchport", "Port-Security", "STP", "Storm-Control" },
            ["errdisableRecovery"] = new[] { "Errdisable" },
            ["stpExtended"] = new[] { "STP" },
            ["switchSecExt"] = new[] { "DHCP Snooping", "DAI", "IPSG" },
            ["portSecurityExt"] = new[] { "Port-Security" },
            ["vlanIpPlan"] = new[] { "VLAN", "SVI" },
            ["staticRoutes"] = new[] { "Static Route", "Backup Route", "Tracked Route" },
            ["ospf"] = new[] { "OSPF" },
            ["isis"] = new[] { "IS-IS" },
            ["bgp"] = new[] { "BGP" },
            ["fhrp"] = new[] { "FHRP" },
            ["vrfLite"] = new[] { "VRF-Lite", "VRF", "VRF Interface", "VRF Route" },
            ["routeMapFilter"] = new[] { "Route-Map", "Prefix-List" },
            ["ipSlaTracking"] = new[] { "Tracked Route", "Backup Route" },
            ["ospfAdvanced"] = new[] { "OSPF" },
            ["bgpAdvanced"] = new[] { "BGP", "IPv6 BGP" },
            ["mpls"] = new[] { "MPLS" },
            ["vrfDefs"] = new[] { "VRF" },
            ["vrfSvi"] = new[] { "VRF Interface", "SVI" },
            ["vrfStaticRoutes"] = new[] { "VRF Route" },
            ["vrfOspf"] = new[] { "VRF OSPF" },
            ["vrfOspfv3"] = new[] { "VRF OSPFv3" },
            ["vrfBgp"] = new[] { "VRF BGP" },
            ["ipv6"] = new[] { "IPv6", "IPv6 RA" },
            ["dhcp"] = new[] { "DHCP", "DHCPv6 Relay" },
            ["acl"] = new[] { "ACL" },
            ["ipv6Extended"] = new[] { "IPv6 Route", "IPv6 ACL", "IPv6 RA", "DHCPv6 Relay" },
            ["ospfv3"] = new[] { "OSPFv3" },
            ["ipv6RoutingProtocols"] = new[] { "IPv6 BGP", "IPv6 EIGRP", "IPv6 IS-IS" },
            ["aclAssistant"] = new[] { "ACL" },
            ["security"] = new[] { "Login-Audit", "Switch" },
            ["nat"] = new[] { "NAT", "NAT Exempt" },
            ["vpn"] = new[] { "GRE" },
            ["customCommands"] = new[] { "Eigene Befehle" },
            ["greIpsec"] = new[] { "GRE", "IPsec" },
            ["wanFailover"] = new[] { "Tracked Route", "Backup Route" },
            ["zoneFirewall"] = new[] { "Zone Firewall" },
            ["dmzAssistant"] = new[] { "DMZ", "ACL", "NAT" },
            ["vpnAdvanced"] = new[] { "IPsec" }
        };

        if (map.TryGetValue(module.Name, out var aliases))
            return aliases;

        return new[] { module.Title };
    }

    private static IEnumerable<string> ExtractParameters(string command)
    {
        foreach (Match match in Regex.Matches(command, "<([^>]+)>"))
            yield return match.Groups[1].Value.Trim();
    }

    private static IEnumerable<string> ExtractOptionalParts(string command)
    {
        foreach (Match match in Regex.Matches(command, @"\[([^\]]+)\]"))
            yield return match.Groups[1].Value.Trim();
    }

    private static string GuessPlaceholder(FieldDefinition field)
    {
        var name = field.Name.ToLowerInvariant();
        var label = field.Label.ToLowerInvariant();

        if (name.Contains("hostname") || label.Contains("hostname")) return "Hostname";
        if (name == "domain" || label.Contains("domain")) return "Domain";
        if (name.Contains("secret") || label.Contains("secret")) return "Secret";
        if (name.Contains("user") || label.Contains("benutzer")) return "User";
        if (name.Contains("password") || label.Contains("passwort")) return "Passwort";
        if (name.Contains("vlan") && (name.Contains("list") || label.Contains("liste"))) return "VLAN-Liste";
        if (name.Contains("vlan")) return "VLAN-ID";
        if (name.Contains("vrf")) return "VRF";
        if (name.Contains("ospf") && (name.Contains("pid") || name.Contains("process") || label.Contains("prozess"))) return "OSPF-Prozess";
        if (name.Contains("ospf") && name.Contains("rid")) return "Router-ID";
        if (name.Contains("bgp") && name.Contains("as")) return "AS";
        if (name.Contains("rid") || label.Contains("router-id")) return "Router-ID";
        if (name.Contains("acl")) return "ACL-Name";
        if (name.Contains("route") && name.Contains("map")) return "Route-Map";
        if (name.Contains("prefix")) return "Prefix-List";
        if (name.Contains("source") || name.Contains("src")) return "Quelle";
        if (name.Contains("dst") || name.Contains("destination")) return "Ziel";
        if (name.Contains("port")) return "Port";
        if (name.Contains("protocol") || label.Contains("protokoll")) return "Protokoll";
        if (name.Contains("if") || label.Contains("interface")) return "Interface";
        if (name.Contains("ip") || label.Contains("ip")) return "IP";
        if (name.Contains("mask") || label.Contains("maske")) return "Maske";
        if (name.Contains("wildcard")) return "Wildcard";
        if (name.Contains("area")) return "Area";
        if (name.Contains("zone")) return "Zone";
        if (name.Contains("pool")) return "Pool-Name";
        if (name.Contains("policy")) return "Policy-Map";
        if (name.Contains("class")) return "Class-Map";
        if (name.Contains("seconds") || label.Contains("sek")) return "Sekunden";
        if (name.Contains("timeout")) return "Sekunden";
        if (name.Contains("level")) return "Level";
        if (name.Contains("name")) return "Name";

        return string.Empty;
    }

    private static bool SameParameter(string a, string b) => NormalizeParameter(a) == NormalizeParameter(b);

    private static string NormalizeParameter(string text)
    {
        return Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9äöüß]+", "");
    }

    private static string ExplainParameter(string parameter)
    {
        var p = parameter.Trim();
        var n = NormalizeParameter(p);

        if (p.Contains('|'))
            return "Auswahlwert. Einer der angegebenen Werte wird eingesetzt, z. B. " + p.Replace("|", " oder ") + ".";

        if (n.Contains("hostname")) return "Gerätename im Cisco-Prompt und in Logs.";
        if (n.Contains("domain")) return "DNS-Domainname; relevant für SSH/RSA-Schlüssel.";
        if (n.Contains("secret") || n.Contains("psk") || n.Contains("sharedsecret")) return "geheimer Schlüssel oder Passwort; nicht im Klartext dokumentieren.";
        if (n.Contains("password") || n.Contains("passwort")) return "Passwortwert für Login, Line oder Parser View.";
        if (n == "user" || n.Contains("username")) return "lokaler Benutzername oder Accountname.";
        if (n.Contains("interface") || n == "if" || n.EndsWith("if")) return "Cisco-Interface, z. B. GigabitEthernet0/1, Vlan99 oder Loopback0.";
        if (n.Contains("interfacerange")) return "Interfacebereich, z. B. Gi0/1-24 oder Fa0/1,Fa0/3.";
        if (n.Contains("ip") || n.Contains("peer") || n.Contains("next") || n.Contains("gateway") || n.Contains("server")) return "IPv4-/IPv6-Adresse oder erreichbarer Next-Hop, abhängig vom Befehl.";
        if (n.Contains("netz") || n.Contains("network") || n.Contains("zielnetz")) return "Netzadresse, z. B. 192.168.10.0 oder 2001:db8::/64.";
        if (n.Contains("maske") || n.Contains("mask")) return "Subnetzmaske, z. B. 255.255.255.0.";
        if (n.Contains("wildcard")) return "OSPF-/ACL-Wildcard, z. B. 0.0.0.255.";
        if (n.Contains("vlanliste")) return "VLAN-Liste, z. B. 10,20,30 oder 10-20.";
        if (n.Contains("voicevlan")) return "Voice-VLAN für IP-Telefone; wird mit switchport voice vlan gesetzt.";
        if (n.Contains("datavlan")) return "Daten-VLAN für Clients/PCs hinter dem Telefon.";
        if (n.Contains("providervlan")) return "äußeres Service-/Provider-VLAN bei QinQ.";
        if (n.Contains("customervlan")) return "inneres Kunden-VLAN bei QinQ.";
        if (n.Contains("tftp")) return "TFTP- oder CallManager-IP für Telefon-Provisionierung.";
        if (n.Contains("nativevlan")) return "Native VLAN auf einem 802.1Q-Trunk.";
        if (n.Contains("vlan")) return "VLAN-ID im Bereich 1–4094, abhängig von Plattform und Design.";
        if (n.Contains("vrf")) return "Name der VRF/Routingtabelle; muss exakt zur VRF-Definition passen.";
        if (n.Contains("rd")) return "Route Distinguisher, meistens im Format ASN:Nummer.";
        if (n.Contains("routetarget") || n == "rt") return "Route-Target/Extended Community, meistens im Format ASN:Nummer.";
        if (n.Contains("neighbor")) return "Nachbaradresse, z. B. LDP- oder BGP-Peer-IP.";
        if (n.Contains("label")) return "MPLS Label oder Label-Protokoll, abhängig vom Befehl.";
        if (n == "as" || n.Contains("asn") || n.Contains("remoteas")) return "BGP Autonomous-System-Nummer.";
        if (n.Contains("ospfprozess")) return "lokale OSPF-Prozess-ID; muss nicht zwischen Geräten identisch sein.";
        if (n.Contains("routerid")) return "eindeutige Router-ID im Format einer IPv4-Adresse.";
        if (n.Contains("area")) return "OSPF-Area, z. B. 0 oder 0.0.0.0.";
        if (n.Contains("acl")) return "ACL-Name oder ACL-Nummer, die vorher erstellt oder referenziert wird.";
        if (n.Contains("routemap")) return "Name einer Route-Map für Policy Routing, NAT-Ausnahme oder Routingfilter.";
        if (n.Contains("prefixlist")) return "Name einer Prefix-List für Routingfilter.";
        if (n.Contains("port")) return "TCP-/UDP-Port oder Dienst, z. B. 22, 80, 443, 53.";
        if (n.Contains("protokoll") || n.Contains("protocol")) return "Protokollwert, z. B. ip, tcp, udp, icmp, ospf oder ein inspect-Protokoll.";
        if (n.Contains("quelle") || n.Contains("source") || n.Contains("src")) return "Quelladresse, Quellnetz oder Quellobjekt.";
        if (n.Contains("ziel") || n.Contains("destination") || n.Contains("dst")) return "Zieladresse, Zielnetz oder Zielobjekt.";
        if (n.Contains("zone")) return "Security-Zone der Zone-Based Firewall.";
        if (n.Contains("classmap")) return "Name einer Class-Map für QoS oder Firewall-Inspection.";
        if (n.Contains("policymap")) return "Name einer Policy-Map für QoS oder Firewall-Inspection.";
        if (n.Contains("transformset")) return "IPsec Transform-Set mit Verschlüsselungs-/Hash-Parametern.";
        if (n.Contains("pool")) return "Name eines DHCP- oder NAT-Pools.";
        if (n.Contains("sekunden") || n.Contains("seconds")) return "Zeitwert in Sekunden.";
        if (n.Contains("minuten") || n.Contains("minutes")) return "Zeitwert in Minuten.";
        if (n.Contains("bytes")) return "Speichergröße oder Puffergröße in Byte.";
        if (n.Contains("level")) return "Privilege-, Logging- oder Security-Level, abhängig vom Befehl.";
        if (n.Contains("prioritaet") || n.Contains("priority")) return "Priorität oder Sequenznummer; niedriger ist oft bevorzugt.";
        if (n.Contains("prozent") || n.Contains("percent")) return "Prozentwert für Bandbreiten- oder QoS-Reservierung.";
        if (n.Contains("dscp")) return "DSCP-Wert oder DSCP-Klasse für QoS-Matching.";
        if (n.Contains("beschreibung") || n.Contains("description")) return "frei wählbarer Beschreibungstext für Dokumentation.";
        if (n.Contains("name") || n.Contains("id") || n.Contains("nummer")) return "frei wählbarer Name, ID oder Nummer passend zum jeweiligen Modul.";

        return "Platzhalter. Dieser Wert wird durch deine Eingabe im Modul ersetzt.";
    }
}
