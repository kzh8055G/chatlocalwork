using System.Text.Json;

namespace ChatLocalWork.Manager;

internal sealed record ManagerSettings(
    bool StopMcpOnExit = true,
    string? WorkspaceRoot = null);

internal static class ManagerSettingsStore
{
    public static ManagerSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new ManagerSettings();
            }

            var settings = JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(path));
            return settings ?? new ManagerSettings();
        }
        catch
        {
            return new ManagerSettings();
        }
    }

    public static void Save(string path, ManagerSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
