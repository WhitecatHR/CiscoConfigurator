using System.IO;
using System.Text;
using System.Text.Json;

namespace CiscoConfigGuiWpf;

public static class ProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Save(NetworkProject project, string path)
    {
        project.ModifiedUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(project, JsonOptions), new UTF8Encoding(false));
    }

    public static NetworkProject Load(string path)
    {
        var project = JsonSerializer.Deserialize<NetworkProject>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                      ?? throw new InvalidDataException("Die Projektdatei enthält keine gültigen Daten.");
        project.Devices ??= new();
        project.IpamEntries ??= new();
        project.Links ??= new();
        project.Backups ??= new();
        return project;
    }

    public static string AutoSavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CiscoKonfigurator",
        "autosave.ciscoproject.json");
}
