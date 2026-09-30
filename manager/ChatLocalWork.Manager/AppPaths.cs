namespace ChatLocalWork.Manager;

internal sealed class AppPaths
{
    private AppPaths(string projectRoot)
    {
        ProjectRoot = projectRoot;
        WorkspaceRoot = Directory.GetParent(projectRoot)?.FullName
            ?? throw new InvalidOperationException("Workspace root could not be resolved.");

        LocalWorkMcpRoot = Path.Combine(ProjectRoot, "localworkmcp");
        StartScript = Path.Combine(ProjectRoot, "scripts", "windows", "StartMCP.cjs");
        StopScript = Path.Combine(ProjectRoot, "scripts", "windows", "StopMCP.cjs");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("LOCALAPPDATA could not be resolved.");
        }

        ChatLocalWorkDataDirectory = Path.Combine(localAppData, "ChatLocalWork");
        ManagerSettingsFile = Path.Combine(ChatLocalWorkDataDirectory, "manager-settings.json");
        RunnerQueueDirectory = Path.Combine(ChatLocalWorkDataDirectory, "runtime", "windows-runner");
        RunnerReadyFile = Path.Combine(RunnerQueueDirectory, "state", "ready.json");
        RunnerLogFile = Path.Combine(RunnerQueueDirectory, "logs", "runner.log");
        LauncherLogFile = Path.Combine(RunnerQueueDirectory, "logs", "launcher.log");
    }

    public string ProjectRoot { get; }
    public string RepositoryRoot => ProjectRoot;
    public string LocalWorkMcpRoot { get; }
    public string WorkspaceRoot { get; }
    public string StartScript { get; }
    public string StopScript { get; }
    public string ChatLocalWorkDataDirectory { get; }
    public string ManagerSettingsFile { get; }
    public string RunnerQueueDirectory { get; }
    public string RunnerReadyFile { get; }
    public string RunnerLogFile { get; }
    public string LauncherLogFile { get; }

    public static AppPaths Discover()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            var startScript = Path.Combine(current.FullName, "scripts", "windows", "StartMCP.cjs");
            var composeFile = Path.Combine(current.FullName, "localworkmcp", "tunneling", "docker-compose.yml");

            if (File.Exists(startScript) && File.Exists(composeFile))
            {
                return new AppPaths(current.FullName);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "ChatLocalWork project root was not found. " +
            "Run the Manager from a build located inside the ChatLocalWork repository.");
    }
}
