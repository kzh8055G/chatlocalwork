namespace ChatLocalWork.Installer;

internal sealed record InstallerOptions(
    bool Install,
    string? DataRoot,
    string? WorkspaceRoot)
{
    public static InstallerOptions Parse(string[] args)
    {
        var install = false;
        var check = args.Length == 0;
        string? dataRoot = null;
        string? workspaceRoot = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--install":
                    install = true;
                    break;
                case "--check":
                    check = true;
                    break;
                case "--data-root":
                    dataRoot = NextValue(args, ref i, arg);
                    break;
                case "--workspace-root":
                    workspaceRoot = NextValue(args, ref i, arg);
                    break;
                case "--help":
                case "-h":
                case "/?":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        if (install && check)
        {
            check = false;
        }

        if (!install && !check)
        {
            throw new ArgumentException("Specify --check or --install.");
        }

        return new InstallerOptions(
            install,
            NormalizeOptionalPath(dataRoot),
            NormalizeOptionalPath(workspaceRoot));
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            Usage:
              ChatLocalWork.Installer.exe --check
              ChatLocalWork.Installer.exe --install [options]

            Options:
              --data-root <path>       Override %LOCALAPPDATA%\ChatLocalWork.
              --workspace-root <path>  Override Documents\ChatLocalWorkWorkspace.
            """);
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }
}
