using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace CiscoConfigGuiWpf;

public static class PeerConfigurationGenerator
{
    public static string GenerateDraft(GenerationRequest request, string peerHostname)
    {
        var requirements = PeerRequirementGenerator.Generate(request);
        var sb = new StringBuilder();
        sb.AppendLine("enable");
        sb.AppendLine("configure terminal");
        sb.AppendLine($"hostname {SanitizeHostname(peerHostname)}");
        sb.AppendLine("!");
        sb.AppendLine("! AUTOMATISCH ERZEUGTER GEGENSTELLENENTWURF");
        sb.AppendLine("! Platzhalter <...> vor der Übertragung ersetzen und Adressierung prüfen.");
        sb.AppendLine("!");
        foreach (var line in requirements.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.StartsWith("! GEGENSTELLEN- /", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("! Automatisch aus", StringComparison.OrdinalIgnoreCase)) continue;
            sb.AppendLine(line);
        }
        sb.AppendLine("end");
        sb.AppendLine("write memory");
        return sb.ToString();
    }

    private static string SanitizeHostname(string value)
    {
        var result = Regex.Replace(value ?? "PEER-DEVICE", "[^A-Za-z0-9_-]", "-").Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "PEER-DEVICE" : result;
    }
}

public static class NetworkDiagramService
{
    public sealed record DiagramPoint(double X, double Y, double Width, double Height);

    public static Dictionary<string, DiagramPoint> CalculateLayout(NetworkProject project, double width, double height)
    {
        var result = new Dictionary<string, DiagramPoint>(StringComparer.OrdinalIgnoreCase);
        var count = Math.Max(1, project.Devices.Count);
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        var rows = Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        var cellW = Math.Max(250, width / columns);
        var cellH = Math.Max(215, height / rows);
        const double nodeWidth = 220;
        const double nodeHeight = 170;

        for (var i = 0; i < project.Devices.Count; i++)
        {
            var device = project.Devices[i];
            var row = i / columns;
            var column = i % columns;
            var defaultX = column * cellW + 25;
            var defaultY = row * cellH + 45;
            var x = device.DiagramX ?? defaultX;
            var y = device.DiagramY ?? defaultY;
            x = Math.Clamp(x, 5, Math.Max(5, width - nodeWidth - 5));
            y = Math.Clamp(y, 35, Math.Max(35, height - nodeHeight - 5));
            result[device.Id] = new DiagramPoint(x, y, nodeWidth, nodeHeight);
        }
        return result;
    }

    public static string GetLinkColor(string? linkType) => (linkType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "access" => "#22c55e",
        "trunk" => "#f59e0b",
        "port-channel" => "#60a5fa",
        "routed link" => "#06b6d4",
        "wan" => "#c084fc",
        "tunnel" => "#f472b6",
        "serial" => "#a3e635",
        "fiber" => "#38bdf8",
        "wireless" => "#facc15",
        _ => "#94a3b8"
    };

    public static string GetLinkDashArray(string? linkType) => (linkType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "wan" => "10 6",
        "tunnel" => "5 5",
        "serial" => "12 5 3 5",
        "wireless" => "3 6",
        _ => string.Empty
    };

    public static double GetLinkThickness(string? linkType) => (linkType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "port-channel" => 6,
        "trunk" => 4,
        "fiber" => 4,
        _ => 3
    };

    public static string BuildSvg(NetworkProject project, int width = 1400, int height = 1150)
    {
        var settings = ApplicationSettingsService.Current;
        var requiredWidth = project.Devices.Where(device => device.DiagramX.HasValue).Select(device => device.DiagramX!.Value + 260).DefaultIfEmpty(width).Max();
        var requiredHeight = project.Devices.Where(device => device.DiagramY.HasValue).Select(device => device.DiagramY!.Value + 210).DefaultIfEmpty(height).Max();
        width = Math.Max(width, (int)Math.Ceiling(requiredWidth));
        height = Math.Max(height, (int)Math.Ceiling(requiredHeight));
        var layout = CalculateLayout(project, width, height);
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>");
        sb.AppendLine("<rect width='100%' height='100%' fill='#0b0e13'/>");

        if (settings.ShowConnectionTypes)
        {
            var legendTypes = project.Links
                .Select(x => string.IsNullOrWhiteSpace(x.LinkType) ? "Ethernet" : x.LinkType.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var legendX = 20.0;
            foreach (var type in legendTypes)
            {
                var color = GetLinkColor(type);
                var dash = GetLinkDashArray(type);
                var dashAttribute = string.IsNullOrWhiteSpace(dash)
                    ? string.Empty
                    : $" stroke-dasharray='{dash}'";

                sb.AppendLine($"<line x1='{legendX:0}' y1='20' x2='{legendX + 34:0}' y2='20' stroke='{color}' stroke-width='{GetLinkThickness(type):0}'{dashAttribute}/>");
                sb.AppendLine($"<text x='{legendX + 40:0}' y='24' fill='#cbd5e1' font-family='Segoe UI' font-size='12'>{SecurityElement.Escape(type)}</text>");
                legendX += 65 + Math.Max(40, type.Length * 7);
            }
        }

        foreach (var link in project.Links)
        {
            if (!layout.TryGetValue(link.SourceDeviceId, out var a) ||
                !layout.TryGetValue(link.TargetDeviceId, out var b))
            {
                continue;
            }

            var x1 = a.X + a.Width / 2;
            var y1 = a.Y + a.Height / 2;
            var x2 = b.X + b.Width / 2;
            var y2 = b.Y + b.Height / 2;
            var color = GetLinkColor(link.LinkType);
            var dash = GetLinkDashArray(link.LinkType);
            var dashAttribute = string.IsNullOrWhiteSpace(dash)
                ? string.Empty
                : $" stroke-dasharray='{dash}'";

            sb.AppendLine($"<line x1='{x1:0}' y1='{y1:0}' x2='{x2:0}' y2='{y2:0}' stroke='{color}' stroke-width='{GetLinkThickness(link.LinkType):0}'{dashAttribute}/>");
            sb.AppendLine($"<circle cx='{x1:0}' cy='{y1:0}' r='5' fill='{color}'/><circle cx='{x2:0}' cy='{y2:0}' r='5' fill='{color}'/>");

            var labelParts = new List<string>();
            var type = string.IsNullOrWhiteSpace(link.LinkType) ? "Ethernet" : link.LinkType.Trim();
            if (settings.ShowConnectionTypes) labelParts.Add(type);
            if (!string.IsNullOrWhiteSpace(link.Description)) labelParts.Add(link.Description.Trim());
            if (settings.ShowInterfaceNames) labelParts.Add($"{link.SourceInterface} ↔ {link.TargetInterface}");
            if (settings.ShowRoutingDetails)
            {
                var routing = RoutingVisualizationService.GetLinkSummary(project, link);
                if (!string.IsNullOrWhiteSpace(routing)) labelParts.Add(routing);
            }

            if (labelParts.Count > 0)
            {
                var label = SecurityElement.Escape(string.Join(" · ", labelParts));
                var labelX = (x1 + x2) / 2;
                var labelY = (y1 + y2) / 2;
                sb.AppendLine($"<rect x='{labelX - 105:0}' y='{labelY - 22:0}' width='210' height='22' rx='6' fill='#0b0e13' fill-opacity='0.92' stroke='{color}' stroke-width='1'/>");
                sb.AppendLine($"<text x='{labelX:0}' y='{labelY - 7:0}' fill='{color}' font-family='Segoe UI' font-size='12' font-weight='bold' text-anchor='middle'>{label}</text>");
            }
        }

        foreach (var device in project.Devices)
        {
            if (!layout.TryGetValue(device.Id, out var p)) continue;

            var style = GetSvgDeviceStyle(device.DeviceType);
            var centerX = p.X + p.Width / 2;
            sb.AppendLine($"<rect x='{p.X:0}' y='{p.Y:0}' width='{p.Width:0}' height='{p.Height:0}' rx='{style.Radius}' fill='{style.Fill}' stroke='{style.Stroke}' stroke-width='2'/>");
            AppendSvgDeviceIcon(sb, style, centerX, p.Y + 10);
            sb.AppendLine($"<text x='{centerX:0}' y='{p.Y + 64:0}' fill='#ffffff' font-family='Segoe UI' font-size='16' font-weight='bold' text-anchor='middle'>{SecurityElement.Escape(device.Name)}</text>");
            sb.AppendLine($"<text x='{centerX:0}' y='{p.Y + 86:0}' fill='{style.Stroke}' font-family='Segoe UI' font-size='11' font-weight='bold' text-anchor='middle'>{SecurityElement.Escape(style.Label)} · {SecurityElement.Escape(device.DeviceType)}</text>");
            sb.AppendLine($"<text x='{centerX:0}' y='{p.Y + 106:0}' fill='#64748b' font-family='Segoe UI' font-size='10' text-anchor='middle'>{SecurityElement.Escape(device.ConfigMode)}</text>");
            var roleSite = $"{TopologyPlanningService.InferRole(device)} · {TopologyPlanningService.InferSite(device)}";
            sb.AppendLine($"<text x='{centerX:0}' y='{p.Y + 123:0}' fill='#94a3b8' font-family='Segoe UI' font-size='9' text-anchor='middle'>{SecurityElement.Escape(roleSite)}</text>");
            if (settings.ShowRoutingDetails)
            {
                var routingLines = RoutingVisualizationService.GetDeviceDetails(device).Take(2).ToList();
                for (var lineIndex = 0; lineIndex < routingLines.Count; lineIndex++)
                    sb.AppendLine($"<text x='{centerX:0}' y='{p.Y + 141 + lineIndex * 15:0}' fill='{style.Stroke}' font-family='Segoe UI' font-size='9' text-anchor='middle'>{SecurityElement.Escape(routingLines[lineIndex])}</text>");
            }
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private sealed record SvgDeviceStyle(string Label, string Fill, string Stroke, int Radius);

    private static SvgDeviceStyle GetSvgDeviceStyle(string? deviceType)
    {
        var normalized = (deviceType ?? string.Empty).Trim();
        if (normalized.Contains("L3", StringComparison.OrdinalIgnoreCase)) return new SvgDeviceStyle("L3SW", "#122224", "#2dd4bf", 12);
        if (normalized.Contains("L2", StringComparison.OrdinalIgnoreCase)) return new SvgDeviceStyle("L2SW", "#132118", "#4ade80", 8);
        return new SvgDeviceStyle("RT", "#24180f", "#e8791a", 28);
    }

    private static void AppendSvgDeviceIcon(StringBuilder sb, SvgDeviceStyle style, double centerX, double topY)
    {
        var left = centerX - 22;
        if (style.Label == "RT")
        {
            sb.AppendLine($"<circle cx='{centerX:0}' cy='{topY + 18:0}' r='17' fill='none' stroke='{style.Stroke}' stroke-width='2'/>");
            sb.AppendLine($"<path d='M {centerX - 13:0},{topY + 18:0} L {centerX + 13:0},{topY + 18:0} M {centerX + 8:0},{topY + 13:0} L {centerX + 13:0},{topY + 18:0} L {centerX + 8:0},{topY + 23:0} M {centerX - 8:0},{topY + 13:0} L {centerX - 13:0},{topY + 18:0} L {centerX - 8:0},{topY + 23:0} M {centerX:0},{topY + 5:0} L {centerX:0},{topY + 31:0} M {centerX - 5:0},{topY + 10:0} L {centerX:0},{topY + 5:0} L {centerX + 5:0},{topY + 10:0} M {centerX - 5:0},{topY + 26:0} L {centerX:0},{topY + 31:0} L {centerX + 5:0},{topY + 26:0}' fill='none' stroke='{style.Stroke}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>");
            return;
        }

        sb.AppendLine($"<rect x='{left:0}' y='{topY + 3:0}' width='44' height='30' rx='{(style.Label == "L3SW" ? 5 : 4)}' fill='none' stroke='{style.Stroke}' stroke-width='2'/>");
        if (style.Label == "L3SW")
        {
            sb.AppendLine($"<path d='M {left + 6:0},{topY + 12:0} L {left + 38:0},{topY + 12:0} M {left + 33:0},{topY + 8:0} L {left + 38:0},{topY + 12:0} L {left + 33:0},{topY + 16:0} M {left + 11:0},{topY + 8:0} L {left + 6:0},{topY + 12:0} L {left + 11:0},{topY + 16:0} M {centerX:0},{topY + 7:0} L {centerX:0},{topY + 29:0} M {centerX - 4:0},{topY + 11:0} L {centerX:0},{topY + 7:0} L {centerX + 4:0},{topY + 11:0} M {centerX - 4:0},{topY + 25:0} L {centerX:0},{topY + 29:0} L {centerX + 4:0},{topY + 25:0}' fill='none' stroke='{style.Stroke}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>");
        }
        else
        {
            sb.AppendLine($"<path d='M {left + 6:0},{topY + 12:0} L {left + 38:0},{topY + 12:0} M {left + 33:0},{topY + 8:0} L {left + 38:0},{topY + 12:0} L {left + 33:0},{topY + 16:0} M {left + 11:0},{topY + 8:0} L {left + 6:0},{topY + 12:0} L {left + 11:0},{topY + 16:0} M {left + 6:0},{topY + 24:0} L {left + 38:0},{topY + 24:0} M {left + 33:0},{topY + 20:0} L {left + 38:0},{topY + 24:0} L {left + 33:0},{topY + 28:0} M {left + 11:0},{topY + 20:0} L {left + 6:0},{topY + 24:0} L {left + 11:0},{topY + 28:0}' fill='none' stroke='{style.Stroke}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>");
        }
    }
}

public static class ReportExportService
{
    public static string BuildPlainText(NetworkProject project, IEnumerable<DependencyFinding> dependencies, IEnumerable<SecurityFinding> security)
    {
        var settings = ApplicationSettingsService.Current;
        var english = ResolveReportLanguage(settings.ReportLanguage).StartsWith("en", StringComparison.OrdinalIgnoreCase);
        string R(string de, string en) => english ? en : de;
        var sb = new StringBuilder();
        var info = project.ProjectInfo ?? new ProjectPlanInfo();

        sb.AppendLine(R("NETZPLAN", "NETWORK PLAN"));
        sb.AppendLine(new string('=', 72));
        sb.AppendLine($"{R("Projekt", "Project")}: {project.Name}");
        if (!string.IsNullOrWhiteSpace(info.ProjectNumber)) sb.AppendLine($"{R("Projektnummer", "Project number")}: {info.ProjectNumber}");
        if (!string.IsNullOrWhiteSpace(info.Customer)) sb.AppendLine($"{R("Organisation / Kunde", "Organization / customer")}: {info.Customer}");
        if (!string.IsNullOrWhiteSpace(info.Location)) sb.AppendLine($"{R("Standort", "Location")}: {info.Location}");
        if (!string.IsNullOrWhiteSpace(info.ProjectManager)) sb.AppendLine($"{R("Projektleiter", "Project manager")}: {info.ProjectManager}");
        if (!string.IsNullOrWhiteSpace(info.Author)) sb.AppendLine($"{R("Bearbeiter", "Author")}: {info.Author}");
        if (!string.IsNullOrWhiteSpace(info.Version)) sb.AppendLine($"{R("Version", "Version")}: {info.Version}");
        if (!string.IsNullOrWhiteSpace(info.Status)) sb.AppendLine($"{R("Status", "Status")}: {info.Status}");
        if (!string.IsNullOrWhiteSpace(settings.CompanyName)) sb.AppendLine($"{R("Firma", "Company")}: {settings.CompanyName}");
        sb.AppendLine($"{R("Erstellt", "Created")}: {DateTime.Now:yyyy-MM-dd HH:mm}");
        if (!string.IsNullOrWhiteSpace(project.Description))
        {
            sb.AppendLine();
            sb.AppendLine(R("BESCHREIBUNG", "DESCRIPTION"));
            sb.AppendLine(project.Description);
        }

        sb.AppendLine();
        sb.AppendLine(R("GERÄTEÜBERSICHT", "DEVICE OVERVIEW"));
        sb.AppendLine(new string('-', 72));
        foreach (var d in project.Devices)
        {
            var label = DeviceLabel(d.DeviceType);
            var protocols = string.Join(", ", ExtractRoutingProtocols(d.GeneratedConfiguration));
            var vrfs = string.Join(", ", ExtractVrfs(d.GeneratedConfiguration));
            var acls = string.Join(", ", ExtractAclBindings(d.GeneratedConfiguration).Select(x => x.Acl).Distinct(StringComparer.OrdinalIgnoreCase));
            sb.AppendLine($"- {d.Name} | {label} | {d.DeviceType} | {d.ConfigMode} | {TopologyPlanningService.InferRole(d)} | {TopologyPlanningService.InferSite(d)}");
            if (!string.IsNullOrWhiteSpace(protocols)) sb.AppendLine($"  {R("Routing", "Routing")}: {protocols}");
            if (!string.IsNullOrWhiteSpace(vrfs)) sb.AppendLine($"  VRF: {vrfs}");
            if (!string.IsNullOrWhiteSpace(acls)) sb.AppendLine($"  ACL: {acls}");
        }

        sb.AppendLine();
        sb.AppendLine(R("VERBINDUNGEN", "CONNECTIONS"));
        sb.AppendLine(new string('-', 72));
        foreach (var l in project.Links)
        {
            var source = project.Devices.FirstOrDefault(x => x.Id == l.SourceDeviceId)?.Name ?? l.SourceDeviceId;
            var target = project.Devices.FirstOrDefault(x => x.Id == l.TargetDeviceId)?.Name ?? l.TargetDeviceId;
            var description = string.IsNullOrWhiteSpace(l.Description) ? string.Empty : $" | {l.Description}";
            sb.AppendLine($"- {source} {l.SourceInterface} ↔ {target} {l.TargetInterface} | {l.LinkType}{description}");
        }

        sb.AppendLine();
        sb.AppendLine(R("IP-/VLAN-ÜBERSICHT", "IP/VLAN OVERVIEW"));
        sb.AppendLine(new string('-', 72));
        foreach (var i in project.IpamEntries.OrderBy(x => x.Network, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- {i.Network}/{i.PrefixLength} | VLAN {i.Vlan} | GW {i.Gateway} | {i.Device} {i.Interface} | {i.Description}");
        foreach (var d in project.Devices)
        {
            var vlans = string.Join(", ", ExtractVlans(d.GeneratedConfiguration));
            if (!string.IsNullOrWhiteSpace(vlans)) sb.AppendLine($"- {d.Name}: {R("VLANs", "VLANs")} {vlans}");
        }

        sb.AppendLine();
        sb.AppendLine(R("INTERFACE- UND PORTPLAN", "INTERFACE AND PORT PLAN"));
        sb.AppendLine(new string('-', 72));
        foreach (var d in project.Devices)
        {
            var ports = PortPlanService.Parse(d.GeneratedConfiguration).Take(12).ToList();
            if (ports.Count == 0) continue;
            sb.AppendLine($"{d.Name}:");
            foreach (var p in ports)
            {
                var mode = string.IsNullOrWhiteSpace(p.Mode) ? "-" : p.Mode;
                var vlanText = string.Join(", ", new[]
                {
                    string.IsNullOrWhiteSpace(p.AccessVlan) ? string.Empty : $"Access {p.AccessVlan}",
                    string.IsNullOrWhiteSpace(p.NativeVlan) ? string.Empty : $"Native {p.NativeVlan}",
                    string.IsNullOrWhiteSpace(p.AllowedVlans) ? string.Empty : $"Allowed {p.AllowedVlans}"
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
                sb.AppendLine($"  - {p.Interface} | {mode} | {vlanText} | {p.IpAddress}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(R("ROUTEN", "ROUTES"));
        sb.AppendLine(new string('-', 72));
        foreach (var route in RoutingVisualizationService.ExtractStaticRoutes(project))
            sb.AppendLine($"- {route.Device} | {route.Vrf} | {route.Network} {route.MaskOrPrefix} -> {route.NextHop} | {route.AddressFamily}");

        sb.AppendLine();
        sb.AppendLine(R("ACL-ZUORDNUNGEN", "ACL ASSIGNMENTS"));
        sb.AppendLine(new string('-', 72));
        var effectiveBindings = project.AclBindings.Count > 0
            ? project.AclBindings
            : new System.Collections.ObjectModel.ObservableCollection<ProjectAclBinding>(project.Devices.SelectMany(device => ExtractAclBindings(device.GeneratedConfiguration).Select(acl => new ProjectAclBinding { DeviceName = device.Name, Interface = acl.Interface, AclName = acl.Acl, Direction = acl.Direction })));
        foreach (var acl in effectiveBindings)
            sb.AppendLine($"- {acl.DeviceName} | {acl.Interface} | {acl.AclName} | {acl.Direction} | {acl.AddressFamily}");

        if (project.AclRules.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(R("ACL-REGELN UND ANALYSE", "ACL RULES AND ANALYSIS"));
            sb.AppendLine(new string('-', 72));
            foreach (var rule in project.AclRules.Where(rule => rule.Enabled).OrderBy(rule => rule.DeviceName).ThenBy(rule => rule.AclName).ThenBy(rule => rule.Sequence))
                sb.AppendLine($"- {rule.DeviceName} | {rule.AclName} | {rule.Sequence} | {rule.Action} {rule.Protocol} {rule.Source} {rule.SourceWildcard} {rule.Destination} {rule.DestinationWildcard} {rule.Service}".TrimEnd());
            foreach (var finding in AclWorkspaceService.Analyze(project.AclRules, effectiveBindings))
                sb.AppendLine($"  [{finding.Severity}] {finding.DeviceName}/{finding.AclName}/{finding.Sequence}: {finding.Message}");
        }

        sb.AppendLine();
        sb.AppendLine(R("VRF- UND ROUTING-PROTOKOLLE", "VRF AND ROUTING PROTOCOLS"));
        sb.AppendLine(new string('-', 72));
        foreach (var d in project.Devices)
        {
            var vrfs = string.Join(", ", ExtractVrfs(d.GeneratedConfiguration));
            var protocols = string.Join(", ", ExtractRoutingProtocols(d.GeneratedConfiguration));
            sb.AppendLine($"- {d.Name} | VRF: {(string.IsNullOrWhiteSpace(vrfs) ? "-" : vrfs)} | {R("Protokolle", "Protocols")}: {(string.IsNullOrWhiteSpace(protocols) ? "-" : protocols)}");
        }

        sb.AppendLine();
        sb.AppendLine(R("PRÜFUNGEN", "VALIDATION"));
        sb.AppendLine(new string('-', 72));
        foreach (var f in dependencies) sb.AppendLine($"- [{f.Severity}] {f.Area}: {(english ? LocalizationService.TranslateText(f.Message, "en-US") : f.Message)}");

        sb.AppendLine();
        sb.AppendLine(R("SICHERHEITSPRÜFUNG", "SECURITY AUDIT"));
        sb.AppendLine(new string('-', 72));
        foreach (var f in security) sb.AppendLine($"- [{f.Severity}] {f.Category}: {(english ? LocalizationService.TranslateText(f.Message, "en-US") : f.Message)} {R("Empfehlung", "Recommendation")}: {(english ? LocalizationService.TranslateText(f.Recommendation, "en-US") : f.Recommendation)}");

        sb.AppendLine();
        sb.AppendLine(R("TESTPLAN", "TEST PLAN"));
        sb.AppendLine(new string('-', 72));
        foreach (var cmd in new[]
                 {
                     "show ip interface brief",
                     "show vlan brief",
                     "show interfaces trunk",
                     "show ip route",
                     "show ip ospf neighbor",
                     "show ip bgp summary",
                     "show access-lists",
                     "show vrf",
                     "show etherchannel summary"
                 })
            sb.AppendLine($"- {cmd}");
        return sb.ToString();
    }

    private static string ResolveReportLanguage(string? language)
    {
        var value = (language ?? "system").Trim();
        if (value.Equals("system", StringComparison.OrdinalIgnoreCase))
            return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "de-DE";
        return value;
    }

    public static void ExportHtml(string path, NetworkProject project, IEnumerable<DependencyFinding> dependencies, IEnumerable<SecurityFinding> security)
    {
        var plain = BuildPlainText(project, dependencies, security);
        var svg = NetworkDiagramService.BuildSvg(project, 1400, 1100);
        var escapedLines = string.Join("", (plain ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => $"<div>{Escape(line)}</div>"));
        var html = $$"""
<!doctype html><html lang="de"><head><meta charset="utf-8"><title>{{Escape(project.Name)}} - Netzplan</title>
<style>
body{font-family:Segoe UI,Arial;background:#0b0e13;color:#eef2f7;margin:32px}
h1,h2{color:#f59e0b}.card{background:#12161e;border:1px solid #303846;border-radius:12px;padding:18px;margin:0 0 18px 0}
.diagram{overflow:auto;background:#0b0e13;border:1px solid #303846;border-radius:10px;padding:12px}.mono{font-family:Consolas,monospace;font-size:13px;line-height:1.45}
</style></head>
<body>
<h1>{{Escape(project.Name)}} – Netzplan</h1>
<div class="card diagram">{{svg}}</div>
<div class="card mono">{{escapedLines}}</div>
</body></html>
""";
        File.WriteAllText(path, html, new UTF8Encoding(false));
    }

    public static void ExportDocx(string path, string plainText)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        WriteEntry(zip, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
""");
        WriteEntry(zip, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
""");
        var body = new StringBuilder();
        foreach (var line in (plainText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(line)).Append("</w:t></w:r></w:p>");
        var settings = ApplicationSettingsService.Current;
        var letter = settings.PageSize.Equals("Letter", StringComparison.OrdinalIgnoreCase);
        var landscape = settings.PageOrientation.Equals("Querformat", StringComparison.OrdinalIgnoreCase);
        var pageW = letter ? 12240 : 11906;
        var pageH = letter ? 15840 : 16838;
        if (landscape) (pageW, pageH) = (pageH, pageW);
        var orient = landscape ? " w:orient=\"landscape\"" : string.Empty;
        WriteEntry(zip, "word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}<w:sectPr><w:pgSz w:w=\"{pageW}\" w:h=\"{pageH}\"{orient}/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\"/></w:sectPr></w:body></w:document>");
    }

    public static void ExportPdf(string path, string plainText)
    {
        var settings = ApplicationSettingsService.Current;
        var landscape = settings.PageOrientation.Equals("Querformat", StringComparison.OrdinalIgnoreCase);
        var letter = settings.PageSize.Equals("Letter", StringComparison.OrdinalIgnoreCase);
        var pageWidth = letter ? 612 : 595;
        var pageHeight = letter ? 792 : 842;
        if (landscape) (pageWidth, pageHeight) = (pageHeight, pageWidth);
        var lineWidth = landscape ? 135 : 92;
        var linesPerPage = landscape ? 42 : 52;
        var lines = WrapLines(plainText, lineWidth).Take(1000).ToList();
        var pages = lines.Chunk(linesPerPage).ToList();
        var objects = new List<string>();
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        var pageRefs = Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 2} 0 R").ToArray();
        objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageRefs)}] /Count {pages.Count} >>");
        for (var p = 0; p < pages.Count; p++)
        {
            var pageObjectNumber = 3 + p * 2;
            var contentObjectNumber = pageObjectNumber + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /Font << /F1 {3 + pages.Count * 2} 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
            var stream = new StringBuilder($"BT\n/F1 9 Tf\n42 {pageHeight - 37} Td\n12 TL\n");
            foreach (var line in pages[p]) stream.Append('(').Append(EscapePdf(line)).Append(") Tj\nT*\n");
            stream.Append("ET\n");
            var bytes = Encoding.Latin1.GetBytes(stream.ToString());
            objects.Add($"<< /Length {bytes.Length} >>\nstream\n{stream}endstream");
        }
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        using var file = File.Create(path);
        using var writer = new StreamWriter(file, Encoding.Latin1, 1024, true) { NewLine = "\n" };
        writer.Write("%PDF-1.4\n"); writer.Flush();
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Position);
            writer.Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); writer.Flush();
        }
        var xref = file.Position;
        writer.Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) writer.Write($"{offset:0000000000} 00000 n \n");
        writer.Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
    }

    private sealed record StaticRouteRow(string Vrf, string Network, string Mask, string NextHop);
    private sealed record AclBindingRow(string Interface, string Acl, string Direction);

    private static string DeviceLabel(string? deviceType)
    {
        var normalized = (deviceType ?? string.Empty).Trim();
        if (normalized.Contains("L3", StringComparison.OrdinalIgnoreCase)) return "L3SW";
        if (normalized.Contains("L2", StringComparison.OrdinalIgnoreCase)) return "L2SW";
        return "RT";
    }

    private static IEnumerable<string> ExtractVlans(string config) => Regex.Matches(config ?? string.Empty, @"(?im)^\s*vlan\s+(\d+)")
        .Cast<Match>()
        .Select(m => m.Groups[1].Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => int.TryParse(x, out var n) ? n : int.MaxValue)
        .ThenBy(x => x, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtractVrfs(string config) => Regex.Matches(config ?? string.Empty, @"(?im)^\s*(?:vrf definition|ip vrf)\s+(\S+)")
        .Cast<Match>()
        .Select(m => m.Groups[1].Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ExtractRoutingProtocols(string config)
    {
        var result = new List<string>();
        if (Regex.IsMatch(config ?? string.Empty, @"(?im)^\s*router\s+ospf\s+")) result.Add("OSPF");
        if (Regex.IsMatch(config ?? string.Empty, @"(?im)^\s*router\s+eigrp\s+")) result.Add("EIGRP");
        if (Regex.IsMatch(config ?? string.Empty, @"(?im)^\s*router\s+bgp\s+")) result.Add("BGP");
        if (Regex.IsMatch(config ?? string.Empty, @"(?im)^\s*router\s+isis(?:\s|$)")) result.Add("IS-IS");
        if (Regex.IsMatch(config ?? string.Empty, @"(?im)^\s*ipv6\s+router\s+ospf\s+")) result.Add("OSPFv3");
        return result;
    }

    private static IEnumerable<StaticRouteRow> ExtractStaticRoutes(string config)
    {
        foreach (Match match in Regex.Matches(config ?? string.Empty, @"(?im)^\s*ip route(?: vrf (\S+))?\s+(\S+)\s+(\S+)\s+(\S+).*$"))
            yield return new StaticRouteRow(string.IsNullOrWhiteSpace(match.Groups[1].Value) ? "Global" : match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value);
    }

    private static IEnumerable<AclBindingRow> ExtractAclBindings(string config)
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
            if (line == "!" || line.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                currentInterface = string.Empty;
                continue;
            }
            var match = Regex.Match(line, @"^ip access-group\s+(\S+)\s+(in|out)", RegexOptions.IgnoreCase);
            if (match.Success)
                yield return new AclBindingRow(string.IsNullOrWhiteSpace(currentInterface) ? "global" : currentInterface, match.Groups[1].Value, match.Groups[2].Value.ToUpperInvariant());
        }
    }

    private static IEnumerable<string> WrapLines(string text, int width)
    {
        foreach (var original in (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = original;
            while (line.Length > width)
            {
                var cut = line.LastIndexOf(' ', width);
                if (cut < 20) cut = width;
                yield return line[..cut];
                line = line[cut..].TrimStart();
            }
            yield return line;
        }
    }

    private static string Escape(string value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    private static string EscapePdf(string value) => Regex.Replace(value ?? string.Empty, @"[^\u0020-\u00FF]", "?").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
