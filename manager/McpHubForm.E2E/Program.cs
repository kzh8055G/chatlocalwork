using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ChatLocalWork.Manager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: McpHubForm.E2E <node.exe> <everything-index.js> <workspace>");
            return 2;
        }

        var nodeExe = Path.GetFullPath(args[0]);
        var everythingEntry = Path.GetFullPath(args[1]);
        var workspace = Path.GetFullPath(args[2]);
        if (!File.Exists(nodeExe) || !File.Exists(everythingEntry) || !Directory.Exists(workspace))
        {
            throw new FileNotFoundException("E2E input path is missing.");
        }

        var dataRoot = Path.Combine(Path.GetTempPath(), "chatlocalwork-mcphub-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var prompts = new TestPrompts(["echo", "get-sum"]);
            using var form = new McpHubForm(dataRoot, workspace, prompts);
            form.Show();
            Application.DoEvents();

            Field<TextBox>(form, "_id").Text = "everything_gui_test";
            Field<TextBox>(form, "_name").Text = "Everything GUI E2E";
            Field<ComboBox>(form, "_transport").SelectedIndex = 2;
            Field<TextBox>(form, "_command").Text = nodeExe;
            Field<TextBox>(form, "_cwd").Text = workspace;
            Field<TextBox>(form, "_args").Lines = [everythingEntry, "stdio"];
            Field<TextBox>(form, "_envNames").Clear();
            Field<TextBox>(form, "_tools").Clear();
            Field<CheckBox>(form, "_enabled").Checked = true;
            Application.DoEvents();

            var inspect = FindButton(form, "도구 조회...");
            inspect.PerformClick();
            PumpUntil(
                () => inspect.Enabled &&
                      Field<TextBox>(form, "_tools").Lines.Contains("echo") &&
                      Field<TextBox>(form, "_tools").Lines.Contains("get-sum"),
                TimeSpan.FromSeconds(60),
                "도구 조회가 완료되지 않았습니다.");

            var selectedTools = Field<TextBox>(form, "_tools").Lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToArray();
            if (!selectedTools.SequenceEqual(["echo", "get-sum"]))
            {
                throw new InvalidOperationException("도구 선택 결과 불일치: " + string.Join(",", selectedTools));
            }

            var beforeMessages = prompts.Messages.Count;
            var verify = FindButton(form, "연결 확인...");
            verify.PerformClick();
            PumpUntil(
                () => verify.Enabled && prompts.Messages.Count > beforeMessages,
                TimeSpan.FromSeconds(60),
                "연결 확인이 완료되지 않았습니다.");

            var verification = prompts.Messages.Skip(beforeMessages).Last();
            if (!verification.Message.Contains("연결에 성공", StringComparison.Ordinal) ||
                !verification.Message.Contains("13개", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("연결 확인 결과 불일치: " + verification.Message);
            }

            var save = FindButton(form, "추가 / 저장");
            save.PerformClick();
            Application.DoEvents();

            var registryFile = Path.Combine(dataRoot, "config", "mcp-servers.json");
            var profileFile = Path.Combine(dataRoot, "private", "mcp-stdio-profiles.json");
            PumpUntil(
                () => File.Exists(registryFile) && File.Exists(profileFile),
                TimeSpan.FromSeconds(10),
                "Registry 또는 stdio profile이 저장되지 않았습니다.");

            ValidateRegistry(registryFile);
            ValidateProfile(profileFile, nodeExe, everythingEntry, workspace);

            if (prompts.Confirmations.Count < 3)
            {
                throw new InvalidOperationException(
                    $"예상한 승인 흐름보다 확인 횟수가 적습니다: {prompts.Confirmations.Count}");
            }

            form.Close();
            Application.DoEvents();

            Console.WriteLine("MCP_HUB_FORM_E2E_PASS");
            Console.WriteLine($"CONFIRMATIONS {prompts.Confirmations.Count}");
            Console.WriteLine("TOOLS echo,get-sum");
            Console.WriteLine("VERIFY 13");
            return 0;
        }
        finally
        {
            try { Directory.Delete(dataRoot, recursive: true); } catch { }
        }
    }

    private static T Field<T>(McpHubForm form, string name) where T : class
        => typeof(McpHubForm)
               .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
               ?.GetValue(form) as T
           ?? throw new MissingFieldException(typeof(McpHubForm).FullName, name);

    private static Button FindButton(Control root, string text)
        => Descendants(root)
               .OfType<Button>()
               .FirstOrDefault(button => button.Text == text)
           ?? throw new InvalidOperationException("버튼을 찾지 못했습니다: " + text);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string error)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > timeout)
            {
                throw new TimeoutException(error);
            }

            Application.DoEvents();
            Thread.Sleep(20);
        }

        Application.DoEvents();
    }

    private static void ValidateRegistry(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var server = json.RootElement.GetProperty("servers").EnumerateArray().Single();
        if (server.GetProperty("id").GetString() != "everything_gui_test" ||
            server.GetProperty("transport").GetString() != "windowsStdio" ||
            !server.GetProperty("enabled").GetBoolean())
        {
            throw new InvalidDataException("Registry 서버 설정이 올바르지 않습니다.");
        }

        var tools = server.GetProperty("allowedTools").EnumerateArray()
            .Select(value => value.GetString()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!tools.SequenceEqual(["echo", "get-sum"]))
        {
            throw new InvalidDataException("Registry 허용 도구가 올바르지 않습니다.");
        }
    }

    private static void ValidateProfile(
        string file,
        string nodeExe,
        string everythingEntry,
        string workspace)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var profile = json.RootElement.GetProperty("servers").EnumerateArray().Single();
        if (profile.GetProperty("id").GetString() != "everything_gui_test" ||
            !profile.GetProperty("approved").GetBoolean() ||
            !string.Equals(profile.GetProperty("command").GetString(), nodeExe, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(profile.GetProperty("cwd").GetString(), workspace, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("stdio 승인 프로필 기본 설정이 올바르지 않습니다.");
        }

        var args = profile.GetProperty("args").EnumerateArray().Select(value => value.GetString()!).ToArray();
        if (args.Length != 2 ||
            !string.Equals(args[0], everythingEntry, StringComparison.OrdinalIgnoreCase) ||
            args[1] != "stdio")
        {
            throw new InvalidDataException("stdio 승인 프로필 인수가 올바르지 않습니다.");
        }

        var tools = profile.GetProperty("allowedTools").EnumerateArray()
            .Select(value => value.GetString()!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!tools.SequenceEqual(["echo", "get-sum"]))
        {
            throw new InvalidDataException("stdio 승인 프로필 허용 도구가 올바르지 않습니다.");
        }
    }

    private sealed class TestPrompts(IReadOnlyCollection<string> toolsToSelect) : IMcpHubPrompts
    {
        private readonly HashSet<string> _toolsToSelect = new(toolsToSelect, StringComparer.Ordinal);

        internal List<(string Title, string Message, MessageBoxIcon Icon)> Messages { get; } = [];
        internal List<(string Title, string Message, MessageBoxIcon Icon)> Confirmations { get; } = [];

        public bool Confirm(IWin32Window owner, string message, string title, MessageBoxIcon icon)
        {
            Confirmations.Add((title, message, icon));
            return true;
        }

        public void Show(IWin32Window owner, string message, string title, MessageBoxIcon icon)
            => Messages.Add((title, message, icon));

        public IReadOnlyList<string>? SelectTools(
            IWin32Window owner,
            IReadOnlyList<string> tools,
            IReadOnlyCollection<string> selectedTools)
            => tools.Where(_toolsToSelect.Contains).ToArray();
    }
}
