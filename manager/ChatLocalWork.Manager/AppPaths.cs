using System.Text.Json;

namespace ChatLocalWork.Manager;

internal sealed class AppPaths
{
    private sealed class InstalledLayoutConfig
    {
        public string? AppRoot { get; set; }
        public string? WorkspaceRoot { get; set; }
        public string? NodePath { get; set; }
        public string? AppVersion { get; set; }
    }

    private AppPaths(
        string appRoot,
        string workspaceRoot,
        string? nodePath,
        string? appVersion,
        string dataDirectory,
        string appConfigFile,
        bool installedLayout)
    {
        AppRoot = Path.GetFullPath(appRoot);
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(WorkspaceRoot);
        NodePath = string.IsNullOrWhiteSpace(nodePath) ? null : Path.GetFullPath(nodePath);
        AppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim();

        LocalWorkMcpRoot = Path.Combine(AppRoot, "localworkmcp");
        StartScript = Path.Combine(AppRoot, "scripts", "windows", "StartMCP.cjs");
        StopScript = Path.Combine(AppRoot, "scripts", "windows", "StopMCP.cjs");

        ChatLocalWorkDataDirectory = dataDirectory;
        AppConfigFile = appConfigFile;
        ManagerSettingsFile = Path.Combine(ChatLocalWorkDataDirectory, "manager-settings.json");
        RunnerQueueDirectory = Path.Combine(ChatLocalWorkDataDirectory, "runtime", "windows-runner");
        RunnerReadyFile = Path.Combine(RunnerQueueDirectory, "state", "ready.json");
        RunnerLogFile = Path.Combine(RunnerQueueDirectory, "logs", "runner.log");
        LauncherLogFile = Path.Combine(RunnerQueueDirectory, "logs", "launcher.log");
        InstalledLayout = installedLayout;
    }

    public string AppRoot { get; }
    public string ProjectRoot => AppRoot;
    public string RepositoryRoot => AppRoot;
    public string LocalWorkMcpRoot { get; }
    public string WorkspaceRoot { get; }
    public string? NodePath { get; }
    public string? AppVersion { get; }
    public string StartScript { get; }
    public string StopScript { get; }
    public string ChatLocalWorkDataDirectory { get; }
    public string AppConfigFile { get; }
    public string ManagerSettingsFile { get; }
    public string RunnerQueueDirectory { get; }
    public string RunnerReadyFile { get; }
    public string RunnerLogFile { get; }
    public string LauncherLogFile { get; }
    public bool InstalledLayout { get; }

    public static AppPaths Discover()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("LOCALAPPDATA could not be resolved.");
        }

        var dataDirectory = Path.Combine(localAppData, "ChatLocalWork");
        var appConfigFile = Path.Combine(dataDirectory, "config", "app-config.json");
        var managerSettingsFile = Path.Combine(dataDirectory, "manager-settings.json");
        var managerSettings = ManagerSettingsStore.Load(managerSettingsFile);

        if (File.Exists(appConfigFile))
        {
            var config = LoadInstalledLayoutConfig(appConfigFile);
            var appRoot = RequireAbsolutePath(config.AppRoot, "appRoot", appConfigFile);
            var workspaceRoot = ResolveWorkspaceRoot(
                RequireAbsolutePath(config.WorkspaceRoot, "workspaceRoot", appConfigFile),
                managerSettings.WorkspaceRoot);
            var nodePath = OptionalAbsolutePath(config.NodePath, "nodePath", appConfigFile);

            return new AppPaths(
                appRoot,
                workspaceRoot,
                nodePath,
                config.AppVersion,
                dataDirectory,
                appConfigFile,
                installedLayout: true);
        }

        var developmentAppRoot = TryDiscoverDevelopmentAppRoot();
        if (developmentAppRoot is not null)
        {
            var developmentWorkspaceRoot = Directory.GetParent(developmentAppRoot)?.FullName
                ?? throw new InvalidOperationException("Workspace root could not be resolved.");
            developmentWorkspaceRoot = ResolveWorkspaceRoot(
                developmentWorkspaceRoot,
                managerSettings.WorkspaceRoot);

            return new AppPaths(
                developmentAppRoot,
                developmentWorkspaceRoot,
                nodePath: null,
                appVersion: null,
                dataDirectory,
                appConfigFile,
                installedLayout: false);
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var defaultAppRoot = Path.Combine(dataDirectory, "app", "current");
        var defaultWorkspaceRoot = ResolveWorkspaceRoot(
            Path.Combine(documents, "ChatLocalWorkWorkspace"),
            managerSettings.WorkspaceRoot);
        var defaultNodePath = Path.Combine(dataDirectory, "tools", "node", "node.exe");

        return new AppPaths(
            defaultAppRoot,
            defaultWorkspaceRoot,
            defaultNodePath,
            appVersion: null,
            dataDirectory,
            appConfigFile,
            installedLayout: true);
    }

    private static string ResolveWorkspaceRoot(string defaultWorkspaceRoot, string? overrideWorkspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(overrideWorkspaceRoot))
        {
            return defaultWorkspaceRoot;
        }

        if (!Path.IsPathFullyQualified(overrideWorkspaceRoot))
        {
            return defaultWorkspaceRoot;
        }

        return Path.GetFullPath(overrideWorkspaceRoot);
    }

    private static InstalledLayoutConfig LoadInstalledLayoutConfig(string appConfigFile)
    {
        try
        {
            var json = File.ReadAllText(appConfigFile);
            return JsonSerializer.Deserialize<InstalledLayoutConfig>(
                       json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("Configuration is empty.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"ChatLocalWork app configuration could not be read: {appConfigFile}",
                ex);
        }
    }

    private static string RequireAbsolutePath(string? value, string propertyName, string appConfigFile)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"'{propertyName}' is required in {appConfigFile}.");
        }

        return ValidateAbsolutePath(value, propertyName, appConfigFile);
    }

    private static string? OptionalAbsolutePath(string? value, string propertyName, string appConfigFile)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ValidateAbsolutePath(value, propertyName, appConfigFile);
    }

    private static string ValidateAbsolutePath(string value, string propertyName, string appConfigFile)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException(
                $"'{propertyName}' must be an absolute path in {appConfigFile}.");
        }

        return Path.GetFullPath(value);
    }

    public bool HasValidAppRoot()
    {
        return File.Exists(StartScript) &&
               File.Exists(Path.Combine(LocalWorkMcpRoot, "tunneling", "docker-compose.yml"));
    }

    private static string? TryDiscoverDevelopmentAppRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            var startScript = Path.Combine(current.FullName, "scripts", "windows", "StartMCP.cjs");
            var composeFile = Path.Combine(current.FullName, "localworkmcp", "tunneling", "docker-compose.yml");

            if (File.Exists(startScript) && File.Exists(composeFile))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
