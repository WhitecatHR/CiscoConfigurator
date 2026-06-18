using System.Text;

namespace CiscoConfigGuiWpf;

public sealed class ImportWorkflowService
{
    public async Task<string> LoadConfigurationFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(null, nameof(path));
        cancellationToken.ThrowIfCancellationRequested();
        return await System.IO.File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public ImportResult Analyze(
        string? configuration,
        IReadOnlyDictionary<string, string> moduleTitles,
        ImportWorkflowText text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moduleTitles);
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var analysis = ImportedConfigAnalyzer.Analyze(configuration);
        var codes = BuildResultCodes(configuration, analysis);
        return new ImportResult
        {
            Analysis = analysis,
            Codes = codes,
            Preview = BuildPreview(analysis, moduleTitles, codes, text),
            UnknownCommandsText = BuildUnknownCommandsText(analysis, text)
        };
    }

    public ImportApplicationPlan CreateApplicationPlan(
        ImportResult result,
        IEnumerable<ModuleDefinition> moduleDefinitions)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(moduleDefinitions);

        var activeModules = result.Analysis.Modules
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resetFields = moduleDefinitions
            .Where(module => activeModules.Contains(module.Name))
            .SelectMany(module => module.Fields)
            .Select(field => field.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ImportApplicationPlan
        {
            Values = new Dictionary<string, string>(result.Analysis.Values, StringComparer.OrdinalIgnoreCase),
            Modules = new Dictionary<string, bool>(result.Analysis.Modules, StringComparer.OrdinalIgnoreCase),
            FieldsToReset = resetFields
        };
    }

    public string GetUnknownCommandsCopyText(ImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.UnknownCommandsText;
    }

    public async Task ExportUnknownCommandsAsync(
        string path,
        ImportResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(null, nameof(path));

        var fullPath = System.IO.Path.GetFullPath(path);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        await System.IO.File.WriteAllTextAsync(fullPath, result.UnknownCommandsText, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<ImportResultCode> BuildResultCodes(string? configuration, ImportedConfigAnalysis analysis)
    {
        var codes = new List<ImportResultCode>();
        if (string.IsNullOrWhiteSpace(configuration)) codes.Add(ImportResultCode.EmptyConfiguration);
        if (analysis.UnknownCommands.Count > 0) codes.Add(ImportResultCode.UnknownCommandsDetected);
        if (!string.IsNullOrWhiteSpace(configuration) && analysis.Values.Count == 0) codes.Add(ImportResultCode.NoApplicableFields);
        if (codes.Count == 0) codes.Add(ImportResultCode.Success);
        return codes;
    }

    private static string BuildPreview(
        ImportedConfigAnalysis analysis,
        IReadOnlyDictionary<string, string> moduleTitles,
        IReadOnlyList<ImportResultCode> codes,
        ImportWorkflowText text)
    {
        var builder = new StringBuilder();
        builder.AppendLine(text.SummaryHeading);
        builder.AppendLine(new string('=', Math.Max(3, text.SummaryHeading.Length)));
        builder.AppendLine($"{text.TotalCommands,-25}: {analysis.TotalCommands}");
        builder.AppendLine($"{text.KnownCommands,-25}: {analysis.KnownCommands}");
        builder.AppendLine($"{text.ApplicableFields,-25}: {analysis.AppliedFields}");
        builder.AppendLine($"{text.ApplicableModules,-25}: {analysis.ActiveModules}");
        builder.AppendLine($"{text.UnknownCommands,-25}: {analysis.UnknownCommands.Count}");
        builder.AppendLine();

        var notes = BuildNotes(codes, analysis, text);
        if (notes.Count > 0)
        {
            builder.AppendLine(text.NotesHeading);
            builder.AppendLine(new string('-', Math.Max(3, text.NotesHeading.Length)));
            foreach (var note in notes) builder.AppendLine("- " + note);
            builder.AppendLine();
        }

        builder.AppendLine(text.RecognizedModulesHeading);
        builder.AppendLine(new string('-', Math.Max(3, text.RecognizedModulesHeading.Length)));
        foreach (var moduleName in analysis.Modules.Where(pair => pair.Value).Select(pair => pair.Key).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var title = moduleTitles.TryGetValue(moduleName, out var localizedTitle) ? localizedTitle : moduleName;
            builder.AppendLine($"- {title} [{moduleName}]");
        }
        builder.AppendLine();

        builder.AppendLine(text.ApplicableFieldsHeading);
        builder.AppendLine(new string('-', Math.Max(3, text.ApplicableFieldsHeading.Length)));
        foreach (var pair in analysis.Values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var preview = (pair.Value ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ");
            if (preview.Length > 140) preview = preview[..140] + " ...";
            builder.AppendLine($"{pair.Key}: {preview}");
        }
        builder.AppendLine();

        builder.AppendLine(text.UnknownCommandsHeading);
        builder.AppendLine(new string('-', Math.Max(3, text.UnknownCommandsHeading.Length)));
        if (analysis.UnknownCommands.Count == 0)
        {
            builder.AppendLine(text.NoUnknownCommands);
        }
        else
        {
            foreach (var item in analysis.UnknownCommands.Take(300))
            {
                builder.AppendLine(string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    text.UnknownCommandLineFormat,
                    item.LineNumber,
                    item.Context,
                    item.Command));
            }
            if (analysis.UnknownCommands.Count > 300)
                builder.AppendLine(string.Format(System.Globalization.CultureInfo.CurrentCulture, text.MoreUnknownCommandsFormat, analysis.UnknownCommands.Count - 300));
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildNotes(
        IReadOnlyList<ImportResultCode> codes,
        ImportedConfigAnalysis analysis,
        ImportWorkflowText text)
    {
        var notes = new List<string>();
        if (codes.Contains(ImportResultCode.EmptyConfiguration)) notes.Add(text.EmptyConfigurationNote);
        if (codes.Contains(ImportResultCode.UnknownCommandsDetected))
            notes.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture, text.UnknownCommandsNoteFormat, analysis.UnknownCommands.Count));
        if (codes.Contains(ImportResultCode.NoApplicableFields)) notes.Add(text.NoApplicableFieldsNote);
        return notes;
    }

    private static string BuildUnknownCommandsText(ImportedConfigAnalysis analysis, ImportWorkflowText text)
    {
        var builder = new StringBuilder();
        builder.AppendLine(text.UnknownExportHeading);
        builder.AppendLine(new string('=', Math.Max(3, text.UnknownExportHeading.Length)));
        builder.AppendLine($"{text.CountLabel}: {analysis.UnknownCommands.Count}");
        builder.AppendLine();
        foreach (var item in analysis.UnknownCommands)
        {
            builder.AppendLine(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                text.UnknownCommandLineFormat,
                item.LineNumber,
                item.Context,
                item.Command));
        }
        return builder.ToString();
    }
}
