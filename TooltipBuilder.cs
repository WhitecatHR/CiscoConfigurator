using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CiscoConfigGuiWpf;

public static partial class TooltipBuilder
{
    public static ToolTip CreateFieldTip(FieldDefinition field) => CreateDynamic(() =>
    {
        var text = Expand(field.Label, field.Help);
        text += BuildFieldSyntaxHint(field);

        if (ApplicationSettingsService.Current.DeveloperMode)
        {
            var catalog = TooltipCatalog.Current;
            var visibleFor = field.VisibleForValues.Count == 0 ? "-" : string.Join(", ", field.VisibleForValues);
            var enabledFor = field.EnabledForValues.Count == 0 ? "-" : string.Join(", ", field.EnabledForValues);
            text += "\n\n" + string.Format(
                CultureInfo.CurrentCulture,
                catalog.Text("developer.fieldDetails"),
                field.Name,
                field.Type,
                string.IsNullOrWhiteSpace(field.DependsOnField) ? "-" : field.DependsOnField,
                visibleFor,
                enabledFor);
        }

        return (field.Label, text);
    });

    public static ToolTip CreateModuleTip(ModuleDefinition module) => CreateDynamic(() =>
    {
        var text = ModuleHelp(module);

        if (ApplicationSettingsService.Current.DeveloperMode)
        {
            var catalog = TooltipCatalog.Current;
            text += "\n\n" + string.Format(
                CultureInfo.CurrentCulture,
                catalog.Text("developer.moduleDetails"),
                module.Name,
                module.Tab,
                string.Join(", ", module.Devices),
                module.Fields.Count);
        }

        return (module.Title, text);
    });

    public static ToolTip Create(string title, string body) => CreateDynamic(() => (title, body));

    private static ToolTip CreateDynamic(Func<(string Title, string Body)> contentFactory)
    {
        var toolTip = new ToolTip
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            HasDropShadow = true
        };

        void Refresh()
        {
            var source = contentFactory();
            var translatedTitle = LocalizationService.TranslateText(source.Title);
            var translatedBody = LocalizationService.TranslateText(source.Body);

            var panel = new StackPanel { MaxWidth = 760, Margin = new Thickness(4) };
            panel.Children.Add(new TextBlock
            {
                Text = translatedTitle,
                Tag = new LocalizationSource(source.Title),
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
                Text = translatedBody,
                Tag = new LocalizationSource(source.Body),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 13,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap
            });
            toolTip.Content = panel;
        }

        toolTip.Opened += (_, _) => Refresh();
        Refresh();
        return toolTip;
    }

    private static string ModuleHelp(ModuleDefinition module)
    {
        var catalog = TooltipCatalog.Current;
        var baseText = catalog.ModuleHelp.TryGetValue(module.Name, out var text)
            ? text
            : string.Format(CultureInfo.CurrentCulture, catalog.Text("module.generic"), module.Title);

        return (baseText.Trim() + BuildModuleCommandDetails(module)).Trim();
    }

    private static string Expand(string label, string help)
    {
        var catalog = TooltipCatalog.Current;
        var text = string.IsNullOrWhiteSpace(help)
            ? catalog.Text("field.noDescription")
            : help.Trim();

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var purposeHeading = catalog.Text("heading.purpose");
        if (!text.StartsWith(purposeHeading, StringComparison.OrdinalIgnoreCase))
            text = purposeHeading + "\n" + text;

        if (label.Contains('|'))
        {
            text += "\n\n" + catalog.Text("heading.format") + "\n" + label.Replace("|", " | ");
            text += "\n\n" + catalog.Text("heading.separator") + "\n" + catalog.Text("field.separatorDescription");
        }

        return text.Trim();
    }

    private static string BuildModuleCommandDetails(ModuleDefinition module)
    {
        var rows = GetRelatedCommandRows(module).ToList();
        if (rows.Count == 0) return string.Empty;

        var catalog = TooltipCatalog.Current;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine(catalog.Text("heading.possibleCommands"));

        foreach (var row in rows.Take(14))
            sb.AppendLine($"• {row.Command} — {row.Meaning}");

        if (rows.Count > 14)
            sb.AppendLine(string.Format(CultureInfo.CurrentCulture, catalog.Text("command.more"), rows.Count - 14));

        var parameterLines = rows
            .SelectMany(r => ExtractParameters(r.Command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .Select(parameter => $"• <{parameter}> = {ExplainParameter(parameter)}")
            .ToList();

        if (parameterLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(catalog.Text("heading.commandParts"));
            foreach (var line in parameterLines)
                sb.AppendLine(line);
        }

        var optionalParts = rows
            .SelectMany(row => ExtractOptionalParts(row.Command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        if (optionalParts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(catalog.Text("heading.optionalParts"));
            foreach (var part in optionalParts)
                sb.AppendLine($"• [{part}] = {catalog.Text("optionalPart.description")}");
        }

        return sb.ToString();
    }

    private static string BuildFieldSyntaxHint(FieldDefinition field)
    {
        var placeholder = GuessPlaceholder(field);
        if (placeholder.Length == 0) return string.Empty;

        var rows = ConfigurationCommandCatalog.All
            .SelectMany(group => group.Rows)
            .Where(row => row.Command.Contains("<" + placeholder + ">", StringComparison.OrdinalIgnoreCase)
                          || ExtractParameters(row.Command).Any(parameter => SameParameter(parameter, placeholder)))
            .Take(5)
            .ToList();

        if (rows.Count == 0) return string.Empty;

        var catalog = TooltipCatalog.Current;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine(catalog.Text("heading.commandParts"));
        sb.AppendLine($"• <{placeholder}> = {ExplainParameter(placeholder)}");
        sb.AppendLine();
        sb.AppendLine(catalog.Text("heading.exampleSyntax"));
        foreach (var row in rows)
            sb.AppendLine($"• {row.Command}");
        return sb.ToString();
    }

    private static IEnumerable<CommandRow> GetRelatedCommandRows(ModuleDefinition module)
    {
        var aliases = GetCommandAliases(module);
        return ConfigurationCommandCatalog.All
            .SelectMany(group => group.Rows)
            .Where(row => aliases.Any(alias => row.Module.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(row => row.Command, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static string[] GetCommandAliases(ModuleDefinition module)
    {
        if (TooltipCatalog.Current.CommandAliases.TryGetValue(module.Name, out var aliases))
            return aliases;

        return new[] { module.Title };
    }

    private static IEnumerable<string> ExtractParameters(string command)
    {
        foreach (Match match in Regex.Matches(command, "<([^>]+)>", RegexOptions.CultureInvariant))
            yield return match.Groups[1].Value.Trim();
    }

    private static IEnumerable<string> ExtractOptionalParts(string command)
    {
        foreach (Match match in Regex.Matches(command, @"\[([^\]]+)\]", RegexOptions.CultureInvariant))
            yield return match.Groups[1].Value.Trim();
    }

    private static string GuessPlaceholder(FieldDefinition field)
    {
        var name = field.Name.ToLowerInvariant();
        var label = field.Label.ToLowerInvariant();

        if (name.Contains("hostname") || label.Contains("hostname")) return "Hostname";
        if (name == "domain" || label.Contains("domain")) return "Domain";
        if (name.Contains("secret") || label.Contains("secret")) return "Secret";
        if (name.Contains("user") || label.Contains("benutzer") || label.Contains("user")) return "User";
        if (name.Contains("password") || label.Contains("passwort") || label.Contains("password")) return "Passwort";
        if (name.Contains("vlan") && (name.Contains("list") || label.Contains("liste") || label.Contains("list"))) return "VLAN-Liste";
        if (name.Contains("vlan")) return "VLAN-ID";
        if (name.Contains("vrf")) return "VRF";
        if (name.Contains("ospf") && (name.Contains("pid") || name.Contains("process") || label.Contains("prozess") || label.Contains("process"))) return "OSPF-Prozess";
        if (name.Contains("ospf") && name.Contains("rid")) return "Router-ID";
        if (name.Contains("bgp") && name.Contains("as")) return "AS";
        if (name.Contains("rid") || label.Contains("router-id")) return "Router-ID";
        if (name.Contains("acl")) return "ACL-Name";
        if (name.Contains("route") && name.Contains("map")) return "Route-Map";
        if (name.Contains("prefix")) return "Prefix-List";
        if (name.Contains("source") || name.Contains("src")) return "Quelle";
        if (name.Contains("dst") || name.Contains("destination")) return "Ziel";
        if (name.Contains("port")) return "Port";
        if (name.Contains("protocol") || label.Contains("protokoll") || label.Contains("protocol")) return "Protokoll";
        if (name.Contains("if") || label.Contains("interface")) return "Interface";
        if (name.Contains("ip") || label.Contains("ip")) return "IP";
        if (name.Contains("mask") || label.Contains("maske") || label.Contains("mask")) return "Maske";
        if (name.Contains("wildcard")) return "Wildcard";
        if (name.Contains("area")) return "Area";
        if (name.Contains("zone")) return "Zone";
        if (name.Contains("pool")) return "Pool-Name";
        if (name.Contains("policy")) return "Policy-Map";
        if (name.Contains("class")) return "Class-Map";
        if (name.Contains("seconds") || label.Contains("sek") || label.Contains("seconds")) return "Sekunden";
        if (name.Contains("timeout")) return "Sekunden";
        if (name.Contains("level")) return "Level";
        if (name.Contains("name")) return "Name";

        return string.Empty;
    }

    private static bool SameParameter(string first, string second) =>
        NormalizeParameter(first) == NormalizeParameter(second);

    private static string NormalizeParameter(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9äöüß]+", string.Empty, RegexOptions.CultureInvariant);

    private static string ExplainParameter(string parameter)
    {
        var catalog = TooltipCatalog.Current;
        var original = parameter.Trim();
        var normalized = NormalizeParameter(original);

        if (original.Contains('|'))
        {
            var choice = original.Replace("|", LocalizationService.IsEnglish ? " or " : " oder ");
            return string.Format(CultureInfo.CurrentCulture, catalog.Text("parameter.choice"), choice);
        }

        var key = ClassifyParameter(normalized);
        return catalog.Parameter(key);
    }

    private static string ClassifyParameter(string normalized)
    {
        if (normalized.Contains("hostname")) return "hostname";
        if (normalized.Contains("domain")) return "domain";
        if (normalized.Contains("secret") || normalized.Contains("psk") || normalized.Contains("sharedsecret")) return "secret";
        if (normalized.Contains("password") || normalized.Contains("passwort")) return "password";
        if (normalized == "user" || normalized.Contains("username")) return "user";
        if (normalized.Contains("interfacerange")) return "interfaceRange";
        if (normalized.Contains("interface") || normalized == "if" || normalized.EndsWith("if", StringComparison.Ordinal)) return "interface";
        if (normalized.Contains("netz") || normalized.Contains("network") || normalized.Contains("zielnetz")) return "network";
        if (normalized.Contains("maske") || normalized.Contains("mask")) return "mask";
        if (normalized.Contains("wildcard")) return "wildcard";
        if (normalized.Contains("vlanliste")) return "vlanList";
        if (normalized.Contains("voicevlan")) return "voiceVlan";
        if (normalized.Contains("datavlan")) return "dataVlan";
        if (normalized.Contains("providervlan")) return "providerVlan";
        if (normalized.Contains("customervlan")) return "customerVlan";
        if (normalized.Contains("tftp")) return "tftp";
        if (normalized.Contains("nativevlan")) return "nativeVlan";
        if (normalized.Contains("vlan")) return "vlan";
        if (normalized.Contains("vrf")) return "vrf";
        if (normalized.Contains("routetarget") || normalized == "rt") return "routeTarget";
        if (normalized.Contains("rd")) return "rd";
        if (normalized.Contains("neighbor")) return "neighbor";
        if (normalized.Contains("label")) return "label";
        if (normalized == "as" || normalized.Contains("asn") || normalized.Contains("remoteas")) return "asn";
        if (normalized.Contains("ospfprozess")) return "ospfProcess";
        if (normalized.Contains("routerid")) return "routerId";
        if (normalized.Contains("area")) return "area";
        if (normalized.Contains("acl")) return "acl";
        if (normalized.Contains("routemap")) return "routeMap";
        if (normalized.Contains("prefixlist")) return "prefixList";
        if (normalized.Contains("port")) return "port";
        if (normalized.Contains("protokoll") || normalized.Contains("protocol")) return "protocol";
        if (normalized.Contains("quelle") || normalized.Contains("source") || normalized.Contains("src")) return "source";
        if (normalized.Contains("ziel") || normalized.Contains("destination") || normalized.Contains("dst")) return "destination";
        if (normalized.Contains("zone")) return "zone";
        if (normalized.Contains("classmap")) return "classMap";
        if (normalized.Contains("policymap")) return "policyMap";
        if (normalized.Contains("transformset")) return "transformSet";
        if (normalized.Contains("pool")) return "pool";
        if (normalized.Contains("sekunden") || normalized.Contains("seconds")) return "seconds";
        if (normalized.Contains("minuten") || normalized.Contains("minutes")) return "minutes";
        if (normalized.Contains("bytes")) return "bytes";
        if (normalized.Contains("level")) return "level";
        if (normalized.Contains("prioritaet") || normalized.Contains("priority")) return "priority";
        if (normalized.Contains("prozent") || normalized.Contains("percent")) return "percent";
        if (normalized.Contains("dscp")) return "dscp";
        if (normalized.Contains("beschreibung") || normalized.Contains("description")) return "description";
        if (normalized.Contains("ip") || normalized.Contains("peer") || normalized.Contains("next") || normalized.Contains("gateway") || normalized.Contains("server")) return "ip";
        if (normalized.Contains("name") || normalized.Contains("id") || normalized.Contains("nummer")) return "name";
        return "default";
    }
}
