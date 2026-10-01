using System.Text.Json;

namespace ChatLocalWork.Manager;

internal enum BootstrapComponentState
{
    Ready,
    Missing,
    UserActionRequired,
    Error,
}

internal sealed record BootstrapComponentStatus(
    string Name,
    BootstrapComponentState State,
    string Detail);

internal sealed record BootstrapResult(
    bool Ready,
    bool RequiresUserAction,
    string Message,
    IReadOnlyList<BootstrapComponentStatus> Components);

internal sealed class BootstrapService
{
    private readonly AppPaths _paths;

    public BootstrapService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<BootstrapResult> PrepareAsync(
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        if (!_paths.InstalledLayout)
        {
            log("Bootstrap · 개발 환경에서는 자동 구성을 건너뜁니다.");
            return await InspectAsync(cancellationToken);
        }

        Directory.CreateDirectory(_paths.ChatLocalWorkDataDirectory);
        Directory.CreateDirectory(_paths.WorkspaceRoot);

        if (_paths.HasValidAppRoot())
        {
            EnsureInstalledAppConfig(log);
        }

        var tailscale = ExecutableLocator.FindTailscale();
        if (tailscale is not null)
        {
            var publicUrl = await TryGetTailscalePublicUrlAsync(
                tailscale,
                cancellationToken);

            if (publicUrl is not null && _paths.HasValidAppRoot())
            {
                EnsureMcpEnvironment(publicUrl);
            }
        }

        return await InspectAsync(cancellationToken);
    }

    public async Task<bool> StartTailscaleLoginAsync(
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var tailscale = ExecutableLocator.FindTailscale();
        if (tailscale is null)
        {
            log("Tailscale 로그인 · tailscale.exe 없음");
            return false;
        }

        if (await IsTailscaleLoggedInAsync(tailscale, cancellationToken))
        {
            log("Tailscale 로그인 · 이미 로그인되어 있습니다.");
            return true;
        }

        log("Tailscale 로그인 · 브라우저 인증을 시작합니다.");

        var workingDirectory = Directory.Exists(_paths.AppRoot)
            ? _paths.AppRoot
            : _paths.ChatLocalWorkDataDirectory;

        var result = await ProcessRunner.RunAsync(
            tailscale,
            new[] { "up" },
            workingDirectory,
            TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            var detail = (result.StandardError + Environment.NewLine + result.StandardOutput).Trim();
            log($"Tailscale 로그인 실패 · {(string.IsNullOrWhiteSpace(detail) ? $"ExitCode={result.ExitCode}" : detail)}");
            return false;
        }

        var ready = await IsTailscaleLoggedInAsync(tailscale, cancellationToken);
        log(ready
            ? "Tailscale 로그인 · 완료"
            : "Tailscale 로그인 · 명령은 완료됐지만 RUNNING 상태가 아닙니다.");
        return ready;
    }

    public async Task<BootstrapResult> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        var components = new List<BootstrapComponentStatus>();

        components.Add(new BootstrapComponentStatus(
            "App source",
            _paths.HasValidAppRoot()
                ? BootstrapComponentState.Ready
                : BootstrapComponentState.Missing,
            _paths.HasValidAppRoot()
                ? _paths.AppRoot
                : "ChatLocalWork app source 없음"));

        var node = ExecutableLocator.FindNode(_paths.NodePath);
        components.Add(new BootstrapComponentStatus(
            "Node",
            node is not null
                ? BootstrapComponentState.Ready
                : BootstrapComponentState.Missing,
            node ?? "Portable/System Node 없음"));

        var docker = ExecutableLocator.FindDocker();
        components.Add(new BootstrapComponentStatus(
            "Docker Desktop",
            docker is not null
                ? BootstrapComponentState.Ready
                : BootstrapComponentState.Missing,
            docker ?? "Docker Desktop 없음"));

        var tailscale = ExecutableLocator.FindTailscale();
        if (tailscale is null)
        {
            components.Add(new BootstrapComponentStatus(
                "Tailscale",
                BootstrapComponentState.Missing,
                "Tailscale 없음"));
        }
        else
        {
            var loginReady = await IsTailscaleLoggedInAsync(
                tailscale,
                cancellationToken);

            components.Add(new BootstrapComponentStatus(
                "Tailscale",
                loginReady
                    ? BootstrapComponentState.Ready
                    : BootstrapComponentState.UserActionRequired,
                loginReady
                    ? "설치 및 로그인 완료"
                    : "설치됨 · 로그인 필요"));
        }

        var envFile = Path.Combine(
            _paths.LocalWorkMcpRoot,
            "tunneling",
            ".env");

        var envReady = File.Exists(envFile) &&
                       File.ReadLines(envFile)
                           .Any(line => line.StartsWith(
                               "MCP_PUBLIC_URL=https://",
                               StringComparison.OrdinalIgnoreCase));

        components.Add(new BootstrapComponentStatus(
            "MCP config",
            envReady
                ? BootstrapComponentState.Ready
                : BootstrapComponentState.Missing,
            envReady ? ".env READY" : "MCP_PUBLIC_URL 설정 필요"));

        var ready = components.All(
            component => component.State == BootstrapComponentState.Ready);

        var userAction = components.Any(
            component => component.State == BootstrapComponentState.UserActionRequired);

        return new BootstrapResult(
            ready,
            userAction,
            ready ? "READY" : "환경 준비 필요",
            components);
    }

    private void EnsureInstalledAppConfig(Action<string> log)
    {
        if (File.Exists(_paths.AppConfigFile))
        {
            return;
        }

        var configDirectory = Path.GetDirectoryName(_paths.AppConfigFile)
            ?? throw new InvalidOperationException(
                "app-config.json directory를 확인할 수 없습니다.");
        Directory.CreateDirectory(configDirectory);

        var versionFile = Path.Combine(
            _paths.AppRoot,
            ".chatlocalwork-version");
        var appVersion = File.Exists(versionFile)
            ? File.ReadAllText(versionFile).Trim()
            : null;

        var config = new
        {
            appRoot = _paths.AppRoot,
            workspaceRoot = _paths.WorkspaceRoot,
            nodePath = _paths.NodePath,
            appVersion = string.IsNullOrWhiteSpace(appVersion)
                ? null
                : appVersion,
        };

        var json = JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        File.WriteAllText(
            _paths.AppConfigFile,
            json + Environment.NewLine);

        log($"Bootstrap · app-config 생성 · {_paths.AppConfigFile}");
    }

    private async Task<string?> TryGetTailscalePublicUrlAsync(
        string tailscale,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Directory.Exists(_paths.AppRoot)
            ? _paths.AppRoot
            : _paths.ChatLocalWorkDataDirectory;

        var result = await ProcessRunner.RunAsync(
            tailscale,
            new[] { "status", "--json" },
            workingDirectory,
            TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;

            if (!root.TryGetProperty("BackendState", out var backend) ||
                !string.Equals(
                    backend.GetString(),
                    "Running",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!root.TryGetProperty("Self", out var self) ||
                !self.TryGetProperty("DNSName", out var dnsNameElement))
            {
                return null;
            }

            var dnsName = dnsNameElement.GetString()?.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(dnsName)
                ? null
                : "https://" + dnsName;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureMcpEnvironment(string publicUrl)
    {
        var envFile = Path.Combine(
            _paths.LocalWorkMcpRoot,
            "tunneling",
            ".env");

        Directory.CreateDirectory(
            Path.GetDirectoryName(envFile)
            ?? throw new InvalidOperationException(
                ".env directory를 확인할 수 없습니다."));

        var lines = File.Exists(envFile)
            ? File.ReadAllLines(envFile).ToList()
            : new List<string>();

        SetEnvValue(
            lines,
            "SHARED_PATH",
            _paths.WorkspaceRoot.Replace('\\', '/'));
        SetEnvValue(
            lines,
            "MCP_PUBLIC_URL",
            publicUrl.TrimEnd('/'));
        SetEnvValue(lines, "TZ", "UTC");
        SetEnvValue(
            lines,
            "WINDOWS_RUNNER_QUEUE_DIR",
            "/chatlocalwork-runtime/windows-runner");
        SetEnvValue(
            lines,
            "WINDOWS_RUNNER_TIMEOUT_MS",
            "120000");

        File.WriteAllLines(envFile, lines);
    }

    private static void SetEnvValue(
        List<string> lines,
        string key,
        string value)
    {
        var prefix = key + "=";
        var index = lines.FindIndex(
            line => line.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            lines[index] = prefix + value;
        }
        else
        {
            lines.Add(prefix + value);
        }
    }

    private async Task<bool> IsTailscaleLoggedInAsync(
        string tailscale,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Directory.Exists(_paths.AppRoot)
            ? _paths.AppRoot
            : _paths.ChatLocalWorkDataDirectory;

        var result = await ProcessRunner.RunAsync(
            tailscale,
            new[] { "status", "--json" },
            workingDirectory,
            TimeSpan.FromSeconds(10),
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            return document.RootElement.TryGetProperty(
                       "BackendState",
                       out var state) &&
                   string.Equals(
                       state.GetString(),
                       "Running",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
