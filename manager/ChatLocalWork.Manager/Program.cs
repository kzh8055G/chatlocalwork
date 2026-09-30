namespace ChatLocalWork.Manager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var paths = AppPaths.Discover();
            Application.Run(new MainForm(paths));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "ChatLocalWork Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
