namespace CiscoConfigGuiWpf;

public enum ImportResultCode
{
    Success,
    EmptyConfiguration,
    UnknownCommandsDetected,
    NoApplicableFields
}

public sealed class ImportResult
{
    public required ImportedConfigAnalysis Analysis { get; init; }
    public required string Preview { get; init; }
    public required string UnknownCommandsText { get; init; }
    public required IReadOnlyList<ImportResultCode> Codes { get; init; }
    public bool HasUnknownCommands => Analysis.UnknownCommands.Count > 0;
}

public sealed class ImportApplicationPlan
{
    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public required IReadOnlyDictionary<string, bool> Modules { get; init; }
    public required IReadOnlySet<string> FieldsToReset { get; init; }
}

public sealed class ImportWorkflowText
{
    public string SummaryHeading { get; init; } = string.Empty;
    public string TotalCommands { get; init; } = string.Empty;
    public string KnownCommands { get; init; } = string.Empty;
    public string ApplicableFields { get; init; } = string.Empty;
    public string ApplicableModules { get; init; } = string.Empty;
    public string UnknownCommands { get; init; } = string.Empty;
    public string NotesHeading { get; init; } = string.Empty;
    public string EmptyConfigurationNote { get; init; } = string.Empty;
    public string UnknownCommandsNoteFormat { get; init; } = string.Empty;
    public string NoApplicableFieldsNote { get; init; } = string.Empty;
    public string RecognizedModulesHeading { get; init; } = string.Empty;
    public string ApplicableFieldsHeading { get; init; } = string.Empty;
    public string UnknownCommandsHeading { get; init; } = string.Empty;
    public string NoUnknownCommands { get; init; } = string.Empty;
    public string UnknownCommandLineFormat { get; init; } = string.Empty;
    public string MoreUnknownCommandsFormat { get; init; } = string.Empty;
    public string UnknownExportHeading { get; init; } = string.Empty;
    public string CountLabel { get; init; } = string.Empty;
}
