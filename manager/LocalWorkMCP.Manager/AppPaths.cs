namespace LocalWorkMCP.Manager;

internal sealed class AppPaths
{
    private AppPaths(string repositoryRoot)
    {
        RepositoryRoot = repositoryRoot;
        WorkspaceRoot = Directory.GetParent(repositoryRoot)?.FullName
            ?? throw new InvalidOperationException("Workspace root could not be resolved.");

        StartScript = Path.Combine(RepositoryRoot, "scripts", "windows", "StartMCP.cjs");
        StopScript = Path.Combine(RepositoryRoot, "scripts", "windows", "StopMCP.cjs");
        RunnerQueueDirectory = Path.Combine(WorkspaceRoot, ".windows-runner");
        RunnerReadyFile = Path.Combine(RunnerQueueDirectory, "state", "ready.json");
        RunnerLogFile = Path.Combine(RunnerQueueDirectory, "logs", "runner.log");
        LauncherLogFile = Path.Combine(RunnerQueueDirectory, "logs", "launcher.log");
    }

    public string RepositoryRoot { get; }
    public string WorkspaceRoot { get; }
    public string StartScript { get; }
    public string StopScript { get; }
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
            var composeFile = Path.Combine(current.FullName, "tunneling", "docker-compose.yml");

            if (File.Exists(startScript) && File.Exists(composeFile))
            {
                return new AppPaths(current.FullName);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "LocalWorkMCP repository root was not found. " +
            "Run the Manager from a build located inside the LocalWorkMCP repository.");
    }
}
