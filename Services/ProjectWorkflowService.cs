namespace CiscoConfigGuiWpf;

public sealed class ProjectWorkflowService
{
    public ProjectWorkflowResult CreateNewProject()
    {
        var project = Normalize(new NetworkProject());
        return new ProjectWorkflowResult
        {
            Code = ProjectWorkflowResultCode.Created,
            Project = project
        };
    }

    public async Task<ProjectWorkflowResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(null, nameof(path));
        cancellationToken.ThrowIfCancellationRequested();
        var project = await ProjectService.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return new ProjectWorkflowResult
        {
            Code = ProjectWorkflowResultCode.Loaded,
            Project = Normalize(project),
            ProjectPath = System.IO.Path.GetFullPath(path)
        };
    }

    public async Task<ProjectWorkflowResult> SaveAsync(
        NetworkProject project,
        string path,
        bool createVersion,
        string versionLabel,
        string versionComment,
        int historyLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(null, nameof(path));
        cancellationToken.ThrowIfCancellationRequested();

        Normalize(project);
        ProjectVersionEntry? version = null;
        if (createVersion)
        {
            version = ProjectVersioningService.CreateVersion(
                project,
                versionLabel,
                versionComment,
                automatic: true,
                historyLimit: historyLimit,
                skipDuplicate: true);
        }

        var fullPath = System.IO.Path.GetFullPath(path);
        await ProjectService.SaveAsync(project, fullPath, cancellationToken).ConfigureAwait(false);
        return new ProjectWorkflowResult
        {
            Code = ProjectWorkflowResultCode.Saved,
            Project = project,
            ProjectPath = fullPath,
            Version = version
        };
    }

    public ProjectWorkflowResult AddCurrentDevice(NetworkProject project, ProjectDeviceWorkflowInput input)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(input);
        Normalize(project);

        var name = GetDeviceName(input.Values, $"DEVICE-{project.Devices.Count + 1}");
        var snapshot = new ProjectDeviceSnapshot
        {
            Name = name,
            DeviceType = input.DeviceType,
            ConfigMode = input.ConfigMode,
            Values = input.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            Modules = input.Modules.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            GeneratedConfiguration = input.GeneratedConfiguration,
            LastUpdatedUtc = DateTime.UtcNow
        };
        project.Devices.Add(snapshot);
        project.ModifiedUtc = DateTime.UtcNow;

        return new ProjectWorkflowResult
        {
            Code = ProjectWorkflowResultCode.DeviceAdded,
            Project = project,
            Device = snapshot
        };
    }

    public ProjectWorkflowResult UpdateDevice(
        NetworkProject project,
        ProjectDeviceSnapshot snapshot,
        ProjectDeviceWorkflowInput input)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(input);
        Normalize(project);

        snapshot.Name = GetDeviceName(input.Values, snapshot.Name);
        snapshot.DeviceType = input.DeviceType;
        snapshot.ConfigMode = input.ConfigMode;
        snapshot.Values = input.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        snapshot.Modules = input.Modules.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        snapshot.GeneratedConfiguration = input.GeneratedConfiguration;
        snapshot.LastUpdatedUtc = DateTime.UtcNow;
        project.ModifiedUtc = DateTime.UtcNow;

        return new ProjectWorkflowResult
        {
            Code = ProjectWorkflowResultCode.DeviceUpdated,
            Project = project,
            Device = snapshot
        };
    }

    public ProjectWorkflowResult CreateVersion(
        NetworkProject project,
        string label,
        string comment,
        bool automatic,
        int historyLimit,
        bool skipDuplicate)
    {
        ArgumentNullException.ThrowIfNull(project);
        Normalize(project);
        var version = ProjectVersioningService.CreateVersion(project, label, comment, automatic, historyLimit, skipDuplicate);
        return new ProjectWorkflowResult
        {
            Code = version == null ? ProjectWorkflowResultCode.VersionSkippedAsDuplicate : ProjectWorkflowResultCode.VersionCreated,
            Project = project,
            Version = version
        };
    }

    public ProjectWorkflowResult RestoreVersion(
        NetworkProject currentProject,
        ProjectVersionEntry selectedVersion,
        string backupLabel,
        string backupComment,
        int historyLimit)
    {
        ArgumentNullException.ThrowIfNull(currentProject);
        ArgumentNullException.ThrowIfNull(selectedVersion);
        Normalize(currentProject);

        ProjectVersioningService.CreateVersion(
            currentProject,
            backupLabel,
            backupComment,
            automatic: true,
            historyLimit: historyLimit,
            skipDuplicate: false);
        var restored = Normalize(ProjectVersioningService.RestoreVersion(currentProject, selectedVersion));
        return new ProjectWorkflowResult
        {
            Code = ProjectWorkflowResultCode.VersionRestored,
            Project = restored,
            Version = selectedVersion
        };
    }

    public NetworkProject Normalize(NetworkProject project) =>
        ProjectService.Normalize(project);

    private static string GetDeviceName(IReadOnlyDictionary<string, string> values, string fallback) =>
        values.TryGetValue("hostname", out var hostname) && !string.IsNullOrWhiteSpace(hostname)
            ? hostname.Trim()
            : fallback;
}
