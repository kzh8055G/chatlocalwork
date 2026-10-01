namespace ChatLocalWork.Installer;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        InstallerOptions options;
        try
        {
            options = InstallerOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            InstallerOptions.PrintUsage();
            return 64;
        }

        Console.WriteLine("ChatLocalWork Installer");
        Console.WriteLine(options.Install ? "Mode: install" : "Mode: check");
        Console.WriteLine();

        try
        {
            var paths = InstallPaths.Create(options);
            var installer = new InstallerService(paths, options);

            var states = installer.Inspect();
            InstallerService.Print(states);

            if (!options.Install)
            {
                return states.All(state => state.Ready) ? 0 : 2;
            }

            await installer.InstallAsync();

            Console.WriteLine();
            Console.WriteLine("Final check");
            states = installer.Inspect();
            InstallerService.Print(states);

            return states.All(state => state.Ready) ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
    }
}
