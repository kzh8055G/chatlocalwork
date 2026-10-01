using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace ChatLocalWork.Installer;

internal static class Program
{
    private static readonly HttpClient Http = new();

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var install = args.Any(arg => arg.Equals("--install", StringComparison.OrdinalIgnoreCase));
        var checkOnly = args.Length == 0 || args.Any(arg => arg.Equals("--check", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("ChatLocalWork Installer");
        Console.WriteLine(install ? "Mode: install" : "Mode: check");
        Console.WriteLine();

        var paths = InstallPaths.Create();
        Directory.CreateDirectory(paths.DataRoot);
        Directory.CreateDirectory(paths.ToolsRoot);

        var states = Inspect(paths);
        Print(states);

        if (checkOnly && !install)
        {
            return states.All(x => x.Ready) ? 0 : 2;
        }

        if (!install)
        {
            Console.Error.WriteLine("Use --check or --install.");
            return 64;
        }

        try
        {
            await InstallSystemPackagesAsync(states);
            await EnsurePortableNodeAsync(paths);

            Console.WriteLine();
            Console.WriteLine("Final check");
            states = Inspect(paths);
            Print(states);

            return states.All(x => x.Ready) ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
    }

    private static List<ComponentState> Inspect(InstallPaths paths)
    {
        return new()
        {
            new("Docker Desktop", FindDocker() is not null,
                FindDocker() ?? "not installed"),
            new("Tailscale", FindTailscale() is not null,
                FindTailscale() ?? "not installed"),
            new("Portable Node", File.Exists(paths.NodeExe),
                File.Exists(paths.NodeExe) ? paths.NodeExe : "not installed"),
        };
    }

    private static void Print(IEnumerable<ComponentState> states)
    {
        foreach (var state in states)
        {
            Console.WriteLine($"{state.Name,-16}: {(state.Ready ? "READY" : "MISSING")} · {state.Detail}");
        }
    }

    private static async Task InstallSystemPackagesAsync(IReadOnlyList<ComponentState> states)
    {
        var packages = new[]
        {
            (Name: "Docker Desktop", Id: "Docker.DockerDesktop"),
            (Name: "Tailscale", Id: "Tailscale.Tailscale"),
        };

        var missing = packages
            .Where(package => states.Any(state => state.Name == package.Name && !state.Ready))
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
                    "--id", package.Id,
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

    private static async Task EnsurePortableNodeAsync(InstallPaths paths)
    {
        if (File.Exists(paths.NodeExe))
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
                    string.Equals(file.GetString(), "win-x64-zip", StringComparison.OrdinalIgnoreCase)));

        if (release.ValueKind == JsonValueKind.Undefined ||
            !release.TryGetProperty("version", out var versionElement))
        {
            throw new InvalidOperationException("Could not resolve a Windows x64 Node.js LTS release.");
        }

        var version = versionElement.GetString()
            ?? throw new InvalidOperationException("Node.js release version is empty.");
        var archiveName = $"node-{version}-win-x64.zip";
        var downloadUrl = $"https://nodejs.org/dist/{version}/{archiveName}";
        var tempRoot = Path.Combine(paths.TempRoot, "node");
        var archivePath = Path.Combine(tempRoot, archiveName);
        var extractPath = Path.Combine(tempRoot, "extract");

        Directory.CreateDirectory(tempRoot);
        if (Directory.Exists(extractPath))
        {
            Directory.Delete(extractPath, recursive: true);
        }

        Console.WriteLine($"==> Downloading Node.js {version}");
        using (var download = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            download.EnsureSuccessStatusCode();
            await using var source = await download.Content.ReadAsStreamAsync();
            await using var destination = File.Create(archivePath);
            await source.CopyToAsync(destination);
        }

        ZipFile.ExtractToDirectory(archivePath, extractPath);

        var extractedRoot = Directory.GetDirectories(extractPath, "node-*-win-x64")
            .SingleOrDefault()
            ?? throw new InvalidOperationException("Unexpected Node.js archive layout.");

        var staging = paths.NodeRoot + ".staging";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.Move(extractedRoot, staging);
        if (Directory.Exists(paths.NodeRoot))
        {
            Directory.Delete(paths.NodeRoot, recursive: true);
        }

        Directory.Move(staging, paths.NodeRoot);

        if (!File.Exists(paths.NodeExe))
        {
            throw new InvalidOperationException("Portable Node installation did not produce node.exe.");
        }

        Console.WriteLine($"Portable Node: {paths.NodeExe}");
    }

    private static string? FindWinget()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return FindExecutable(
            "winget.exe",
            string.IsNullOrWhiteSpace(localAppData)
                ? null
                : Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe"));
    }

    private static string? FindDocker()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return FindExecutable(
            "docker.exe",
            Path.Combine(programFiles, "Docker", "Docker", "resources", "bin", "docker.exe"));
    }

    private static string? FindTailscale()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return FindExecutable(
            "tailscale.exe",
            Path.Combine(programFiles, "Tailscale", "tailscale.exe"),
            Path.Combine(programFilesX86, "Tailscale", "tailscale.exe"));
    }

    private static string? FindExecutable(string name, params string?[] preferred)
    {
        foreach (var path in preferred)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDirectory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(rawDirectory.Trim().Trim('"'), name);
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
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.WriteLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {executable}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            process.WaitForExit();
            return process.ExitCode;
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

            throw new TimeoutException($"{Path.GetFileName(executable)} timed out.");
        }
    }

    private sealed record ComponentState(string Name, bool Ready, string Detail);

    private sealed record InstallPaths(
        string DataRoot,
        string ToolsRoot,
        string NodeRoot,
        string NodeExe,
        string TempRoot)
    {
        public static InstallPaths Create()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("LOCALAPPDATA could not be resolved.");
            }

            var dataRoot = Path.Combine(localAppData, "ChatLocalWork");
            var toolsRoot = Path.Combine(dataRoot, "tools");
            var nodeRoot = Path.Combine(toolsRoot, "node");
            return new InstallPaths(
                dataRoot,
                toolsRoot,
                nodeRoot,
                Path.Combine(nodeRoot, "node.exe"),
                Path.Combine(dataRoot, "installer-cache"));
        }
    }
}
