namespace ChatLocalWork.Installer;

internal sealed record InstallPaths(
    string DataRoot,
    string ToolsRoot,
    string NodeRoot,
    string NodeExe,
    string AppDirectory,
    string AppRoot,
    string ConfigDirectory,
    string AppConfigFile,
    string WorkspaceRoot,
    string TempRoot)
{
    public static InstallPaths Create(InstallerOptions options)
    {
        var dataRoot = options.DataRoot ?? DefaultDataRoot();
        var workspaceRoot = options.WorkspaceRoot ?? DefaultWorkspaceRoot();

        return new InstallPaths(
            dataRoot,
            Path.Combine(dataRoot, "tools"),
            Path.Combine(dataRoot, "tools", "node"),
            Path.Combine(dataRoot, "tools", "node", "node.exe"),
            Path.Combine(dataRoot, "app"),
            Path.Combine(dataRoot, "app", "current"),
            Path.Combine(dataRoot, "config"),
            Path.Combine(dataRoot, "config", "app-config.json"),
            workspaceRoot,
            Path.Combine(dataRoot, "installer-cache"));
    }

    private static string DefaultDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("LOCALAPPDATA could not be resolved.");
        }

        return Path.Combine(localAppData, "ChatLocalWork");
    }

    private static string DefaultWorkspaceRoot()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(documents))
        {
            throw new InvalidOperationException("Documents/UserProfile could not be resolved.");
        }

        return Path.Combine(documents, "ChatLocalWorkWorkspace");
    }
}
