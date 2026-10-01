namespace ChatLocalWork.Manager;

internal static class ExecutableLocator
{
    public static string? Find(string executableName, params string?[] preferredPaths)
    {
        foreach (var candidate in preferredPaths)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDirectory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = rawDirectory.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    public static string? FindNode(string? configuredPath = null)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var portableNode = string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "ChatLocalWork", "tools", "node", "node.exe");

        return Find(
            "node.exe",
            configuredPath,
            portableNode,
            @"C:\nvm4w\nodejs\node.exe",
            Path.Combine(programFiles, "nodejs", "node.exe"));
    }

    public static string? FindDocker()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return Find(
            "docker.exe",
            Path.Combine(programFiles, "Docker", "Docker", "resources", "bin", "docker.exe"));
    }

    public static string? FindTailscale()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return Find(
            "tailscale.exe",
            Path.Combine(programFiles, "Tailscale", "tailscale.exe"),
            Path.Combine(programFilesX86, "Tailscale", "tailscale.exe"));
    }
}
