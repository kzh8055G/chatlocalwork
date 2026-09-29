namespace LocalWorkMCP.Manager;

internal sealed class MpcLifecycleService
{
    private readonly AppPaths _paths;

    public MpcLifecycleService(AppPaths paths)
    {
        _paths = paths;
    }

    public Task<ProcessResult> StartAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        return RunScriptAsync(_paths.StartScript, TimeSpan.FromMinutes(7), onOutput, cancellationToken);
    }

    public Task<ProcessResult> StopAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        return RunScriptAsync(_paths.StopScript, TimeSpan.FromMinutes(4), onOutput, cancellationToken);
    }

    private Task<ProcessResult> RunScriptAsync(
        string script,
        TimeSpan timeout,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(script))
        {
            throw new FileNotFoundException("MCP lifecycle script was not found.", script);
        }

        var node = ExecutableLocator.FindNode()
            ?? throw new InvalidOperationException(
                "node.exe was not found. Install Node.js or add it to PATH.");

        return ProcessRunner.RunAsync(
            node,
            new[] { script },
            _paths.RepositoryRoot,
            timeout,
            onOutput,
            cancellationToken);
    }
}
