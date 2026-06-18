namespace CiscoConfigGuiWpf;

public enum ProjectWorkflowResultCode
{
    Created,
    Loaded,
    Saved,
    DeviceAdded,
    DeviceUpdated,
    VersionCreated,
    VersionSkippedAsDuplicate,
    VersionRestored
}

public sealed class ProjectWorkflowResult
{
    public required ProjectWorkflowResultCode Code { get; init; }
    public required NetworkProject Project { get; init; }
    public string ProjectPath { get; init; } = string.Empty;
    public ProjectDeviceSnapshot? Device { get; init; }
    public ProjectVersionEntry? Version { get; init; }
}

public sealed class ProjectDeviceWorkflowInput
{
    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public required IReadOnlyDictionary<string, bool> Modules { get; init; }
    public required string GeneratedConfiguration { get; init; }
    public string DeviceType { get; init; } = "Router";
    public string ConfigMode { get; init; } = "Ohne VRF";
}

public enum ProjectAutoSaveLoadCode
{
    NotFound,
    Loaded,
    Expired,
    Invalid
}

public sealed class ProjectAutoSaveLoadResult
{
    public required ProjectAutoSaveLoadCode Code { get; init; }
    public AutoSaveState? State { get; init; }
    public Exception? Error { get; init; }
}
