namespace ChatLocalWork.Manager;

internal sealed class EnvironmentSettingsForm : Form
{
    private readonly AppPaths _paths;
    private readonly BootstrapService _bootstrapService;
    private readonly Action<string> _externalLog;

    private readonly Label _workspaceLabel = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private readonly Label _summaryLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        Text = "환경 상태 확인 중...",
    };

    private readonly ListView _componentList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
    };

    private readonly Button _inspectButton = new() { Text = "환경 점검", AutoSize = true };
    private readonly Button _tailscaleLoginButton = new() { Text = "Tailscale 로그인", AutoSize = true };
    private readonly Button _workspaceButton = new() { Text = "Workspace 변경", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "닫기", AutoSize = true };

    private ManagerSettings _settings;
    private string _displayWorkspace;
    private bool _busy;

    public EnvironmentSettingsForm(AppPaths paths, Action<string> externalLog)
    {
        _paths = paths;
        _bootstrapService = new BootstrapService(paths);
        _externalLog = externalLog;
        _settings = ManagerSettingsStore.Load(_paths.ManagerSettingsFile);
        _displayWorkspace = _paths.WorkspaceRoot;

        Text = "ChatLocalWork 환경 설정";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 500);
        Size = new Size(760, 560);
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        _componentList.Columns.Add("항목", 150);
        _componentList.Columns.Add("상태", 130);
        _componentList.Columns.Add("설명", 390);

        Controls.Add(BuildLayout());

        _inspectButton.Click += async (_, _) => await RunEnvironmentCheckAsync();
        _tailscaleLoginButton.Click += async (_, _) => await RunTailscaleLoginAsync();
        _workspaceButton.Click += (_, _) => ChangeWorkspace();
        _closeButton.Click += (_, _) => Close();

        Shown += async (_, _) => await RunEnvironmentCheckAsync();
    }

    public bool StartMcpRequested { get; private set; }

    public bool RestartRequired { get; private set; }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8),
        };
        titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titlePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "환경 설정",
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
        };

        var subtitle = new Label
        {
            Text = "ChatLocalWork 실행 환경과 Workspace를 관리합니다.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, 0, 0, 0),
        };

        titlePanel.Controls.Add(title, 0, 0);
        titlePanel.Controls.Add(subtitle, 0, 1);

        var workspaceGroup = new GroupBox
        {
            Text = "Workspace",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 8),
        };

        var workspaceTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
        };
        workspaceTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspaceTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        workspaceTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspaceTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _workspaceButton.MinimumSize = new Size(0, 34);
        _workspaceButton.Padding = new Padding(7, 2, 7, 2);

        _workspaceLabel.Text = _displayWorkspace;
        workspaceTable.Controls.Add(_workspaceLabel, 0, 0);
        workspaceTable.SetColumnSpan(_workspaceLabel, 2);

        var workspaceHint = new Label
        {
            Text = "변경한 Workspace는 Manager 재시작 후 Windows Runner와 Docker /shared에 적용됩니다.",
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        workspaceTable.Controls.Add(workspaceHint, 0, 1);
        workspaceTable.Controls.Add(_workspaceButton, 1, 1);
        workspaceGroup.Controls.Add(workspaceTable);

        var environmentGroup = new GroupBox
        {
            Text = "환경 상태",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 8, 10, 10),
        };

        var environmentTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        environmentTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        environmentTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        environmentTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 6, 0, 6),
        };

        foreach (var button in new[] { _inspectButton, _tailscaleLoginButton })
        {
            button.MinimumSize = new Size(0, 34);
            button.Padding = new Padding(7, 2, 7, 2);
            actions.Controls.Add(button);
        }

        environmentTable.Controls.Add(_summaryLabel, 0, 0);
        environmentTable.Controls.Add(_componentList, 0, 1);
        environmentTable.Controls.Add(actions, 0, 2);
        environmentGroup.Controls.Add(environmentTable);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 10, 0, 4),
        };
        _closeButton.MinimumSize = new Size(0, 34);
        _closeButton.Padding = new Padding(10, 2, 10, 2);
        footer.Controls.Add(_closeButton);

        root.Controls.Add(titlePanel, 0, 0);
        root.Controls.Add(workspaceGroup, 0, 1);
        root.Controls.Add(environmentGroup, 0, 2);
        root.Controls.Add(footer, 0, 3);

        return root;
    }

    private async Task RunEnvironmentCheckAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "환경 점검 중...");

        try
        {
            var result = await LoadEnvironmentAsync();
            SetSummary(result);
        }
        catch (Exception ex)
        {
            Log($"환경 점검 실패 · {ex.Message}");
            _summaryLabel.Text = "환경 점검 실패";
            _summaryLabel.ForeColor = Color.Firebrick;
            MessageBox.Show(
                this,
                ex.Message,
                "환경 점검 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunTailscaleLoginAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "Tailscale 로그인 진행 중...");
        StartMcpRequested = false;

        try
        {
            var loggedIn = await _bootstrapService.StartTailscaleLoginAsync(Log);
            if (!loggedIn)
            {
                _summaryLabel.Text = "Tailscale 로그인 미완료";
                _summaryLabel.ForeColor = Color.DarkOrange;
                return;
            }

            var result = await LoadEnvironmentAsync();
            SetSummary(result);

            if (result.Ready && !RestartRequired)
            {
                StartMcpRequested = true;
                _summaryLabel.Text = "환경 READY · 이 창을 닫으면 MCP를 시작합니다.";
                _summaryLabel.ForeColor = Color.ForestGreen;
            }
        }
        catch (Exception ex)
        {
            Log($"Tailscale 로그인 실패 · {ex.Message}");
            _summaryLabel.Text = "Tailscale 로그인 오류";
            _summaryLabel.ForeColor = Color.Firebrick;
            MessageBox.Show(
                this,
                ex.Message,
                "Tailscale 로그인 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<BootstrapResult> LoadEnvironmentAsync()
    {
        var result = await _bootstrapService.PrepareAsync(Log);

        _componentList.BeginUpdate();
        try
        {
            _componentList.Items.Clear();

            foreach (var component in result.Components)
            {
                Log($"환경 · {component.Name} · {component.State} · {component.Detail}");

                var item = new ListViewItem(component.Name);
                item.SubItems.Add(FormatState(component.State));
                item.SubItems.Add(component.Detail);
                _componentList.Items.Add(item);
            }
        }
        finally
        {
            _componentList.EndUpdate();
        }

        return result;
    }

    private void SetSummary(BootstrapResult result)
    {
        if (RestartRequired)
        {
            _summaryLabel.Text = "Workspace 변경됨 · Manager 재시작 필요";
            _summaryLabel.ForeColor = Color.DarkOrange;
            return;
        }

        _summaryLabel.Text = result.Ready ? "환경 READY" : result.Message;
        _summaryLabel.ForeColor = result.Ready ? Color.ForestGreen : Color.DarkOrange;
    }

    private void ChangeWorkspace()
    {
        if (_busy)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = $"새 Workspace 폴더를 선택하세요.\r\n현재: {_displayWorkspace}",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_displayWorkspace)
                ? _displayWorkspace
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK ||
            string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            var selectedPath = Path.GetFullPath(dialog.SelectedPath);
            Directory.CreateDirectory(selectedPath);

            _settings = _settings with { WorkspaceRoot = selectedPath };
            ManagerSettingsStore.Save(_paths.ManagerSettingsFile, _settings);

            _displayWorkspace = selectedPath;
            _workspaceLabel.Text = selectedPath + "  (재시작 후 적용)";
            RestartRequired = !PathsEqual(selectedPath, _paths.WorkspaceRoot);
            StartMcpRequested = false;

            Log($"Workspace 변경 예약 · {selectedPath}");

            _summaryLabel.Text = RestartRequired
                ? "Workspace 변경됨 · Manager 재시작 필요"
                : "현재 Workspace와 동일한 경로입니다.";
            _summaryLabel.ForeColor = RestartRequired
                ? Color.DarkOrange
                : SystemColors.ControlText;
        }
        catch (Exception ex)
        {
            Log($"Workspace 변경 실패 · {ex.Message}");
            MessageBox.Show(
                this,
                ex.Message,
                "Workspace 변경 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SetBusy(bool busy, string message = "")
    {
        _busy = busy;
        _inspectButton.Enabled = !busy;
        _tailscaleLoginButton.Enabled = !busy;
        _workspaceButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
        UseWaitCursor = busy;

        if (busy)
        {
            _summaryLabel.Text = message;
            _summaryLabel.ForeColor = Color.SteelBlue;
        }
    }

    private void Log(string message)
    {
        _externalLog(message);
    }

    private static string FormatState(BootstrapComponentState state)
    {
        return state switch
        {
            BootstrapComponentState.Ready => "READY",
            BootstrapComponentState.Missing => "MISSING",
            BootstrapComponentState.UserActionRequired => "ACTION REQUIRED",
            BootstrapComponentState.Error => "ERROR",
            _ => state.ToString(),
        };
    }

    private static bool PathsEqual(string left, string right)
    {
        static string Normalize(string value) =>
            Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
