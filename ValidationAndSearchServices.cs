using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public static class DependencyValidationService
{
    private static readonly string[] RoutingModules = { "staticRoutes", "ospf", "ospfAdvanced", "isis", "bgp", "bgpAdvanced", "fhrp", "vrfOspf", "vrfOspfv3", "vrfBgp" };

    public static IReadOnlyList<DependencyFinding> Analyze(GenerationRequest request)
    {
        var findings = new List<DependencyFinding>();
        string V(string key) => request.Values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
        bool M(string key) => request.Modules.TryGetValue(key, out var active) && active;

        if (RoutingModules.Any(M) && V("deviceType").Equals("L3-Switch", StringComparison.OrdinalIgnoreCase) &&
            (!M("routingBase") || !V("ipRouting").Equals("Ja", StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("Fehler", "Routing", "Routingprotokolle sind aktiv, aber 'ip routing' ist auf dem Layer-3-Switch nicht aktiviert.", "enable-ip-routing", "routingBase", "ipRouting"));

        if (M("ssh"))
        {
            if (string.IsNullOrWhiteSpace(V("domain")))
                findings.Add(new("Fehler", "SSH", "Für die RSA-Schlüsselerzeugung fehlt der Domainname.", "set-default-domain", "basic", "domain"));

            var loginMode = V("vtyLoginMode");
            if (loginMode.Equals("login local", StringComparison.OrdinalIgnoreCase) &&
                (!M("userRights") || string.IsNullOrWhiteSpace(V("privUserList"))))
                findings.Add(new("Fehler", "VTY", "'login local' benötigt mindestens einen lokalen Benutzer.", "create-local-user", "userRights", "privUserList"));

            if (loginMode.Equals("password login", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(V("vtyPassword")))
                findings.Add(new("Fehler", "VTY", "Für 'password login' fehlt das VTY-Passwort.", "set-vty-password", "ssh", "vtyPassword"));

            if (loginMode.Equals("login authentication default", StringComparison.OrdinalIgnoreCase) &&
                !M("aaa") && !M("radiusTacacs"))
                findings.Add(new("Fehler", "AAA", "'login authentication default' benötigt eine AAA-Konfiguration.", "enable-aaa", "aaa", "aaaLocal"));
        }

        if (M("ospf"))
        {
            if (string.IsNullOrWhiteSpace(V("ospfRid")))
                findings.Add(new("Warnung", "OSPF", "Es ist keine feste OSPF Router-ID gesetzt.", "set-ospf-router-id", "ospf", "ospfRid"));
            if (string.IsNullOrWhiteSpace(V("ospfNetList")) && string.IsNullOrWhiteSpace(V("ospfInterfaceAreaList")))
                findings.Add(new("Fehler", "OSPF", "OSPF ist aktiv, aber es wurden weder Network-Statements noch Interface-Area-Zuordnungen definiert.", "", "ospf", "ospfConfigMode"));
        }

        if (M("bgp"))
        {
            if (string.IsNullOrWhiteSpace(V("bgpAs")))
                findings.Add(new("Fehler", "BGP", "Die lokale BGP-AS-Nummer fehlt.", "", "bgp", "bgpAs"));
            if (string.IsNullOrWhiteSpace(V("bgpNeighborList")) && string.IsNullOrWhiteSpace(V("bgpExtNeighborList")))
                findings.Add(new("Warnung", "BGP", "BGP enthält keine Nachbarn.", "", "bgp", "bgpNeighborList"));
        }

        if (M("dhcp") && string.IsNullOrWhiteSpace(V("dhcpPoolList")) && string.IsNullOrWhiteSpace(V("dhcpExtPoolList")))
            findings.Add(new("Warnung", "DHCP", "Das DHCP-Modul ist aktiv, enthält aber keinen Pool.", "", "dhcp", "dhcpPoolList"));

        var vlanIds = ParseVlanIds(V("vlanList")).ToHashSet();
        foreach (var trunkRow in Rows(V("trunkUplinkList")))
        {
            var parts = trunkRow.Split('|');
            if (parts.Length < 2) continue;
            foreach (var vlan in ExpandVlanExpression(parts[1]))
                if (vlanIds.Count > 0 && !vlanIds.Contains(vlan))
                    findings.Add(new("Warnung", "Trunk", $"VLAN {vlan} ist am Trunk {parts[0]} erlaubt, wurde aber nicht im VLAN-Modul angelegt.", "add-missing-vlans", "vlans", "vlanList"));
        }

        foreach (var sviRow in Rows(V("sviList")))
        {
            var parts = sviRow.Split('|');
            if (parts.Length == 0 || !int.TryParse(parts[0], out var vlan)) continue;
            if (vlanIds.Count > 0 && !vlanIds.Contains(vlan))
                findings.Add(new("Fehler", "SVI", $"Für SVI Vlan{vlan} fehlt die VLAN-Definition.", "add-missing-vlans", "vlans", "vlanList"));
        }

        if (M("stpExtended") && V("stpGlobalMode").Equals("mst", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(V("stpMstName")))
                findings.Add(new("Fehler", "MST", "Der MST-Regionsname fehlt.", "set-mst-name", "stpExtended", "stpMstName"));
            if (string.IsNullOrWhiteSpace(V("stpMstInstanceList")))
                findings.Add(new("Warnung", "MST", "Es wurde keine VLAN-zu-MST-Instanz-Zuordnung definiert.", "", "stpExtended", "stpMstInstanceList"));
        }

        if (M("fhrp") && string.IsNullOrWhiteSpace(V("hsrpList")))
            findings.Add(new("Warnung", "HSRP", "Das FHRP-Modul ist aktiv, aber keine HSRP-Gruppe wurde definiert.", "", "fhrp", "hsrpList"));

        if (M("acl") && string.IsNullOrWhiteSpace(V("aclApplyList")) && string.IsNullOrWhiteSpace(V("aclAssignExtList")))
            findings.Add(new("Hinweis", "ACL", "ACLs sind definiert, aber möglicherweise keinem Interface zugewiesen.", "", "acl", "aclApplyList"));

        return findings;
    }

    private static IEnumerable<string> Rows(string value) => (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0);

    private static IEnumerable<int> ParseVlanIds(string value)
    {
        foreach (var row in Rows(value))
        {
            var first = row.Split('|')[0];
            if (int.TryParse(first, out var vlan)) yield return vlan;
        }
    }

    public static IEnumerable<int> ExpandVlanExpression(string expression)
    {
        foreach (var token in (expression ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Contains('-'))
            {
                var range = token.Split('-', 2);
                if (range.Length == 2 && int.TryParse(range[0], out var start) && int.TryParse(range[1], out var end))
                    for (var vlan = Math.Min(start, end); vlan <= Math.Max(start, end); vlan++) yield return vlan;
            }
            else if (int.TryParse(token, out var vlan)) yield return vlan;
        }
    }
}

public static class SecurityAuditService
{
    public static IReadOnlyList<SecurityFinding> Analyze(string config)
    {
        var c = "\n" + (config ?? string.Empty).ToLowerInvariant() + "\n";
        var findings = new List<SecurityFinding>();
        void Add(string severity, string category, string message, string recommendation) => findings.Add(new(severity, category, message, recommendation));

        if (c.Contains("transport input telnet") || c.Contains("transport input all"))
            Add("Kritisch", "Management", "Telnet ist auf VTY-Leitungen zugelassen.", "Nur 'transport input ssh' verwenden.");
        if (c.Contains("\nip http server\n"))
            Add("Hoch", "Management", "Der unverschlüsselte HTTP-Server ist aktiviert.", "'no ip http server' konfigurieren.");
        if (!c.Contains("ip ssh version 2"))
            Add("Hoch", "SSH", "SSH Version 2 ist nicht explizit aktiviert.", "'ip ssh version 2' setzen.");
        if (Regex.IsMatch(c, @"\nenable password\s+"))
            Add("Hoch", "Passwörter", "Es wird 'enable password' statt 'enable secret' verwendet.", "Auf 'enable secret' umstellen.");
        if (!c.Contains("service password-encryption"))
            Add("Mittel", "Passwörter", "Einfache Line-Passwörter werden nicht verschleiert.", "'service password-encryption' ergänzen; Secrets bevorzugen.");
        if (Regex.IsMatch(c, @"\nsnmp-server community\s+"))
            Add("Hoch", "SNMP", "SNMPv1/v2c Community wurde gefunden.", "SNMPv3 mit Authentifizierung und Verschlüsselung nutzen.");
        if (Regex.IsMatch(c, @"switchport trunk native vlan\s+1\b"))
            Add("Mittel", "Switching", "VLAN 1 wird als Native VLAN verwendet.", "Ein ungenutztes separates Native VLAN einsetzen.");
        if (Regex.IsMatch(c, @"switchport access vlan\s+1\b"))
            Add("Mittel", "Switching", "Ein Access-Port verwendet VLAN 1.", "Produktive Ports aus VLAN 1 entfernen.");
        if (c.Contains("line vty") && !c.Contains("access-class "))
            Add("Mittel", "VTY", "Die VTY-Leitungen besitzen keine Zugriffs-ACL.", "SSH-Quellnetze über 'access-class ... in' einschränken.");
        if (c.Contains("switchport mode access") && !c.Contains("spanning-tree portfast bpduguard default") && !c.Contains("spanning-tree bpduguard enable"))
            Add("Mittel", "STP", "Für Access-Ports ist kein BPDU Guard erkennbar.", "BPDU Guard global oder pro Access-Port aktivieren.");
        if (!c.Contains("login block-for"))
            Add("Niedrig", "Login", "Keine Login-Sperre gegen wiederholte Fehlversuche erkannt.", "'login block-for ... attempts ... within ...' konfigurieren.");
        if (!c.Contains("logging host") && !c.Contains("logging trap"))
            Add("Niedrig", "Logging", "Kein zentraler Syslog-Versand erkennbar.", "Mindestens einen Syslog-Server konfigurieren.");
        if (!c.Contains("ntp server"))
            Add("Niedrig", "Zeit", "Keine NTP-Zeitquelle erkennbar.", "NTP-Server und bevorzugtes Quellinterface konfigurieren.");

        if (findings.Count == 0)
            findings.Add(new("OK", "Gesamt", "Keine offensichtlichen Sicherheitsmängel anhand der statischen Prüfung erkannt.", "Geräteplattform und Unternehmensrichtlinien zusätzlich prüfen."));
        return findings;
    }
}

public static class GlobalSearchService
{
    public static IReadOnlyList<GlobalSearchResult> Search(string query)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length < 2) return Array.Empty<GlobalSearchResult>();
        var result = new List<GlobalSearchResult>();
        foreach (var module in ModuleCatalog.All)
        {
            if (Contains(module.Title, query) || Contains(module.Name, query))
                result.Add(new("Modul", module.Title, module.Tab, module.Tab, module.Name, "", ""));
            foreach (var field in module.Fields)
                if (Contains(field.Label, query) || Contains(field.Name, query) || Contains(field.Help, query))
                    result.Add(new("Feld", $"{module.Title} · {field.Label}", field.Help, module.Tab, module.Name, field.Name, ""));
        }
        foreach (var group in ConfigurationCommandCatalog.All)
        foreach (var row in group.Rows)
            if (Contains(row.Command, query) || Contains(row.Meaning, query) || Contains(row.Module, query))
                result.Add(new("Befehl", row.Command, row.Meaning, "Befehle", "", "", row.Command));
        return result.Take(200).ToList();
    }

    private static bool Contains(string? value, string query) => (value ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
}

public static class CommandAnalysisService
{
    public static CommandAnalysisResult Analyze(string input)
    {
        input = Regex.Replace((input ?? string.Empty).Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(input))
            return new(input, "", "", "", "Kein Befehl eingegeben.", Array.Empty<string>(), 0);

        var candidates = ConfigurationCommandCatalog.All
            .SelectMany(g => g.Rows.Select(r => (Group: g.Name, Row: r, Score: Score(input, r.Command))))
            .OrderByDescending(x => x.Score)
            .ToList();
        var best = candidates.FirstOrDefault();
        if (best.Row == null || best.Score < 0.18)
            return new(input, "", "", "", "Kein passendes Befehlsmuster im Register gefunden.", TokenExplanation(input, ""), best.Score);
        return new(input, best.Row.Command, best.Row.Module, best.Row.Mode, best.Row.Meaning, TokenExplanation(input, best.Row.Command), best.Score);
    }

    private static double Score(string input, string pattern)
    {
        var i = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var p = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fixedTokens = p.Where(x => !x.StartsWith('<') && !x.StartsWith('[')).ToArray();
        if (fixedTokens.Length == 0) return 0;
        var matched = fixedTokens.Count(token => i.Any(x => x.Equals(token.TrimEnd(','), StringComparison.OrdinalIgnoreCase)));
        var prefix = 0;
        for (var n = 0; n < Math.Min(i.Length, p.Length); n++)
        {
            if (p[n].StartsWith('<') || p[n].StartsWith('[') || i[n].Equals(p[n], StringComparison.OrdinalIgnoreCase)) prefix++;
            else break;
        }
        return (matched / (double)fixedTokens.Length) * 0.55 + (prefix / (double)Math.Max(i.Length, p.Length)) * 0.45;
    }

    private static IReadOnlyList<string> TokenExplanation(string input, string pattern)
    {
        var inputTokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var patternTokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        for (var index = 0; index < inputTokens.Length; index++)
        {
            var token = inputTokens[index];
            var expected = index < patternTokens.Length ? patternTokens[index] : "zusätzlicher Parameter";
            var meaning = expected.StartsWith('<') ? $"Wert für {expected.Trim('<', '>')}" :
                expected.StartsWith('[') ? $"optionaler Teil {expected}" :
                expected.Equals(token, StringComparison.OrdinalIgnoreCase) ? "Befehlswort" : $"erwartetes Muster: {expected}";
            result.Add($"{index + 1}. {token} — {meaning}");
        }
        return result;
    }
}
