using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatLocalWork.Manager;

internal enum ComponentState
{
    Ready,
    Stopped,
    Warning,
    Unavailable,
}

internal sealed record ComponentStatus(
    string Name,
    ComponentState State,
    string Detail);

internal sealed class StatusService
{
    private static readonly Regex PortRegex = new(@":(?<port>\d+)\s*$", RegexOptions.Compiled);
    private readonly AppPaths _paths;

    public StatusService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<ComponentStatus>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var runner = GetRunnerStatus();

        var dockerTask = GetDockerStatusAsync(cancellationToken);
        var tailscaleTask = GetTailscaleStatusAsync(cancellationToken);
        var funnelTask = GetFunnelStatusAsync(cancellationToken);

        await Task.WhenAll(dockerTask, tailscaleTask, funnelTask);

        var mcp = await GetMcpStatusAsync(cancellationToken);

        return new[]
        {
            runner,
            await dockerTask,
            await tailscaleTask,
            await funnelTask,
            mcp,
        };
    }

    private ComponentStatus GetRunnerStatus()
    {
        if (!File.Exists(_paths.RunnerReadyFile))
        {
            return new ComponentStatus("Windows Runner", ComponentState.Stopped, "ready.json 없음");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_paths.RunnerReadyFile));
            var root = document.RootElement;

            if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                return new ComponentStatus("Windows Runner", ComponentState.Warning, "ready 상태가 아님");
            }

            if (!root.TryGetProperty("pid", out var pidElement) || !pidElement.TryGetInt32(out var pid))
            {
                return new ComponentStatus("Windows Runner", ComponentState.Warning, "PID 정보 없음");
            }

            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return new ComponentStatus("Windows Runner", ComponentState.Stopped, $"PID {pid} 종료됨");
            }

            return new ComponentStatus("Windows Runner", ComponentState.Ready, $"READY · PID {pid}");
        }
        catch
        {
            return new ComponentStatus("Windows Runner", ComponentState.Stopped, "Runner 프로세스 확인 실패");
        }
    }

    private async Task<ComponentStatus> GetDockerStatusAsync(CancellationToken cancellationToken)
    {
        var docker = ExecutableLocator.FindDocker();
        if (docker is null)
        {
            return new ComponentStatus("Docker", ComponentState.Unavailable, "docker.exe 없음");
        }

        var result = await ProcessRunner.RunAsync(
            docker,
            new[]
            {
                "inspect",
                "-f",
                "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}",
                "workmachine",
            },
            _paths.RepositoryRoot,
            TimeSpan.FromSeconds(8),
            cancellationToken: cancellationToken);

        var state = result.StandardOutput.Trim();
        return state switch
        {
            "healthy" => new ComponentStatus("Docker", ComponentState.Ready, "workmachine HEALTHY"),
            "running" => new ComponentStatus("Docker", ComponentState.Ready, "workmachine RUNNING"),
            _ when result.TimedOut => new ComponentStatus("Docker", ComponentState.Warning, "상태 확인 시간 초과"),
            _ => new ComponentStatus("Docker", ComponentState.Stopped, "workmachine 중지됨"),
        };
    }

    private async Task<ComponentStatus> GetTailscaleStatusAsync(CancellationToken cancellationToken)
    {
        var tailscale = ExecutableLocator.FindTailscale();
        if (tailscale is null)
        {
            return new ComponentStatus("Tailscale", ComponentState.Unavailable, "tailscale.exe 없음");
        }

        var result = await ProcessRunner.RunAsync(
            tailscale,
            new[] { "status", "--json" },
            _paths.RepositoryRoot,
            TimeSpan.FromSeconds(8),
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return new ComponentStatus("Tailscale", ComponentState.Stopped, "연결되지 않음");
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var backendState = document.RootElement.GetProperty("BackendState").GetString();

            return string.Equals(backendState, "Running", StringComparison.OrdinalIgnoreCase)
                ? new ComponentStatus("Tailscale", ComponentState.Ready, "RUNNING")
                : new ComponentStatus("Tailscale", ComponentState.Stopped, backendState ?? "STOPPED");
        }
        catch
        {
            return new ComponentStatus("Tailscale", ComponentState.Warning, "상태 JSON 분석 실패");
        }
    }

    private async Task<ComponentStatus> GetFunnelStatusAsync(CancellationToken cancellationToken)
    {
        var tailscale = ExecutableLocator.FindTailscale();
        if (tailscale is null)
        {
            return new ComponentStatus("Funnel", ComponentState.Unavailable, "tailscale.exe 없음");
        }

        var result = await ProcessRunner.RunAsync(
            tailscale,
            new[] { "funnel", "status" },
            _paths.RepositoryRoot,
            TimeSpan.FromSeconds(8),
            cancellationToken: cancellationToken);

        var output = (result.StandardOutput + result.StandardError).Trim();

        if (result.Success && output.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            var firstUrl = output
                .Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(item => item.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            return new ComponentStatus("Funnel", ComponentState.Ready, firstUrl ?? "활성");
        }

        return new ComponentStatus("Funnel", ComponentState.Stopped, "비활성");
    }

    private async Task<ComponentStatus> GetMcpStatusAsync(CancellationToken cancellationToken)
    {
        var docker = ExecutableLocator.FindDocker();
        if (docker is null)
        {
            return new ComponentStatus("MCP", ComponentState.Unavailable, "Docker 없음");
        }

        var portResult = await ProcessRunner.RunAsync(
            docker,
            new[] { "port", "workmachine", "2999/tcp" },
            _paths.RepositoryRoot,
            TimeSpan.FromSeconds(8),
            cancellationToken: cancellationToken);

        if (!portResult.Success)
        {
            return new ComponentStatus("MCP", ComponentState.Stopped, "gateway 포트 없음");
        }

        var match = PortRegex.Match(portResult.StandardOutput.Trim());
        if (!match.Success || !int.TryParse(match.Groups["port"].Value, out var port))
        {
            return new ComponentStatus("MCP", ComponentState.Warning, "gateway 포트 분석 실패");
        }

        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(4),
            };

            using var response = await client.GetAsync(
                $"http://127.0.0.1:{port}/health",
                cancellationToken);

            return response.IsSuccessStatusCode
                ? new ComponentStatus("MCP", ComponentState.Ready, $"READY · 127.0.0.1:{port}")
                : new ComponentStatus("MCP", ComponentState.Warning, $"HTTP {(int)response.StatusCode}");
        }
        catch
        {
            return new ComponentStatus("MCP", ComponentState.Stopped, $"127.0.0.1:{port} 응답 없음");
        }
    }
}
