namespace ChatLocalWork.Manager;

internal sealed record DependencyInstallResult(
    bool Success,
    bool Changed,
    string Message);

internal sealed class DependencyInstallerService
{
    private static readonly (string Name, string PackageId, Func<string?> Locate)[] Packages =
    {
        ("Docker Desktop", "Docker.DockerDesktop", ExecutableLocator.FindDocker),
        ("Tailscale", "Tailscale.Tailscale", ExecutableLocator.FindTailscale),
    };

    public bool HasMissingPackages()
    {
        return Packages.Any(package => package.Locate() is null);
    }

    public async Task<DependencyInstallResult> InstallMissingAsync(
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var missing = Packages
            .Where(package => package.Locate() is null)
            .ToArray();

        if (missing.Length == 0)
        {
            return new DependencyInstallResult(
                Success: true,
                Changed: false,
                Message: "Docker Desktop과 Tailscale이 이미 설치되어 있습니다.");
        }

        var winget = ExecutableLocator.FindWinget();
        if (winget is null)
        {
            return new DependencyInstallResult(
                Success: false,
                Changed: false,
                Message: "winget을 찾을 수 없습니다. Windows App Installer를 먼저 준비해야 합니다.");
        }

        var changed = false;

        foreach (var package in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log($"Dependency · {package.Name} 설치 시작");

            var result = await ProcessRunner.RunAsync(
                winget,
                new[]
                {
                    "install",
                    "--id", package.PackageId,
                    "--exact",
                    "--accept-package-agreements",
                    "--accept-source-agreements",
                    "--disable-interactivity",
                },
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                TimeSpan.FromMinutes(20),
                line => log($"winget · {line}"),
                cancellationToken);

            if (!result.Success)
            {
                var detail = result.TimedOut
                    ? "설치 시간이 초과되었습니다."
                    : LastUsefulLine(result.StandardError)
                      ?? LastUsefulLine(result.StandardOutput)
                      ?? $"ExitCode={result.ExitCode}";

                return new DependencyInstallResult(
                    Success: false,
                    Changed: changed,
                    Message: $"{package.Name} 설치 실패: {detail}");
            }

            changed = true;
            log($"Dependency · {package.Name} 설치 완료");
        }

        return new DependencyInstallResult(
            Success: true,
            Changed: changed,
            Message: "필수 외부 앱 설치가 완료되었습니다. Tailscale은 로그인이 필요할 수 있습니다.");
    }

    private static string? LastUsefulLine(string text)
    {
        return text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0);
    }
}
