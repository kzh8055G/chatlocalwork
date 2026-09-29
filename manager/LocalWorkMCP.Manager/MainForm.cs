using System.Diagnostics;

namespace LocalWorkMCP.Manager;

internal sealed class MainForm : Form
{
    private static readonly string[] ComponentNames =
    {
        "Windows Runner",
        "Docker",
        "Tailscale",
        "Funnel",
        "MCP",
    };

    private readonly AppPaths _paths;
    private readonly MpcLifecycleService _lifecycleService;
    private readonly StatusService _statusService;
    private readonly Dictionary<string, Label> _stateLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _detailLabels = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _statusTimer;

    private readonly Button _startButton = new() { Text = "Start MCP", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop MCP", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "상태 새로 고침", AutoSize = true };
    private readonly Button _loadLogButton = new() { Text = "Runner 로그", AutoSize = true };
    private readonly Button _openRepositoryButton = new() { Text = "프로젝트 폴더", AutoSize = true };
    private readonly RichTextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        DetectUrls = false,
        Font = new Font("Consolas", 9F),
        BackColor = SystemColors.Window,
    };
    private readonly Label _overallLabel = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Text = "상태 확인 중...",
    };

    private bool _busy;

    public MainForm(AppPaths paths)
    {
        _paths = paths;
        _lifecycleService = new MpcLifecycleService(paths);
        _statusService = new StatusService(paths);

        Text = "LocalWorkMCP Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 620);
        Size = new Size(920, 700);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(BuildLayout());

        _startButton.Click += async (_, _) => await RunLifecycleAsync(start: true);
        _stopButton.Click += async (_, _) => await RunLifecycleAsync(start: false);
        _refreshButton.Click += async (_, _) => await RefreshStatusAsync();
        _loadLogButton.Click += (_, _) => LoadRunnerLog();
        _openRepositoryButton.Click += (_, _) => OpenRepository();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();

        Shown += async (_, _) =>
        {
            AppendLog($"Repository: {_paths.RepositoryRoot}");
            AppendLog($"Workspace : {_paths.WorkspaceRoot}");
            await RefreshStatusAsync();
            _statusTimer.Start();
        };

        FormClosed += (_, _) => _statusTimer.Dispose();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Fill };

        var title = new Label
        {
            Text = "LocalWorkMCP Manager",
            AutoSize = true,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            Location = new Point(0, 0),
        };

        var subtitle = new Label
        {
            Text = "Windows Runner · Docker · Tailscale · MCP 상태 및 실행 관리",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(2, 40),
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };

        foreach (var button in new[]
                 {
                     _startButton,
                     _stopButton,
                     _refreshButton,
                     _loadLogButton,
                     _openRepositoryButton,
                 })
        {
            button.Height = 32;
            button.Padding = new Padding(8, 0, 8, 0);
            actions.Controls.Add(button);
        }

        actions.Controls.Add(new Label { Text = "     ", AutoSize = true });
        actions.Controls.Add(_overallLabel);

        var statusGroup = new GroupBox
        {
            Text = "상태",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };

        var statusTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = ComponentNames.Length + 1,
        };

        statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        statusTable.Controls.Add(CreateHeaderLabel("구성 요소"), 0, 0);
        statusTable.Controls.Add(CreateHeaderLabel("상태"), 1, 0);
        statusTable.Controls.Add(CreateHeaderLabel("상세"), 2, 0);

        for (var i = 0; i < ComponentNames.Length; i++)
        {
            var name = ComponentNames[i];
            var row = i + 1;

            statusTable.Controls.Add(new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, row);

            var stateLabel = new Label
            {
                Text = "-",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            };

            var detailLabel = new Label
            {
                Text = "-",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };

            _stateLabels[name] = stateLabel;
            _detailLabels[name] = detailLabel;

            statusTable.Controls.Add(stateLabel, 1, row);
            statusTable.Controls.Add(detailLabel, 2, row);
        }

        statusGroup.Controls.Add(statusTable);

        var logGroup = new GroupBox
        {
            Text = "실행 로그",
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };
        logGroup.Controls.Add(_logBox);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(statusGroup, 0, 2);
        root.Controls.Add(logGroup, 0, 3);

        return root;
    }

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };
    }

    private async Task RunLifecycleAsync(bool start)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        _statusTimer.Stop();

        var operation = start ? "START" : "STOP";
        AppendLog(string.Empty);
        AppendLog($"===== {operation} {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");

        try
        {
            var result = start
                ? await _lifecycleService.StartAsync(AppendLog)
                : await _lifecycleService.StopAsync(AppendLog);

            if (result.Success)
            {
                AppendLog($"[{operation}] 완료");
            }
            else if (result.TimedOut)
            {
                AppendLog($"[{operation}] 시간 초과");
            }
            else
            {
                AppendLog($"[{operation}] 실패 · ExitCode={result.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[{operation}] ERROR: {ex.Message}");
            MessageBox.Show(
                ex.Message,
                "LocalWorkMCP Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync();
            _statusTimer.Start();
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (_busy)
        {
            return;
        }

        _refreshButton.Enabled = false;

        try
        {
            var statuses = await _statusService.GetStatusAsync();

            foreach (var status in statuses)
            {
                if (_stateLabels.TryGetValue(status.Name, out var stateLabel))
                {
                    stateLabel.Text = StateText(status.State);
                    stateLabel.ForeColor = StateColor(status.State);
                }

                if (_detailLabels.TryGetValue(status.Name, out var detailLabel))
                {
                    detailLabel.Text = status.Detail;
                }
            }

            var readyCount = statuses.Count(status => status.State == ComponentState.Ready);
            _overallLabel.Text = readyCount == statuses.Count
                ? "전체 상태: READY"
                : $"전체 상태: {readyCount}/{statuses.Count} READY";
            _overallLabel.ForeColor = readyCount == statuses.Count
                ? Color.ForestGreen
                : SystemColors.ControlText;
        }
        catch (Exception ex)
        {
            _overallLabel.Text = "상태 확인 실패";
            _overallLabel.ForeColor = Color.Firebrick;
            AppendLog($"[STATUS] {ex.Message}");
        }
        finally
        {
            _refreshButton.Enabled = !_busy;
        }
    }

    private void LoadRunnerLog()
    {
        var candidates = new[] { _paths.RunnerLogFile, _paths.LauncherLogFile };
        var existing = candidates.Where(File.Exists).ToArray();

        if (existing.Length == 0)
        {
            AppendLog("[LOG] Runner 로그 파일이 없습니다.");
            return;
        }

        foreach (var file in existing)
        {
            AppendLog(string.Empty);
            AppendLog($"----- {file} -----");

            try
            {
                var text = ReadTail(file, 64 * 1024);
                foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    AppendLog(line);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[LOG] {ex.Message}");
            }
        }
    }

    private static string ReadTail(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var offset = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(offset, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        if (offset > 0)
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                text = text[(firstNewLine + 1)..];
            }
        }

        return text;
    }

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { _paths.RepositoryRoot },
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[OPEN] {ex.Message}");
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _startButton.Enabled = !busy;
        _stopButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _loadLogButton.Enabled = !busy;
        _openRepositoryButton.Enabled = !busy;
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(text));
            return;
        }

        _logBox.AppendText(text + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private static string StateText(ComponentState state) => state switch
    {
        ComponentState.Ready => "READY",
        ComponentState.Stopped => "STOPPED",
        ComponentState.Warning => "WARNING",
        ComponentState.Unavailable => "N/A",
        _ => "-",
    };

    private static Color StateColor(ComponentState state) => state switch
    {
        ComponentState.Ready => Color.ForestGreen,
        ComponentState.Stopped => Color.Firebrick,
        ComponentState.Warning => Color.DarkOrange,
        ComponentState.Unavailable => SystemColors.GrayText,
        _ => SystemColors.ControlText,
    };
}
