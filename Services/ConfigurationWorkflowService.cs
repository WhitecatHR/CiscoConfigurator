using System.Text;

namespace CiscoConfigGuiWpf;

public sealed record DuplicateConfigIssue(
    string Context,
    string Command,
    int Count,
    IReadOnlyList<int> Lines);

public sealed class ConfigurationWorkflowOptions
{
    public bool IncludeComments { get; init; } = true;
    public bool IncludeSectionSeparators { get; init; } = true;
    public bool IncludeEnable { get; init; } = true;
    public bool IncludeConfigureTerminal { get; init; } = true;
    public bool IncludeEnd { get; init; } = true;
    public bool IncludeWriteMemory { get; init; } = true;
    public string LineEndings { get; init; } = "Windows (CRLF)";
    public string ExportFileNamePattern { get; init; } = "cisco_config_{hostname}";
    public bool TimestampInFileName { get; init; }
}

public sealed class ConfigurationPreviewText
{
    public string Heading { get; init; } = string.Empty;
    public string ResultFormat { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public string IssueFormat { get; init; } = string.Empty;
    public string MoreIssuesFormat { get; init; } = string.Empty;
}

public sealed class ConfigurationWorkflowResult
{
    public required GenerationRequest Request { get; init; }
    public required string Configuration { get; init; }
    public required string Preview { get; init; }
    public required IReadOnlyList<DuplicateConfigIssue> DuplicateIssues { get; init; }
    public bool HasDuplicateIssues => DuplicateIssues.Count > 0;
}

public sealed class ConfigurationExportRequest
{
    public required string TargetPath { get; init; }
    public required ConfigurationWorkflowResult Generation { get; init; }
    public bool ExportPeerConfiguration { get; init; }
    public string RollbackText { get; init; } = string.Empty;
    public string ReportText { get; init; } = string.Empty;
}

public sealed class ConfigurationExportResult
{
    public required string ConfigurationPath { get; init; }
    public string? PeerPath { get; init; }
    public string? RollbackPath { get; init; }
    public string? ReportPath { get; init; }
}

public sealed class ConfigurationWorkflowService
{
    private static readonly HashSet<string> VrfOnlyModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "vrfDefs", "vrfSvi", "vrfStaticRoutes", "vrfOspf", "vrfOspfv3", "vrfBgp"
    };

    private static readonly HashSet<string> NoVrfOnlyModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "staticRoutes", "ospf", "bgp", "isis", "ospfv3", "ipv6RoutingProtocols"
    };

    public GenerationRequest BuildRequest(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, bool> selectedModules,
        IEnumerable<ModuleDefinition> moduleDefinitions)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(selectedModules);
        ArgumentNullException.ThrowIfNull(moduleDefinitions);

        var requestValues = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var deviceType = GetValue(requestValues, "deviceType", "Router");
        var configMode = GetValue(requestValues, "configMode", "Ohne VRF");
        var modules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in moduleDefinitions)
        {
            var selected = selectedModules.TryGetValue(module.Name, out var isSelected) && isSelected;
            modules[module.Name] = selected && IsModuleAllowed(module, deviceType, configMode);
        }

        return new GenerationRequest
        {
            Values = requestValues,
            Modules = modules
        };
    }

    public async Task<ConfigurationWorkflowResult> GenerateAsync(
        GenerationRequest request,
        ConfigurationWorkflowOptions options,
        ConfigurationPreviewText previewText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(previewText);

        cancellationToken.ThrowIfCancellationRequested();
        var generated = await GenerateRawAsync(request, cancellationToken).ConfigureAwait(false);
        var configuration = ApplyOutputSettings(generated, options);
        var duplicateIssues = FindDuplicateConfigIssues(configuration);
        var preview = BuildPreview(configuration, duplicateIssues, previewText);

        return new ConfigurationWorkflowResult
        {
            Request = request,
            Configuration = configuration,
            Preview = preview,
            DuplicateIssues = duplicateIssues
        };
    }

    public string GetCopyText(ConfigurationWorkflowResult result, bool includePreview) =>
        includePreview ? result.Preview : result.Configuration;

    public Task<string> GenerateRawAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var pluginSections = PluginModuleService.Generate(request.Values, request.Modules);
        return NativeCiscoGenerator.GenerateAsync(request, pluginSections, cancellationToken);
    }

    public string GeneratePeerRequirements(GenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PeerRequirementGenerator.Generate(request);
    }

    public async Task<ConfigurationExportResult> ExportAsync(
        ConfigurationExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = System.IO.Path.GetFullPath(request.TargetPath);
        var directory = System.IO.Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        System.IO.Directory.CreateDirectory(directory);
        await System.IO.File.WriteAllTextAsync(fullPath, request.Generation.Configuration, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        var baseName = System.IO.Path.GetFileNameWithoutExtension(fullPath);
        string? peerPath = null;
        string? rollbackPath = null;
        string? reportPath = null;

        if (request.ExportPeerConfiguration)
        {
            peerPath = System.IO.Path.Combine(directory, baseName + "_peer.txt");
            var peerText = GeneratePeerRequirements(request.Generation.Request);
            await System.IO.File.WriteAllTextAsync(peerPath, peerText, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.RollbackText))
        {
            rollbackPath = System.IO.Path.Combine(directory, baseName + "_rollback.txt");
            await System.IO.File.WriteAllTextAsync(rollbackPath, request.RollbackText, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.ReportText))
        {
            reportPath = System.IO.Path.Combine(directory, baseName + "_report.txt");
            await System.IO.File.WriteAllTextAsync(reportPath, request.ReportText, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        }

        return new ConfigurationExportResult
        {
            ConfigurationPath = fullPath,
            PeerPath = peerPath,
            RollbackPath = rollbackPath,
            ReportPath = reportPath
        };
    }

    public string BuildSafeExportFileName(
        ConfigurationWorkflowOptions options,
        IReadOnlyDictionary<string, string> values,
        DateTime timestamp)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(values);

        var hostname = GetValue(values, "hostname", "device");
        var device = GetValue(values, "deviceType", "device");
        var pattern = string.IsNullOrWhiteSpace(options.ExportFileNamePattern)
            ? "cisco_config_{hostname}"
            : options.ExportFileNamePattern;

        var name = pattern
            .Replace("{hostname}", hostname, StringComparison.OrdinalIgnoreCase)
            .Replace("{device}", device, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", timestamp.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", timestamp.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase);

        if (options.TimestampInFileName && !name.Contains(timestamp.ToString("yyyyMMdd"), StringComparison.Ordinal))
            name += "_" + timestamp.ToString("yyyyMMdd_HHmmss");

        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        name = name.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(name)) name = "cisco_config";
        if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name += ".txt";
        return name;
    }

    public string BuildDuplicateWarning(
        IReadOnlyList<DuplicateConfigIssue> issues,
        string issueFormat,
        string moreIssuesFormat,
        int maximumIssues = 8)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.Count == 0) return string.Empty;

        var lines = issues.Take(maximumIssues).Select(issue => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            issueFormat,
            issue.Context,
            issue.Command,
            issue.Count,
            string.Join(", ", issue.Lines)));
        var result = string.Join(Environment.NewLine, lines);
        if (issues.Count > maximumIssues)
            result += Environment.NewLine + string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                moreIssuesFormat,
                issues.Count - maximumIssues);
        return result;
    }

    private static bool IsModuleAllowed(ModuleDefinition module, string deviceType, string configMode)
    {
        var deviceAllowed = module.Devices.Contains("All", StringComparer.OrdinalIgnoreCase) ||
                            module.Devices.Contains(deviceType, StringComparer.OrdinalIgnoreCase);
        if (!deviceAllowed) return false;

        return configMode.Equals("Mit VRF", StringComparison.OrdinalIgnoreCase)
            ? !NoVrfOnlyModules.Contains(module.Name)
            : !VrfOnlyModules.Contains(module.Name);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string ApplyOutputSettings(string configuration, ConfigurationWorkflowOptions options)
    {
        var lines = (configuration ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (!options.IncludeComments)
            lines = lines.Where(line => !line.TrimStart().StartsWith("!", StringComparison.Ordinal)).ToList();
        else if (!options.IncludeSectionSeparators)
            lines = lines.Where(line => !System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^!\s*[=\-]{3,}\s*$")).ToList();

        if (!options.IncludeEnable)
            lines.RemoveAll(line => line.Trim().Equals("enable", StringComparison.OrdinalIgnoreCase));
        if (!options.IncludeConfigureTerminal)
            lines.RemoveAll(line => line.Trim().Equals("configure terminal", StringComparison.OrdinalIgnoreCase) || line.Trim().Equals("conf t", StringComparison.OrdinalIgnoreCase));
        if (!options.IncludeEnd)
            lines.RemoveAll(line => line.Trim().Equals("end", StringComparison.OrdinalIgnoreCase));
        if (!options.IncludeWriteMemory)
            lines.RemoveAll(line => line.Trim().Equals("write memory", StringComparison.OrdinalIgnoreCase) || line.Trim().Equals("copy running-config startup-config", StringComparison.OrdinalIgnoreCase));

        var newline = options.LineEndings.Equals("Unix (LF)", StringComparison.OrdinalIgnoreCase) ? "\n" : "\r\n";
        return string.Join(newline, lines).TrimEnd() + newline;
    }

    private static string BuildPreview(
        string configuration,
        IReadOnlyList<DuplicateConfigIssue> issues,
        ConfigurationPreviewText text)
    {
        if (issues.Count == 0) return configuration;

        var builder = new StringBuilder();
        builder.AppendLine("! " + text.Heading);
        builder.AppendLine("! " + string.Format(System.Globalization.CultureInfo.CurrentCulture, text.ResultFormat, issues.Count));
        builder.AppendLine("! " + text.Recommendation);
        builder.AppendLine("!");

        foreach (var issue in issues.Take(25))
        {
            builder.AppendLine("! " + string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                text.IssueFormat,
                issue.Context,
                issue.Command,
                issue.Count,
                string.Join(", ", issue.Lines)));
        }

        if (issues.Count > 25)
            builder.AppendLine("! " + string.Format(System.Globalization.CultureInfo.CurrentCulture, text.MoreIssuesFormat, issues.Count - 25));

        builder.AppendLine("!");
        builder.Append(configuration);
        return builder.ToString();
    }

    private static IReadOnlyList<DuplicateConfigIssue> FindDuplicateConfigIssues(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration)) return Array.Empty<DuplicateConfigIssue>();

        var occurrences = new Dictionary<string, (string Context, string Command, List<int> Lines)>(StringComparer.OrdinalIgnoreCase);
        var currentContext = "global";
        var lines = configuration.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (IsContextReset(trimmed))
            {
                currentContext = "global";
                continue;
            }

            if (ShouldIgnoreDuplicateLine(trimmed)) continue;
            if (TryGetConfigContext(trimmed, currentContext, out var newContext))
            {
                currentContext = newContext;
                continue;
            }

            var normalizedCommand = NormalizeConfigCommand(trimmed);
            if (string.IsNullOrWhiteSpace(normalizedCommand)) continue;

            var key = currentContext + "\u001F" + normalizedCommand;
            if (!occurrences.TryGetValue(key, out var entry))
            {
                entry = (currentContext, normalizedCommand, new List<int>());
                occurrences[key] = entry;
            }
            entry.Lines.Add(index + 1);
        }

        return occurrences.Values
            .Where(item => item.Lines.Count > 1)
            .OrderByDescending(item => item.Lines.Count)
            .ThenBy(item => item.Context, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Command, StringComparer.OrdinalIgnoreCase)
            .Select(item => new DuplicateConfigIssue(item.Context, item.Command, item.Lines.Count, item.Lines))
            .ToList();
    }

    private static bool ShouldIgnoreDuplicateLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("!", StringComparison.OrdinalIgnoreCase)) return true;
        return NormalizeConfigCommand(line) is
            "enable" or "configure terminal" or "conf t" or "end" or "exit" or
            "exit-address-family" or "write memory" or "wr" or "no shutdown";
    }

    private static bool IsContextReset(string line) =>
        NormalizeConfigCommand(line) is "exit" or "exit-address-family" or "end";

    private static bool TryGetConfigContext(string line, string currentContext, out string context)
    {
        var normalized = NormalizeConfigCommand(line);
        string[] headers =
        {
            "interface ", "router ", "line ", "vlan ", "ip dhcp pool ",
            "ip access-list ", "ipv6 access-list ", "route-map ", "class-map ",
            "policy-map ", "crypto isakmp policy ", "crypto ipsec transform-set ",
            "crypto map ", "key chain ", "vrf definition ", "ip vrf ",
            "control-plane", "voice class ", "dial-peer voice ", "telephony-service",
            "call-manager-fallback", "zone security ", "zone-pair security ",
            "parameter-map ", "object-group ", "ip sla ", "track "
        };

        if (headers.Any(header => normalized.StartsWith(header, StringComparison.OrdinalIgnoreCase) || normalized.Equals(header.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            context = normalized;
            return true;
        }

        if (normalized.StartsWith("address-family ", StringComparison.OrdinalIgnoreCase))
        {
            context = currentContext + " > " + normalized;
            return true;
        }

        context = currentContext;
        return false;
    }

    private static string NormalizeConfigCommand(string line) =>
        string.Join(" ", (line ?? string.Empty).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
}
