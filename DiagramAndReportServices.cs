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
        var cellW = Math.Max(180, width / columns);
        var cellH = Math.Max(130, height / rows);
        for (var i = 0; i < project.Devices.Count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            result[project.Devices[i].Id] = new DiagramPoint(column * cellW + 25, row * cellH + 25, 150, 82);
        }
        return result;
    }

    public static string BuildSvg(NetworkProject project, int width = 1400, int height = 900)
    {
        var layout = CalculateLayout(project, width, height);
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#0b0e13\"/>");
        sb.AppendLine("<defs><marker id=\"arrow\" markerWidth=\"8\" markerHeight=\"8\" refX=\"7\" refY=\"4\" orient=\"auto\"><path d=\"M0,0 L8,4 L0,8 z\" fill=\"#e8791a\"/></marker></defs>");
        foreach (var link in project.Links)
        {
            if (!layout.TryGetValue(link.SourceDeviceId, out var a) || !layout.TryGetValue(link.TargetDeviceId, out var b)) continue;
            var x1 = a.X + a.Width / 2; var y1 = a.Y + a.Height / 2;
            var x2 = b.X + b.Width / 2; var y2 = b.Y + b.Height / 2;
            sb.AppendLine($"<line x1=\"{x1:0}\" y1=\"{y1:0}\" x2=\"{x2:0}\" y2=\"{y2:0}\" stroke=\"#e8791a\" stroke-width=\"3\" marker-end=\"url(#arrow)\"/>");
            var label = SecurityElement.Escape($"{link.SourceInterface} ↔ {link.TargetInterface}");
            sb.AppendLine($"<text x=\"{(x1 + x2) / 2:0}\" y=\"{(y1 + y2) / 2 - 7:0}\" fill=\"#fbbf24\" font-family=\"Segoe UI\" font-size=\"12\" text-anchor=\"middle\">{label}</text>");
        }
        foreach (var device in project.Devices)
        {
            if (!layout.TryGetValue(device.Id, out var p)) continue;
            sb.AppendLine($"<rect x=\"{p.X:0}\" y=\"{p.Y:0}\" width=\"{p.Width:0}\" height=\"{p.Height:0}\" rx=\"12\" fill=\"#171c25\" stroke=\"#e8791a\" stroke-width=\"2\"/>");
            sb.AppendLine($"<text x=\"{p.X + p.Width / 2:0}\" y=\"{p.Y + 30:0}\" fill=\"#ffffff\" font-family=\"Segoe UI\" font-size=\"16\" font-weight=\"bold\" text-anchor=\"middle\">{SecurityElement.Escape(device.Name)}</text>");
            sb.AppendLine($"<text x=\"{p.X + p.Width / 2:0}\" y=\"{p.Y + 55:0}\" fill=\"#9ca6b5\" font-family=\"Segoe UI\" font-size=\"12\" text-anchor=\"middle\">{SecurityElement.Escape(device.DeviceType)}</text>");
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}

public static class ReportExportService
{
    public static string BuildPlainText(NetworkProject project, IEnumerable<DependencyFinding> dependencies, IEnumerable<SecurityFinding> security)
    {
        var sb = new StringBuilder();
        sb.AppendLine(project.Name);
        sb.AppendLine(new string('=', Math.Max(10, project.Name.Length)));
        sb.AppendLine($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm}");
        if (!string.IsNullOrWhiteSpace(project.Description)) sb.AppendLine(project.Description);
        sb.AppendLine();
        sb.AppendLine("GERÄTEÜBERSICHT");
        foreach (var d in project.Devices) sb.AppendLine($"- {d.Name} | {d.DeviceType} | {d.ConfigMode} | {d.Status}");
        sb.AppendLine();
        sb.AppendLine("IP-ADRESSPLAN");
        foreach (var i in project.IpamEntries) sb.AppendLine($"- {i.Network}/{i.PrefixLength} | VLAN {i.Vlan} | GW {i.Gateway} | {i.Device} {i.Interface} | {i.Description}");
        sb.AppendLine();
        sb.AppendLine("VERBINDUNGEN");
        foreach (var l in project.Links)
        {
            var source = project.Devices.FirstOrDefault(x => x.Id == l.SourceDeviceId)?.Name ?? l.SourceDeviceId;
            var target = project.Devices.FirstOrDefault(x => x.Id == l.TargetDeviceId)?.Name ?? l.TargetDeviceId;
            sb.AppendLine($"- {source} {l.SourceInterface} ↔ {target} {l.TargetInterface} | {l.LinkType}");
        }
        sb.AppendLine();
        sb.AppendLine("ABHÄNGIGKEITEN");
        foreach (var f in dependencies) sb.AppendLine($"- [{f.Severity}] {f.Area}: {f.Message}");
        sb.AppendLine();
        sb.AppendLine("SICHERHEITSPRÜFUNG");
        foreach (var f in security) sb.AppendLine($"- [{f.Severity}] {f.Category}: {f.Message} Empfehlung: {f.Recommendation}");
        sb.AppendLine();
        sb.AppendLine("TESTPLAN");
        sb.AppendLine("- show ip interface brief");
        sb.AppendLine("- show vlan brief");
        sb.AppendLine("- show interfaces trunk");
        sb.AppendLine("- show spanning-tree summary");
        sb.AppendLine("- show ip route");
        sb.AppendLine("- show ip ospf neighbor");
        sb.AppendLine("- show etherchannel summary");
        sb.AppendLine("- show access-lists");
        return sb.ToString();
    }

    public static void ExportHtml(string path, NetworkProject project, IEnumerable<DependencyFinding> dependencies, IEnumerable<SecurityFinding> security)
    {
        var plain = BuildPlainText(project, dependencies, security);
        var svg = NetworkDiagramService.BuildSvg(project, 1200, 650);
        var html = $$"""
<!doctype html><html lang="de"><head><meta charset="utf-8"><title>{{Escape(project.Name)}}</title>
<style>body{font-family:Segoe UI,Arial;background:#0b0e13;color:#eef2f7;margin:32px}h1,h2{color:#f59e0b}pre{white-space:pre-wrap;background:#12161e;border:1px solid #303846;border-radius:10px;padding:18px}.diagram{overflow:auto;background:#0b0e13;border:1px solid #303846;border-radius:10px}</style></head>
<body><h1>{{Escape(project.Name)}}</h1><div class="diagram">{{svg}}</div><h2>Projektbericht</h2><pre>{{Escape(plain)}}</pre></body></html>
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
        WriteEntry(zip, "word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\"/></w:sectPr></w:body></w:document>");
    }

    public static void ExportPdf(string path, string plainText)
    {
        var lines = WrapLines(plainText, 92).Take(800).ToList();
        var pages = lines.Chunk(52).ToList();
        var objects = new List<string>();
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        var pageRefs = Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 2} 0 R").ToArray();
        objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageRefs)}] /Count {pages.Count} >>");
        for (var p = 0; p < pages.Count; p++)
        {
            var pageObjectNumber = 3 + p * 2;
            var contentObjectNumber = pageObjectNumber + 1;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {3 + pages.Count * 2} 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
            var stream = new StringBuilder("BT\n/F1 9 Tf\n42 805 Td\n12 TL\n");
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
