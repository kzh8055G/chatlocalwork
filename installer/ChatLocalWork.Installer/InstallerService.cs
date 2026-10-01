using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace ChatLocalWork.Installer;

internal sealed record ComponentState(
    string Name,
    bool Ready,
    string Detail);

internal sealed class InstallerService
{
    private const string RepositoryUrl = "https://github.com/kzh8055G/chatlocalwork.git";
    private const string RepositoryBranch = "main";

    private static readonly HttpClient Http = new();

    private readonly InstallPaths _paths;
    private readonly InstallerOptions _options;

    public InstallerService(InstallPaths paths, InstallerOptions options)
    {
        _paths = paths;
        _options = options;
    }

    public List<ComponentState> Inspect()
    {
        var git = FindGit();
        var docker = FindDocker();
        var tailscale = FindTailscale();

        return new List<ComponentState>
        {
            new(
                "Git",
                git is not null,
                git ?? "not installed"),
            new(
                "Docker Desktop",
                docker is not null,
                docker ?? "not installed"),
            new(
                "Tailscale",
                tailscale is not null,
                tailscale ?? "not installed"),
            new(
                "Portable Node",
                File.Exists(_paths.NodeExe),
                File.Exists(_paths.NodeExe) ? _paths.NodeExe : "not installed"),
            new(
                "ChatLocalWork app",
                HasValidAppRoot(_paths.AppRoot),
                HasValidAppRoot(_paths.AppRoot) ? _paths.AppRoot : "not installed"),
        };
    }

    public static void Print(IEnumerable<ComponentState> states)
    {
        foreach (var state in states)
        {
            Console.WriteLine(
                $"{state.Name,-18}: {(state.Ready ? "READY" : "MISSING")} · {state.Detail}");
        }
    }

    public async Task InstallAsync()
    {
        Directory.CreateDirectory(_paths.DataRoot);
        Directory.CreateDirectory(_paths.ToolsRoot);
        Directory.CreateDirectory(_paths.AppDirectory);
        Directory.CreateDirectory(_paths.WorkspaceRoot);
        Directory.CreateDirectory(_paths.TempRoot);

        await InstallSystemPackagesAsync();
        await EnsurePortableNodeAsync();
        await EnsureAppRepositoryAsync();
    }

    private async Task InstallSystemPackagesAsync()
    {
        var packages = new[]
        {
            (
                Name: "Git",
                PackageId: "Git.Git",
                Locate: (Func<string?>)FindGit),
            (
                Name: "Docker Desktop",
                PackageId: "Docker.DockerDesktop",
                Locate: (Func<string?>)FindDocker),
            (
                Name: "Tailscale",
                PackageId: "Tailscale.Tailscale",
                Locate: (Func<string?>)FindTailscale),
        };

        var missing = packages
            .Where(package => package.Locate() is null)
            .ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        var winget = FindWinget()
            ?? throw new InvalidOperationException(
                "winget.exe was not found. Install/update Windows App Installer first.");

        foreach (var package in missing)
        {
            Console.WriteLine();
            Console.WriteLine($"==> Installing {package.Name}");

            var exitCode = await RunAsync(
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
                TimeSpan.FromMinutes(30));

            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{package.Name} installation failed. ExitCode={exitCode}");
            }
        }
    }

    private async Task EnsurePortableNodeAsync()
    {
        if (File.Exists(_paths.NodeExe))
        {
            Console.WriteLine();
            Console.WriteLine("==> Portable Node already installed");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("==> Resolving current Node.js LTS");

        using var response = await Http.GetAsync("https://nodejs.org/dist/index.json");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        var release = document.RootElement
            .EnumerateArray()
            .FirstOrDefault(item =>
                item.TryGetProperty("lts", out var lts) &&
                lts.ValueKind == JsonValueKind.String &&
                item.TryGetProperty("files", out var files) &&
                files.EnumerateArray().Any(file =>
                    string.Equals(
                        file.GetString(),
                        "win-x64-zip",
                        StringComparison.OrdinalIgnoreCase)));

        if (release.ValueKind == JsonValueKind.Undefined ||
            !release.TryGetProperty("version", out var versionElement))
        {
            throw new InvalidOperationException(
                "Could not resolve a Windows x64 Node.js LTS release.");
        }

        var version = versionElement.GetString()
            ?? throw new InvalidOperationException("Node.js release version is empty.");
        var archiveName = $"node-{version}-win-x64.zip";
        var downloadUrl = $"https://nodejs.org/dist/{version}/{archiveName}";
        var tempRoot = Path.Combine(_paths.TempRoot, "node");
        var archivePath = Path.Combine(tempRoot, archiveName);
        var extractPath = Path.Combine(tempRoot, "extract");

        RecreateDirectory(tempRoot);

        Console.WriteLine($"==> Downloading Node.js {version}");
        using (var download = await Http.GetAsync(
                   downloadUrl,
                   HttpCompletionOption.ResponseHeadersRead))
        {
            download.EnsureSuccessStatusCode();
            await using var source = await download.Content.ReadAsStreamAsync();
            await using var destination = File.Create(archivePath);
            await source.CopyToAsync(destination);
        }

        ZipFile.ExtractToDirectory(archivePath, extractPath);

        var extractedRoot = Directory
            .GetDirectories(extractPath, "node-*-win-x64")
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                "Unexpected Node.js archive layout.");

        var staging = _paths.NodeRoot + ".staging";
        DeleteDirectoryIfExists(staging);
        Directory.Move(extractedRoot, staging);

        DeleteDirectoryIfExists(_paths.NodeRoot);
        Directory.Move(staging, _paths.NodeRoot);

        if (!File.Exists(_paths.NodeExe))
        {
            throw new InvalidOperationException(
                "Portable Node installation did not produce node.exe.");
        }

        Console.WriteLine($"Portable Node: {_paths.NodeExe}");
    }

    private async Task EnsureAppRepositoryAsync()
    {
        var git = FindGit()
            ?? throw new InvalidOperationException(
                "git.exe was not found after dependency installation.");

        Console.WriteLine();
        Console.WriteLine("==> Installing ChatLocalWork app source");

        var staging = Path.Combine(_paths.AppDirectory, ".staging");
        DeleteDirectoryIfExists(staging);

        var cloneExitCode = await RunAsync(
            git,
            new[]
            {
                "clone",
                "--depth", "1",
                "--branch", RepositoryBranch,
                "--single-branch",
                RepositoryUrl,
                staging,
            },
            TimeSpan.FromMinutes(10));

        if (cloneExitCode != 0)
        {
            DeleteDirectoryIfExists(staging);
            throw new InvalidOperationException(
                $"ChatLocalWork clone failed. ExitCode={cloneExitCode}");
        }

        if (!HasValidAppRoot(staging))
        {
            DeleteDirectoryIfExists(staging);
            throw new InvalidOperationException(
                "Cloned repository does not contain a valid ChatLocalWork app layout.");
        }

        var previous = Path.Combine(_paths.AppDirectory, ".previous");
        DeleteDirectoryIfExists(previous);

        if (Directory.Exists(_paths.AppRoot))
        {
            Directory.Move(_paths.AppRoot, previous);
        }

        try
        {
            Directory.Move(staging, _paths.AppRoot);
            DeleteDirectoryIfExists(previous);
        }
        catch
        {
            if (!Directory.Exists(_paths.AppRoot) && Directory.Exists(previous))
            {
                Directory.Move(previous, _paths.AppRoot);
            }

            throw;
        }

        var head = await CaptureAsync(
            git,
            new[] { "rev-parse", "HEAD" },
            _paths.AppRoot,
            TimeSpan.FromSeconds(20));

        var versionFile = Path.Combine(_paths.AppRoot, ".chatlocalwork-version");
        File.WriteAllText(
            versionFile,
            string.IsNullOrWhiteSpace(head) ? "unknown" : head.Trim());

        Console.WriteLine($"ChatLocalWork app: {_paths.AppRoot}");
    }

    private static bool HasValidAppRoot(string root)
    {
        return File.Exists(Path.Combine(
                   root,
                   "scripts",
                   "windows",
                   "StartMCP.cjs")) &&
               File.Exists(Path.Combine(
                   root,
                   "localworkmcp",
                   "tunneling",
                   "docker-compose.yml")) &&
               File.Exists(Path.Combine(
                   root,
                   "windows-runner",
                   "runner.cjs"));
    }

    private static string? FindWinget()
    {
        var localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return FindExecutable(
            "winget.exe",
            string.IsNullOrWhiteSpace(localAppData)
                ? null
                : Path.Combine(
                    localAppData,
                    "Microsoft",
                    "WindowsApps",
                    "winget.exe"));
    }

    private static string? FindGit()
    {
        var programFiles =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return FindExecutable(
            "git.exe",
            Path.Combine(programFiles, "Git", "cmd", "git.exe"),
            Path.Combine(programFilesX86, "Git", "cmd", "git.exe"),
            string.IsNullOrWhiteSpace(localAppData)
                ? null
                : Path.Combine(
                    localAppData,
                    "Programs",
                    "Git",
                    "cmd",
                    "git.exe"));
    }

    private static string? FindDocker()
    {
        var programFiles =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return FindExecutable(
            "docker.exe",
            Path.Combine(
                programFiles,
                "Docker",
                "Docker",
                "resources",
                "bin",
                "docker.exe"),
            string.IsNullOrWhiteSpace(localAppData)
                ? null
                : Path.Combine(
                    localAppData,
                    "Programs",
                    "DockerDesktop",
                    "resources",
                    "bin",
                    "docker.exe"));
    }

    private static string? FindTailscale()
    {
        var programFiles =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return FindExecutable(
            "tailscale.exe",
            Path.Combine(programFiles, "Tailscale", "tailscale.exe"),
            Path.Combine(programFilesX86, "Tailscale", "tailscale.exe"));
    }

    private static string? FindExecutable(
        string name,
        params string?[] preferred)
    {
        foreach (var path in preferred)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        var pathValue =
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDirectory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate =
                    Path.Combine(rawDirectory.Trim().Trim('"'), name);
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

    private static async Task<int> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        string? workingDirectory = null)
    {
        var result = await RunProcessAsync(
            executable,
            arguments,
            workingDirectory,
            timeout,
            captureOnly: false);

        return result.ExitCode;
    }

    private static async Task<string> CaptureAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory,
        TimeSpan timeout)
    {
        var result = await RunProcessAsync(
            executable,
            arguments,
            workingDirectory,
            timeout,
            captureOnly: true);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} failed. ExitCode={result.ExitCode}");
        }

        return result.StandardOutput;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        bool captureOnly)
    {
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = captureOnly,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stdout.AppendLine(e.Data);
            if (!captureOnly)
            {
                Console.WriteLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stderr.AppendLine(e.Data);
            if (!captureOnly)
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start {executable}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException(
                $"{Path.GetFileName(executable)} timed out.");
        }

        return new ProcessResult(
            process.ExitCode,
            stdout.ToString(),
            stderr.ToString());
    }

    private static void RecreateDirectory(string path)
    {
        DeleteDirectoryIfExists(path);
        Directory.CreateDirectory(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
