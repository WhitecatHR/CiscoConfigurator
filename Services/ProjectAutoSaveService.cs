using System.Text;
using System.Text.Json;

namespace CiscoConfigGuiWpf;

public sealed class ProjectAutoSaveService
{
    private readonly object _sync = new();
    private CancellationTokenSource? _pendingCancellation;
    private Task _pendingTask = Task.CompletedTask;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public Exception? LastScheduleError { get; private set; }

    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> saveAction)
    {
        ArgumentNullException.ThrowIfNull(saveAction);
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            _pendingCancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            _pendingCancellation = cancellation;
            _pendingTask = RunScheduledSaveAsync(delay, saveAction, cancellation.Token);
        }
    }

    public void CancelPending()
    {
        lock (_sync)
        {
            _pendingCancellation?.Cancel();
            _pendingCancellation = null;
            _pendingTask = Task.CompletedTask;
        }
    }

    public async Task FlushPendingAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (_sync) pending = _pendingTask;
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveStateAsync(string path, AutoSaveState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(null, nameof(path));

        var fullPath = System.IO.Path.GetFullPath(path);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await System.IO.File.WriteAllTextAsync(fullPath, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectAutoSaveLoadResult> LoadStateAsync(
        string path,
        TimeSpan maximumAge,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return new ProjectAutoSaveLoadResult { Code = ProjectAutoSaveLoadCode.NotFound };

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<AutoSaveState>(json, JsonOptions);
            if (state == null)
                return new ProjectAutoSaveLoadResult { Code = ProjectAutoSaveLoadCode.Invalid };
            if (DateTime.UtcNow - state.SavedUtc > maximumAge)
                return new ProjectAutoSaveLoadResult { Code = ProjectAutoSaveLoadCode.Expired, State = state };
            return new ProjectAutoSaveLoadResult { Code = ProjectAutoSaveLoadCode.Loaded, State = state };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProjectAutoSaveLoadResult
            {
                Code = ProjectAutoSaveLoadCode.Invalid,
                Error = ex
            };
        }
    }

    private async Task RunScheduledSaveAsync(
        TimeSpan delay,
        Func<CancellationToken, Task> saveAction,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await saveAction(cancellationToken);
            LastScheduleError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LastScheduleError = ex;
            StartupDiagnostics.WriteWarning($"Scheduled project autosave failed: {ex.Message}");
        }
    }
}
