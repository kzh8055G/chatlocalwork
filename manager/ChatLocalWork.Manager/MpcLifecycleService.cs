namespace ChatLocalWork.Manager;

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

        var node = ExecutableLocator.FindNode(_paths.NodePath)
            ?? throw new InvalidOperationException(
                "node.exe was not found. Configure portable Node or install Node.js.");

        var environment = new Dictionary<string, string?>
        {
            ["CHATLOCALWORK_APP_ROOT"] = _paths.AppRoot,
            ["CHATLOCALWORK_WORKSPACE_ROOT"] = _paths.WorkspaceRoot,
            ["CHATLOCALWORK_APP_VERSION"] = _paths.AppVersion,
        };

        return ProcessRunner.RunAsync(
            node,
            new[] { script },
            _paths.AppRoot,
            timeout,
            onOutput,
            cancellationToken,
            environment);
    }
}
