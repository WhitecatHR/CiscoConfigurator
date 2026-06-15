using System.Text.Json.Serialization;

namespace CiscoConfigGuiWpf;

public sealed class ModuleDefinition
{
    public string Tab { get; set; } = "";
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Devices { get; set; } = new();
    public int Height { get; set; }
    public bool Default { get; set; }
    public List<FieldDefinition> Fields { get; set; } = new();
}

public sealed class FieldDefinition
{
    public string Type { get; set; } = "Text";
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Help { get; set; } = "";
    public List<string> Items { get; set; } = new();
    public int Selected { get; set; }
    public string DependsOnField { get; set; } = "";
    public List<string> VisibleForValues { get; set; } = new();
    public List<string> EnabledForValues { get; set; } = new();
    public bool ReadOnly { get; set; }
    public int W { get; set; } = 300;
    public int H { get; set; } = 120;
}

public sealed class CommandGroup
{
    public string Name { get; set; } = "";
    public List<CommandRow> Rows { get; set; } = new();
}

public sealed class CommandRow
{
    public string Module { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Command { get; set; } = "";
    public string Meaning { get; set; } = "";
}

public sealed class GenerationRequest
{
    public Dictionary<string, string> Values { get; set; } = new();
    public Dictionary<string, bool> Modules { get; set; } = new();
}

public sealed class TemplateData
{
    public Dictionary<string, string> Values { get; set; } = new();
    public Dictionary<string, bool> Modules { get; set; } = new();
}
